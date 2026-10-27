"""
Taak drives.dualmotion: twee drives tegelijk — teachen van twee posities en daarna heen en weer
bewegen (Profile Position, beide assen gelijktijdig ingeschakeld, nog zonder interpolatie).

  actie = "teach": de actuele positie van beide drives lezen (motoren uit, as met de hand verzet).
  actie = "run":   beide drives enablen, profiel (snelheid rpm, acceleratie rev/s²) zetten en
                   'herhalingen' keer: beide naar positie 2, wachten tot beide er zijn, beide naar
                   positie 1, wachten. Per beweging duur en maximale volgfout per as. Aan het eind
                   motoren uit.

Parameters:
    drives        [{"positie": 1, "dictionary": url, "naam": "Drive 1"}, {"positie": 2, ...}]
    actie         teach | run
    pos1, pos2    {"1": increments, "2": increments}  (sleutel = drive-positie; alleen bij run)
    snelheid      rpm (60)      acceleratie rev/s² (500)     herhalingen (10)
    interpoleren  true = lineair geïnterpoleerd: de as met de langste weg krijgt snelheid/acceleratie zoals
                  ingesteld, de andere evenredig minder, zodat beide assen tegelijk vertrekken én aankomen
    maxVolgfoutPct  % van een omwenteling (100)             timeoutFactor (3)
"""

import time

from . import _hulp, _ingenia
from .drives_motion import _verschil

NAAM = "drives.dualmotion"
OMSCHRIJVING = "Dual motion test: twee assen tegelijk tussen twee geteachte posities"


def beschikbaar(inst: dict):
    return _ingenia.beschikbaar()


def _positie(mc, alias: str) -> int:
    return int(_ingenia.sdo(mc.motion.get_actual_position, servo=alias))


def _resolutie(mc, alias: str, job) -> int:
    try:
        sensor = mc.configuration.get_position_feedback(servo=alias)
        return int(mc.configuration.get_feedback_resolution(sensor, servo=alias))
    except Exception as e:  # noqa: BLE001
        job.log(f"  {alias}: resolutie niet te lezen ({e}); 131072 aangenomen")
        return 131072


def voer_uit(job, inst: dict, parameters: dict) -> None:
    drives = parameters.get("drives") or []
    if len(drives) != 2:
        raise RuntimeError("Dual motion test vraagt precies twee drives.")
    actie = (parameters.get("actie") or "teach").lower()
    lijst = []
    for d in drives:
        pos = int(d.get("positie") or 0)
        xdf = _hulp.haal_firmware(d.get("dictionary") or "", job, f"dictionary_{pos}.xdf")
        lijst.append((f"d{pos}", pos, xdf, d.get("naam") or f"Drive {pos}"))
    aliassen = [(a, p, n) for a, p, _x, n in lijst]

    with _ingenia.verbinding_meerdere(inst, [(a, p, x) for a, p, x, _n in lijst], job) as mc:
        if actie == "teach":
            uit = {}
            for alias, pos, naam in aliassen:
                p = _positie(mc, alias)
                uit[str(pos)] = p
                job.log(f"  {naam} (positie {pos}): {p}")
            job.klaar({"posities": uit})
            return

        # ---- run
        snelheid = _ingenia.getal(parameters, "snelheid", 200.0)
        acc = _ingenia.getal(parameters, "acceleratie", 500.0)
        herhalingen = max(1, int(_ingenia.getal(parameters, "herhalingen", 10)))
        max_vf_pct = _ingenia.getal(parameters, "maxVolgfoutPct", 100.0)
        timeout_factor = _ingenia.getal(parameters, "timeoutFactor", 3.0)
        interpoleren = str(parameters.get("interpoleren", "true")).lower() in ("1", "true", "ja")
        pos1 = {str(k): int(v) for k, v in (parameters.get("pos1") or {}).items()}
        pos2 = {str(k): int(v) for k, v in (parameters.get("pos2") or {}).items()}
        for _a, pos, naam in aliassen:
            if str(pos) not in pos1 or str(pos) not in pos2:
                raise RuntimeError(f"Geen geteachte posities voor {naam}; eerst Teach pos 1 en Teach pos 2.")

        from ingeniamotion.enums import OperationMode

        for alias, pos, naam in aliassen:
            melding = _ingenia.sto_vrij(mc, job, servo=alias)
            if melding:
                job.klaar({"geslaagd": False, "sto_ontbreekt": True, "samenvatting": f"{naam}: {melding}"})
                return

        res = {alias: _resolutie(mc, alias, job) for alias, _p, _n in aliassen}
        rps = snelheid / 60.0
        # langste afstand bepaalt de verwachte tijd
        afstanden = {alias: abs(_verschil(pos2[str(pos)], pos1[str(pos)])) for alias, pos, _n in aliassen}
        omw_max = max(afstanden[a] / res[a] for a, _p, _n in aliassen)
        t_acc = rps / acc
        s_acc = 0.5 * acc * t_acc * t_acc
        t_theorie = (2 * t_acc + (omw_max - 2 * s_acc) / rps) if 2 * s_acc < omw_max else 2 * (omw_max / acc) ** 0.5
        timeout = timeout_factor * t_theorie + 2
        # geïnterpoleerd: per as snelheid en acceleratie schalen met (eigen weg / langste weg); dan hebben
        # beide assen hetzelfde tijdprofiel en komen ze tegelijk aan (lineaire interpolatie in de gewrichtsruimte)
        profiel = {}
        for alias, _p, naam in aliassen:
            omw = afstanden[alias] / res[alias]
            f = (omw / omw_max) if (interpoleren and omw_max > 0) else 1.0
            f = max(f, 0.02)                      # nooit 0: een stilstaande as krijgt een traag maar geldig profiel
            profiel[alias] = (rps * f, acc * f)
        job.log(f"Snelheid {snelheid:g} rpm, acceleratie {acc:g} rev/s², {herhalingen} herhalingen"
                + (", geïnterpoleerd" if interpoleren else ", onafhankelijk") + "; "
                + "; ".join(f"{n}: {afstanden[a]} incr ({afstanden[a] / res[a]:.2f} omw, {profiel[a][0] * 60:.1f} rpm / {profiel[a][1]:.0f} rev/s²)" for a, _p, n in aliassen)
                + f"; verwacht {t_theorie:.2f} s per beweging, timeout {timeout:.1f} s.")

        backup = {}
        for alias, _p, _n in aliassen:
            for uid in ("PROF_MAX_VEL", "PROF_MAX_ACC", "PROF_MAX_DEC", "DRV_OP_CMD"):
                try:
                    backup[(alias, uid)] = _ingenia.sdo(mc.communication.get_register, uid, servo=alias)
                except Exception:  # noqa: BLE001
                    pass

        bewegingen = []
        fouten = []
        totaal = herhalingen * 2
        n = 0
        try:
            for alias, _p, naam in aliassen:
                mc.motion.set_operation_mode(OperationMode.PROFILE_POSITION, servo=alias)
                v_as, a_as = profiel[alias]
                mc.configuration.set_profiler(acceleration=a_as, deceleration=a_as, velocity=v_as, servo=alias)
            for alias, _p, naam in aliassen:
                job.log(f"{naam}: motor inschakelen …")
                _ingenia.motor_aan(mc, job, servo=alias)
            job.log("Beide assen ingeschakeld.")

            for h in range(herhalingen):
                for doelnaam, doelen in (("pos 2", pos2), ("pos 1", pos1)):
                    if getattr(job, "stop_gevraagd", False):
                        fouten.append("gestopt door gebruiker")
                        break
                    t0 = time.time()
                    van = {a: _positie(mc, a) for a, _p, _n in aliassen}
                    for alias, pos, _n in aliassen:
                        _ingenia.sdo(mc.motion.move_to_position, doelen[str(pos)], servo=alias, blocking=False)
                    klaar = {a: False for a, _p, _n in aliassen}
                    vf_max = {a: 0 for a, _p, _n in aliassen}
                    ok = True
                    reden = ""
                    einde = t0 + timeout
                    while time.time() < einde and not all(klaar.values()):
                        if getattr(job, "stop_gevraagd", False):
                            ok = False
                            reden = "gestopt door gebruiker"
                            break
                        for alias, pos, naam in aliassen:
                            # drive in fout (bv. overtemperatuur)? dan niet de time-out afwachten
                            try:
                                if mc.errors.is_fault_active(servo=alias):
                                    code = _ingenia.laatste_fout(mc, job, servo=alias)
                                    ok = False
                                    reden = f"{naam} in FOUT tijdens de beweging" + (f" [{code}]" if code else "")
                                    break
                            except Exception:  # noqa: BLE001
                                pass
                            p = _positie(mc, alias)
                            doel = doelen[str(pos)]
                            if abs(_verschil(p, doel)) <= max(20, res[alias] // 2000):
                                klaar[alias] = True
                            if abs(_verschil(p, van[alias])) > afstanden[alias] + 1.5 * res[alias]:
                                for a2, _p2, _n2 in aliassen:
                                    try:
                                        _ingenia.sdo(mc.motion.motor_disable, servo=a2)
                                    except Exception:  # noqa: BLE001
                                        pass
                                raise RuntimeError(f"{naam} op hol: {_verschil(p, van[alias]):+d} increments; beide motoren uitgeschakeld")
                            try:
                                ref = int(_ingenia.sdo(mc.communication.get_register, "CL_POS_REF_VALUE", servo=alias))
                                vf_max[alias] = max(vf_max[alias], abs(_verschil(ref, p)))
                            except Exception:  # noqa: BLE001
                                pass
                        if not ok:
                            break
                        time.sleep(0.02)
                    duur = time.time() - t0
                    if ok and not all(klaar.values()):
                        ok = False
                        nietklaar = [n2 for a2, _p2, n2 in aliassen if not klaar[a2]]
                        reden = f"{', '.join(nietklaar)} niet op {doelnaam} binnen {timeout:.1f} s"
                    if not ok:
                        for a2, _p2, _n2 in aliassen:
                            try:
                                _ingenia.sdo(mc.motion.motor_disable, servo=a2)
                            except Exception:  # noqa: BLE001
                                pass
                    for alias, _p, naam in aliassen:
                        grens = int(res[alias] * max_vf_pct / 100)
                        if ok and vf_max[alias] > grens:
                            ok = False
                            reden = f"{naam}: volgfout {vf_max[alias]} > {grens}"
                    n += 1
                    rij = {"herhaling": h + 1, "doel": doelnaam, "duur": round(duur, 2), "ok": ok, "reden": reden}
                    for alias, pos, naam in aliassen:
                        rij[f"vf{pos}"] = vf_max[alias]
                    bewegingen.append(rij)
                    job.log(f"  #{h + 1} → {doelnaam}: {duur:.2f} s, volgfout max " + ", ".join(f"{naam} {vf_max[a]}" for a, _p, naam in aliassen)
                            + ("" if ok else " — " + reden))
                    job.resultaat = {"live": True, "bewegingen": bewegingen[-40:], "n": n, "totaal": totaal}
                    job.voortgang(int(5 + 90 * n / totaal))
                    if not ok:
                        fouten.append(f"#{h + 1} {doelnaam}: {reden}")
                        break
                    time.sleep(0.15)
                if fouten:
                    break
        finally:
            for alias, _p, _n in aliassen:
                try:
                    _ingenia.sdo(mc.motion.motor_disable, servo=alias, job=job)
                except Exception as e:  # noqa: BLE001
                    job.log(f"  afronden {alias}: {e}")
            for (alias, uid), w in backup.items():
                try:
                    _ingenia.sdo(mc.communication.set_register, uid, w, servo=alias, job=job)
                except Exception:  # noqa: BLE001
                    pass
            job.log("Motoren uit, profielen hersteld.")
            for alias, _p, _n in aliassen:
                _ingenia.laatste_fout(mc, job, servo=alias)

    geslaagd = not fouten and len(bewegingen) == totaal
    gem = (sum(b["duur"] for b in bewegingen) / len(bewegingen)) if bewegingen else 0
    samenvatting = f"{len(bewegingen)} van {totaal} bewegingen, gemiddeld {gem:.2f} s" + (
        "; volgfout max " + ", ".join(f"{naam} {max(b.get(f'vf{pos}', 0) for b in bewegingen)}" for _a, pos, naam in aliassen) if bewegingen else "")
    if fouten:
        samenvatting += " — " + fouten[0]
    job.log(("GESLAAGD: " if geslaagd else "AFGEKEURD: ") + samenvatting)
    job.klaar({"geslaagd": geslaagd, "samenvatting": samenvatting, "bewegingen": bewegingen, "totaal": totaal, "interpoleren": interpoleren})
