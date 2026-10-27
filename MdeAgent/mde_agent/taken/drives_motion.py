"""
Bewegingstesten voor Ingenia/Novanta-drives (Everest/Capitan) via ingeniamotion.

drives.encodertest — encodertest met de interne generator (zoals de feedback-test in MotionLab):
    het magnetische veld wordt in open lus (modus Current, commutatie op de interne generator,
    zaagtand) precies één omwenteling gedraaid — één generatorcyclus is bij de Summit-firmware
    één mechanische omwenteling, de drive rekent zelf met de poolparen — eerst vooruit en dan terug. De positieterugkoppeling moet daarbij de encoderresolutie
    (bv. 131072 increments) laten zien, binnen een marge (standaard 2 %), en vooruit/terug
    moeten symmetrisch zijn. Alle aangeraakte registers worden hersteld.

    Parameters: positie, dictionary, verwachtIncrementen (leeg = resolutie uit de drive),
                margePct (2), frequentie (Hz van de zaagtand, 1 cyclus = 1 elektrische omwenteling, 0.5),
                stroomPct (deel van de nominale stroom, 0.8)
    Encoderstatus: de foutbuffer van de drive wordt bij de start onthouden en het statuswoord tijdens de test
    bekeken; elke nieuwe melding van de feedbackmodule (0x73xx: BiSS-C-warningbit, foutbits, encoder los) keurt
    de test af, ook een waarschuwing (bv. een encoder die niet goed uitgelijnd is). Andere meldingen staan in
    het resultaat (sommige foutopties zet de test zelf tijdelijk op 'waarschuwing').

drives.motiontest — bewegingstest in gesloten lus (Profile Position): per snelheid één omwenteling
    vooruit en één terug, x keer herhaald. Controleert dat elke positie binnen de tijd gehaald wordt
    en bewaakt de volgfout. Meet per beweging de motorstroom (|Iq|): max, gemiddeld en RMS.

    Parameters: positie, dictionary, snelheden ("60,120,300" in rpm), herhalingen (3),
                acceleratie (rev/s², 500), incrementen (per omwenteling; leeg = resolutie uit de drive),
                maxVolgfoutPct (100 = 1 omwenteling, % van een omwenteling), timeoutFactor (3 × de theoretische tijd + 2 s),
                maxStroom (A; leeg/0 = niet beoordelen, alleen meten)
"""

import time

from . import _hulp, _ingenia

# registers die de encodertest aanraakt — vooraf lezen, achteraf terugzetten
_BACKUP = ["MOT_PAIR_POLES", "DRV_OP_CMD", "CL_CUR_Q_SET_POINT", "CL_CUR_D_SET_POINT",
           "FBK_GEN_MODE", "FBK_GEN_FREQ", "FBK_GEN_GAIN", "FBK_GEN_OFFSET", "FBK_GEN_CYCLES",
           "COMMU_ANGLE_SENSOR", "COMMU_PHASING_MODE", "MOT_COMMU_MOD",
           "ERROR_DIGENC_AGAINST_HALL_OPTION", "CL_VEL_FOLLOWING_OPTION", "ERROR_VEL_OUT_LIMITS_OPTION",
           "ERROR_POS_OUT_LIMITS_OPTION", "ERROR_POS_FOLLOWING_OPTION",
           "COMMU_ANGLE_INTEGRITY1_OPTION", "COMMU_ANGLE_INTEGRITY2_OPTION",
           "PROF_POS_OPTION_CODE", "CL_POS_REF_MAX_RANGE_LIMIT", "CL_POS_REF_MIN_RANGE_LIMIT"]


def _backup(mc, job) -> dict:
    uit = {}
    for uid in _BACKUP:
        try:
            uit[uid] = _ingenia.sdo(mc.communication.get_register, uid)
        except Exception:  # noqa: BLE001
            pass
    job.log(f"{len(uit)} registers veiliggesteld voor herstel.")
    return uit


def _herstel(mc, backup: dict, job) -> None:
    fouten = 0
    for uid, waarde in backup.items():
        try:
            _ingenia.sdo(mc.communication.set_register, uid, waarde, job=job)
        except Exception:  # noqa: BLE001
            fouten += 1
    job.log(f"Registers hersteld ({len(backup) - fouten} ok{', ' + str(fouten) + ' niet' if fouten else ''}).")
    if fouten and fouten == len(backup):
        job.log("LET OP: de drive antwoordt niet meer op de bus (van de bus gevallen / herstart?). De testinstellingen "
                "staan alleen in het werkgeheugen: spanning uit/aan zet de opgeslagen parameterset terug.")
    _ingenia.laatste_fout(mc, job)


def _resolutie(mc, job) -> int | None:
    """Increments per omwenteling van de positieterugkoppeling, uit de drive."""
    try:
        sensor = mc.configuration.get_position_feedback()
        res = int(mc.configuration.get_feedback_resolution(sensor))
        job.log(f"Positieterugkoppeling {getattr(sensor, 'name', sensor)}: {res} increments per omwenteling.")
        return res
    except Exception as e:  # noqa: BLE001
        job.log(f"  resolutie niet uit de drive te lezen: {e}")
        return None


def _positie(mc) -> int:
    return int(_ingenia.sdo(mc.motion.get_actual_position))


def _live_positie(job, mc, res: int, extra: dict | None = None) -> int:
    """Positie lezen en als tussenstand in job.resultaat zetten (de app tekent er de wijzerplaat mee)."""
    p = _positie(mc)
    single = p % res
    stand = {"live": True, "positie": p, "resolutie": res, "singleturn": single, "graden": round(single / res * 360, 2),
             "multiturn": (p - single) // res}
    if extra:
        stand.update(extra)
    job.resultaat = stand
    return p


def _wacht_met_live(job, mc, res: int, seconden: float, extra: dict | None = None, bewaking=None) -> None:
    """Wachten terwijl de positie ~20×/s wordt doorgegeven (en zo nodig het statuswoord bekeken)."""
    einde = time.time() + seconden
    while time.time() < einde:
        try:
            _live_positie(job, mc, res, extra)
        except Exception:  # noqa: BLE001
            pass
        if bewaking is not None:
            bewaking.kijk()
        time.sleep(0.05)


def _verschil(a: int, b: int) -> int:
    """a − b met 32-bit overloop: de positie van een multiturn-encoder zit vaak vlak bij −2^31."""
    return ((a - b + 2**31) % 2**32) - 2**31


_volgfout_bron: str | None = None      # welk register de volgfout geeft (één keer bepalen)


def _volgfout(mc, actueel: int) -> int | None:
    """Positievolgfout in increments: CL_POS_ERR_VALUE als dat bestaat, anders demand − werkelijk."""
    global _volgfout_bron
    for bron in ([_volgfout_bron] if _volgfout_bron else ["CL_POS_ERR_VALUE", "CL_POS_REF_VALUE"]):
        try:
            w = int(_ingenia.sdo(mc.communication.get_register, bron))
            _volgfout_bron = bron
            return abs(w) if bron == "CL_POS_ERR_VALUE" else abs(_verschil(w, actueel))
        except Exception:  # noqa: BLE001
            continue
    return None


# ---------------------------------------------------------------------------------------------
#  Encodertest
# ---------------------------------------------------------------------------------------------

class Encodertest:
    NAAM = "drives.encodertest"
    OMSCHRIJVING = "Encodertest: veld 1 omwenteling draaien (open lus), increments vergelijken"

    @staticmethod
    def beschikbaar(inst: dict):
        return _ingenia.beschikbaar()

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        from ingeniamotion.enums import CommutationMode, OperationMode, SensorType

        positie = int(parameters.get("positie") or 1)
        xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")
        marge = _ingenia.getal(parameters, "margePct", 2.0)
        freq = _ingenia.getal(parameters, "frequentie", 0.5)
        stroom_deel = _ingenia.getal(parameters, "stroomPct", 0.8)
        verwacht_param = _ingenia.getal(parameters, "verwachtIncrementen", 0)

        _ingenia.sdo_herhalingen = 0
        with _ingenia.verbinding(inst, positie, xdf, job) as mc:
            melding = _ingenia.sto_vrij(mc, job)
            if melding:
                job.klaar({"geslaagd": False, "sto_ontbreekt": True, "samenvatting": melding})
                return

            res = _resolutie(mc, job)
            verwacht = int(verwacht_param) if verwacht_param > 0 else (res or 0)
            if verwacht <= 0:
                raise RuntimeError("Verwachte increments onbekend: geef 'verwachtIncrementen' op (bv. 131072).")
            pp = int(mc.configuration.get_motor_pair_poles())
            if pp <= 0:
                raise RuntimeError("Poolparen (MOT_PAIR_POLES) niet gelezen.")
            nominaal = min(float(_ingenia.sdo(mc.communication.get_register, "MOT_RATED_CURRENT")),
                           float(_ingenia.sdo(mc.communication.get_register, "DRV_PROT_MAN_MAX_CONT_CURRENT_VALUE")))
            stroom = nominaal * stroom_deel
            job.log(f"Poolparen {pp}; 1 omwenteling = 1 generatorcyclus op {freq:g} Hz ({1 / freq:.1f} s per richting); "
                    f"teststroom {stroom:.2f} A ({stroom_deel:.0%} van {nominaal:.2f} A); verwacht {verwacht} increments ±{marge:g} %.")
            job.voortgang(10)

            backup = _backup(mc, job)
            _ingenia.sdo(mc.motion.motor_disable)
            bewaking = _ingenia.Foutbewaking(mc, job)      # encoderstatus: warning en error tijdens de test
            bewaking.kijk()
            if bewaking.warning_gezien or bewaking.fault_gezien:
                job.log("  (die melding stond er al vóór de test)")
            try:
                mc.configuration.set_commutation_mode(CommutationMode.SINUSOIDAL)
                for uid, w in (("PROF_POS_OPTION_CODE", 0), ("CL_POS_REF_MAX_RANGE_LIMIT", 0), ("CL_POS_REF_MIN_RANGE_LIMIT", 0)):
                    try:
                        _ingenia.sdo(mc.communication.set_register, uid, w)
                    except Exception:  # noqa: BLE001
                        pass
                mc.motion.set_internal_generator_configuration(OperationMode.CURRENT, pair_poles=pp)
                for uid in ("ERROR_DIGENC_AGAINST_HALL_OPTION", "CL_VEL_FOLLOWING_OPTION", "ERROR_VEL_OUT_LIMITS_OPTION",
                            "ERROR_POS_OUT_LIMITS_OPTION", "ERROR_POS_FOLLOWING_OPTION",
                            "COMMU_ANGLE_INTEGRITY1_OPTION", "COMMU_ANGLE_INTEGRITY2_OPTION"):
                    try:
                        _ingenia.sdo(mc.communication.set_register, uid, 1)      # volgfouten → waarschuwing
                    except Exception:  # noqa: BLE001
                        pass
                job.log("Commutatie op interne generator, modus Current; motor inschakelen …")
                _ingenia.motor_aan(mc, job)

                # uitlijnen: 1 cyclus zaagtand terwijl de stroom oploopt
                mc.motion.internal_generator_saw_tooth_move(1, 1, freq)
                mc.motion.current_quadrature_ramp(stroom, 2 / freq)
                _wacht_met_live(job, mc, res or verwacht, 0.5, {"fase": "uitlijnen"}, bewaking)
                p0 = _positie(mc)
                job.log(f"Uitgelijnd; startpositie {p0}.")
                job.voortgang(30)

                job.log("Vooruit: 1 omwenteling …")
                mc.motion.internal_generator_saw_tooth_move(1, 1, freq)
                _wacht_met_live(job, mc, res or verwacht, 1 / freq + 0.5, {"fase": "vooruit", "start": p0}, bewaking)
                p1 = _positie(mc)
                vooruit = _verschil(p1, p0)
                job.log(f"  positie {p1}: verplaatsing {vooruit:+d} increments")
                job.voortgang(60)

                job.log("Terug: 1 omwenteling …")
                mc.motion.internal_generator_saw_tooth_move(-1, 1, freq)
                _wacht_met_live(job, mc, res or verwacht, 1 / freq + 0.5, {"fase": "terug", "start": p0}, bewaking)
                p2 = _positie(mc)
                terug = _verschil(p2, p1)
                job.log(f"  positie {p2}: verplaatsing {terug:+d} increments")
                job.voortgang(85)
            finally:
                for stap in (lambda: mc.motion.set_current_quadrature(0.0), mc.motion.motor_disable):
                    try:
                        _ingenia.sdo(stap, job=job)
                    except Exception as e:  # noqa: BLE001
                        job.log(f"  afronden: {e}")
                _herstel(mc, backup, job)
                if _ingenia.sdo_herhalingen:
                    job.log(f"LET OP: {_ingenia.sdo_herhalingen} SDO-herhaling(en) nodig gehad — de EtherCAT-verbinding hapert tijdens de test (EMI bij hoge motorstroom?).")
                meldingen = bewaking.nieuwe()

        fout_vooruit = abs(abs(vooruit) - verwacht) / verwacht * 100
        fout_terug = abs(abs(terug) - verwacht) / verwacht * 100
        symmetrie = abs(vooruit + terug) / verwacht * 100
        redenen = []
        if vooruit == 0:
            redenen.append("geen beweging gezien (bedrading/encoder)")
        if fout_vooruit > marge:
            redenen.append(f"vooruit {abs(vooruit)} i.p.v. {verwacht} ({fout_vooruit:.2f} % > {marge:g} %)")
        if fout_terug > marge:
            redenen.append(f"terug {abs(terug)} i.p.v. {verwacht} ({fout_terug:.2f} % > {marge:g} %)")
        if symmetrie > marge:
            redenen.append(f"vooruit en terug niet symmetrisch ({symmetrie:.2f} %)")
        encoder = [m for m in meldingen if m["encoder"]]
        for m in encoder:
            redenen.append(f"encoder {'waarschuwing' if m['waarschuwing'] else 'fout'}: {m['tekst']}")
        encoderstatus = ("fout" if any(not m["waarschuwing"] for m in encoder) else "waarschuwing") if encoder else "ok"
        polariteit = "normaal" if vooruit > 0 else "omgekeerd"
        geslaagd = not redenen
        samenvatting = (f"vooruit {vooruit:+d}, terug {terug:+d} increments (verwacht {verwacht}, fout {max(fout_vooruit, fout_terug):.2f} %, "
                        f"polariteit {polariteit}, encoderstatus {encoderstatus})") + ("" if geslaagd else " — " + "; ".join(redenen))
        job.log(("GESLAAGD: " if geslaagd else "AFGEKEURD: ") + samenvatting)
        job.klaar({"geslaagd": geslaagd, "samenvatting": samenvatting, "verwacht": verwacht, "vooruit": vooruit, "terug": terug,
                   "foutVooruitPct": round(fout_vooruit, 3), "foutTerugPct": round(fout_terug, 3), "symmetriePct": round(symmetrie, 3),
                   "polariteit": polariteit, "poolparen": pp, "resolutieDrive": res,
                   "encoderstatus": encoderstatus, "meldingen": meldingen})


# ---------------------------------------------------------------------------------------------
#  Motiontest
# ---------------------------------------------------------------------------------------------

class Motiontest:
    NAAM = "drives.motiontest"
    OMSCHRIJVING = "Motiontest: per snelheid 1 omwenteling vooruit en terug, x keer"

    @staticmethod
    def beschikbaar(inst: dict):
        return _ingenia.beschikbaar()

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        from ingeniamotion.enums import OperationMode

        positie = int(parameters.get("positie") or 1)
        xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")
        try:
            snelheden = [float(x) for x in str(parameters.get("snelheden") or "60,120,300").replace(";", ",").split(",") if x.strip()]
        except Exception:  # noqa: BLE001
            raise RuntimeError("Snelheden: lijst in rpm, bv. 60,120,300")
        herhalingen = max(1, int(_ingenia.getal(parameters, "herhalingen", 3)))
        acc = _ingenia.getal(parameters, "acceleratie", 500.0)
        max_volgfout_pct = _ingenia.getal(parameters, "maxVolgfoutPct", 100.0)     # % van een omwenteling (100 = 1 omw)
        timeout_factor = _ingenia.getal(parameters, "timeoutFactor", 3.0)
        inc_param = _ingenia.getal(parameters, "incrementen", 0)
        max_stroom = _ingenia.getal(parameters, "maxStroom", 0.0)                   # A; 0 = alleen meten

        with _ingenia.verbinding(inst, positie, xdf, job) as mc:
            melding = _ingenia.sto_vrij(mc, job)
            if melding:
                job.klaar({"geslaagd": False, "sto_ontbreekt": True, "samenvatting": melding})
                return
            res = _resolutie(mc, job)
            inc = int(inc_param) if inc_param > 0 else (res or 0)
            if inc <= 0:
                raise RuntimeError("Increments per omwenteling onbekend: geef 'incrementen' op (bv. 131072).")
            max_volgfout = int(inc * max_volgfout_pct / 100)

            backup = {}
            for uid in ("PROF_MAX_VEL", "PROF_MAX_ACC", "PROF_MAX_DEC", "DRV_OP_CMD"):
                try:
                    backup[uid] = _ingenia.sdo(mc.communication.get_register, uid)
                except Exception:  # noqa: BLE001
                    pass

            bewegingen = []
            fouten = []
            totaal = len(snelheden) * herhalingen * 2
            n = 0
            reeks: list[list[float]] = []      # [t, demand, werkelijk] t.o.v. de startpositie, voor de rolgrafiek
            t_start = time.time()

            def live(p_act: int, start_ref: int) -> None:
                try:
                    dem = int(_ingenia.sdo(mc.communication.get_register, "CL_POS_REF_VALUE"))
                except Exception:  # noqa: BLE001
                    dem = p_act
                reeks.append([round(time.time() - t_start, 3), _verschil(dem, start_ref), _verschil(p_act, start_ref)])
                if len(reeks) > 600:
                    del reeks[:-600]
                job.resultaat = {"live": True, "reeks": reeks[-300:], "incrementen": inc, "positie": p_act, "beweging": n + 1, "totaal": totaal}
            try:
                mc.motion.set_operation_mode(OperationMode.PROFILE_POSITION)
                mc.configuration.set_profiler(acceleration=acc, deceleration=acc, velocity=max(snelheden) / 60.0)
                # teruglezen: zo zien we in de log welke eenheden de drive werkelijk hanteert
                for uid in ("DRV_OP_CMD", "PROF_MAX_VEL", "PROF_MAX_ACC", "PROF_MAX_DEC", "CL_VEL_REF_MAX", "PROF_POS_OPTION_CODE"):
                    try:
                        job.log(f"  {uid} = {_ingenia.sdo(mc.communication.get_register, uid)}")
                    except Exception:  # noqa: BLE001
                        pass
                _ingenia.motor_aan(mc, job)
                start = _positie(mc)
                job.log(f"Volgfout uit {_volgfout_bron or 'CL_POS_ERR_VALUE / CL_POS_REF_VALUE'}." if _volgfout(mc, start) is not None else "Volgfout niet uit te lezen (geen CL_POS_ERR_VALUE/CL_POS_REF_VALUE); alleen tijd en doel worden beoordeeld.")
                job.log(f"Start op {start}; {len(snelheden)} snelheden × {herhalingen} × (vooruit + terug) = {totaal} bewegingen van {inc} increments.")
                doel = start
                for rpm in snelheden:
                    rps = rpm / 60.0
                    mc.configuration.set_max_profile_velocity(rps)
                    try:
                        job.log(f"  PROF_MAX_VEL teruggelezen: {_ingenia.sdo(mc.communication.get_register, 'PROF_MAX_VEL')} (gevraagd {rps:.3f} rev/s)")
                    except Exception:  # noqa: BLE001
                        pass
                    # theoretische tijd: trapezium met acc; timeout = factor × tijd + 2 s
                    t_acc = rps / acc
                    s_acc = 0.5 * acc * t_acc * t_acc
                    t_theorie = (2 * t_acc + (1 - 2 * s_acc) / rps) if 2 * s_acc < 1 else 2 * (1 / acc) ** 0.5
                    timeout = timeout_factor * t_theorie + 2
                    job.log(f"Snelheid {rpm:g} rpm ({rps:.2f} rev/s), ±{inc} increments, verwachte tijd {t_theorie:.2f} s per omwenteling …")
                    for h in range(herhalingen):
                        for richting, naam in ((1, "vooruit"), (-1, "terug")):
                            doel = _verschil(doel + richting * inc, 0)      # binnen int32 houden (overloop)
                            t0 = time.time()
                            volgfout_max = 0
                            v_max = 0.0
                            ok = True
                            reden = ""
                            stroom = _ingenia.Stroommeter(mc)
                            try:
                                van = _positie(mc)
                                _ingenia.sdo(mc.motion.move_to_position, doel, blocking=False)
                                einde = t0 + timeout
                                while time.time() < einde:
                                    p = _positie(mc)
                                    live(p, start)
                                    if abs(_verschil(p, doel)) <= max(20, inc // 2000):
                                        break
                                    # op hol? meer dan een halve omwenteling voorbij het doel → meteen uit
                                    afgelegd = _verschil(p, van)
                                    if abs(afgelegd) > 1.5 * inc:
                                        _ingenia.sdo(mc.motion.motor_disable)
                                        raise RuntimeError(f"op hol: {afgelegd:+d} increments verplaatst ({'goede' if afgelegd * richting > 0 else 'VERKEERDE'} richting), "
                                                           f"doel was {richting * inc:+d}; motor uitgeschakeld")
                                    vf = _volgfout(mc, p)
                                    if vf is not None:
                                        volgfout_max = max(volgfout_max, vf)
                                    try:
                                        v_max = max(v_max, abs(float(_ingenia.sdo(mc.motion.get_actual_velocity))))
                                    except Exception:  # noqa: BLE001
                                        pass
                                    stroom.meet()
                                    time.sleep(0.02)
                                else:
                                    ok = False
                                    reden = f"positie niet gehaald binnen {timeout:.1f} s (op {_positie(mc)}, doel {doel})"
                            except Exception as e:  # noqa: BLE001
                                ok = False
                                reden = str(e)
                            duur = time.time() - t0
                            if ok and volgfout_max > max_volgfout:
                                ok = False
                                reden = f"volgfout {volgfout_max} > {max_volgfout} increments"
                            i = stroom.uitkomst()
                            if ok and max_stroom > 0 and i and i["max"] > max_stroom:
                                ok = False
                                reden = f"stroom {i['max']:.2f} A > {max_stroom:g} A"
                            n += 1
                            bewegingen.append({"rpm": rpm, "herhaling": h + 1, "richting": naam, "duur": round(duur, 2),
                                               "volgfout": volgfout_max, "vmax": round(v_max, 3), "ok": ok, "reden": reden,
                                               "imax": i["max"] if i else None, "igem": i["gem"] if i else None, "irms": i["rms"] if i else None})
                            job.log(f"  {rpm:g} rpm #{h + 1} {naam}: {duur:.2f} s, snelheid max {v_max:.3f} (register-eenheid; rev/s → {v_max * 60:.0f} rpm), "
                                    f"volgfout max {volgfout_max}"
                                    + (f", stroom max {i['max']:.2f} A / gem {i['gem']:.2f} A / rms {i['rms']:.2f} A ({i['n']} metingen)" if i else ", stroom niet gemeten")
                                    + ('' if ok else ' — ' + reden))
                            job.voortgang(int(5 + 90 * n / totaal))
                            if not ok:
                                fouten.append(f"{rpm:g} rpm #{h + 1} {naam}: {reden}")
                                break
                            _wacht_rust = time.time() + 0.2
                            while time.time() < _wacht_rust:
                                try:
                                    live(_positie(mc), start)
                                except Exception:  # noqa: BLE001
                                    pass
                                time.sleep(0.03)
                        if fouten:
                            break
                    if fouten:
                        break
            finally:
                for stap in (mc.motion.motor_disable,):
                    try:
                        _ingenia.sdo(stap, job=job)
                    except Exception as e:  # noqa: BLE001
                        job.log(f"  afronden: {e}")
                for uid, w in backup.items():
                    try:
                        _ingenia.sdo(mc.communication.set_register, uid, w, job=job)
                    except Exception:  # noqa: BLE001
                        pass
                job.log("Motor uit, profiel hersteld.")
                _ingenia.laatste_fout(mc, job)

        geslaagd = not fouten and len(bewegingen) == totaal
        per_rpm = {}
        for b in bewegingen:
            d = per_rpm.setdefault(b["rpm"], {"n": 0, "duur": 0.0, "volgfout": 0, "imax": None, "igem": []})
            d["n"] += 1; d["duur"] += b["duur"]; d["volgfout"] = max(d["volgfout"], b["volgfout"])
            if b.get("imax") is not None:
                d["imax"] = max(d["imax"] or 0.0, b["imax"]); d["igem"].append(b["igem"])
        stromen = {f"{rpm:g}": {"max": d["imax"], "gem": round(sum(d["igem"]) / len(d["igem"]), 3)}
                   for rpm, d in per_rpm.items() if d["igem"]}
        samenvatting = f"{len(bewegingen)} van {totaal} bewegingen" + "".join(
            f"; {rpm:g} rpm: {d['duur'] / d['n']:.2f} s/omw, volgfout max {d['volgfout']}"
            + (f", stroom max {d['imax']:.2f} A gem {sum(d['igem']) / len(d['igem']):.2f} A" if d["igem"] else "")
            for rpm, d in per_rpm.items())
        if fouten:
            samenvatting += " — " + fouten[0]
        job.log(("GESLAAGD: " if geslaagd else "AFGEKEURD: ") + samenvatting)
        job.klaar({"geslaagd": geslaagd, "samenvatting": samenvatting, "bewegingen": bewegingen, "incrementen": inc,
                   "maxVolgfout": max_volgfout, "maxStroom": max_stroom or None, "stromen": stromen, "reeks": reeks[-600:]})


# ingangen voor taken/__init__.py
Encodertest_ = Encodertest()
Motiontest_ = Motiontest()
