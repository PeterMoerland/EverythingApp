"""
Taak drives.phasing: motor-phasing (commutatie-uitlijning) van een Ingenia-drive, zoals de
Phasing-wizard in MotionLab, mét live meetreeks:

  - methode "statisch" (standaard): ingeniamotion's tests.commutation(), de binaire zoektocht van MotionLab;
    zet COMMU_ANGLE_OFFSET en laat de phasing mode zoals hij is (Non-forced/Forced: bij elke enable opnieuw);
  - methode "dynamisch": ingeniamotion's tests.dynamic_forced_phasing(), MotionLab's dynamic phasing: het veld
    draait in open lus (Id-stroom, interne generator) één omwenteling vooruit en één terug, de drive meet
    ondertussen (monitoring) het verschil tussen commutatie- en referentiehoek; dat gemiddelde wordt de offset
    (COMMU_ANGLE_OFFSET en COMMU_ANGLE_REF_OFFSET) en de phasing mode wordt No phasing (= "Preset": geen phasing
    meer bij enable). Vraagt een absolute encoder die zowel commutatie- als referentiesensor is. Geen last van
    statische wrijving. Alleen bij SUCCESS toegepast (een WARNING = te asymmetrisch, meestal te snel gedraaid);
  - ondertussen worden ~20×/s vier registers gelezen en als tussenstand doorgegeven:
        stroom:  Current command value, Current actual value
        hoek:    Reference angle value (COMMU_ANGLE_REF_VALUE), Commutation angle value (COMMU_ANGLE_VALUE)
    (ingenialink serialiseert SDO's met een lock, dus meten naast de phasing is veilig);
  - bij succes: store, en de melding dat spanning uit/aan nodig is.

Parameters: positie, dictionary, opslaan (true), methode (statisch | dynamisch),
            dynamisch: stroom (A; leeg = 40 % van de nominale stroom, 80 % bij een vertraging), frequentie (Hz van
            de draaiing, 0.03 = 33 s per omwenteling)
Resultaat: {"geslaagd", "samenvatting", "methode", "registers": {...}, "reeks": [[t, Icmd, Iact, hoekRef, hoekCommu], ...],
            "herstartNodig": bool}
"""

import threading
import time

from . import _hulp, _ingenia

NAAM = "drives.phasing"
OMSCHRIJVING = "Motor-phasing (commutatie-uitlijning) met live stroom- en hoekgrafiek"


def beschikbaar(inst: dict):
    return _ingenia.beschikbaar()


def _uid_op_label(mc, label: str, terugval: list[str], job) -> str | None:
    """Register zoeken op het Engelse label uit de dictionary (zoals MotionLab het toont), anders op naam."""
    label = label.lower()
    try:
        drive = mc._get_drive("default")
        for sn in (1, 0):
            try:
                regs = drive.dictionary.registers(sn)
            except Exception:  # noqa: BLE001
                continue
            for uid, reg in regs.items():
                labels = getattr(reg, "labels", None) or {}
                for tekst in labels.values():
                    if str(tekst).strip().lower() == label:
                        return uid
    except Exception:  # noqa: BLE001
        pass
    for uid in terugval:
        try:
            _ingenia.sdo(mc.communication.get_register, uid)
            return uid
        except Exception:  # noqa: BLE001
            continue
    job.log(f"  register '{label}' niet gevonden (ook niet als {', '.join(terugval)})")
    return None


def voer_uit(job, inst: dict, parameters: dict) -> None:
    positie = int(parameters.get("positie") or 1)
    xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")
    opslaan = str(parameters.get("opslaan", "1")).lower() not in ("0", "false", "nee")
    dynamisch = str(parameters.get("methode") or "statisch").lower().startswith("dyn")
    stroom = _ingenia.getal(parameters, "stroom", 0.0) or None          # A; None = ingeniamotion kiest
    frequentie = _ingenia.getal(parameters, "frequentie", 0.03)
    if dynamisch and not 0.005 <= frequentie <= 1:
        raise RuntimeError("Frequentie voor dynamic phasing: tussen 0,005 en 1 Hz (standaard 0,03).")
    # verwachte duur: statisch ~12 s; dynamisch 2 omwentelingen + stroom opbouwen + monitoring
    verwacht = (2 / frequentie + 5) if dynamisch else 12.0
    limiet = (2 / frequentie + 60) if dynamisch else 120.0

    with _ingenia.verbinding(inst, positie, xdf, job) as mc:
        melding = _ingenia.sto_vrij(mc, job)
        if melding:
            job.klaar({"geslaagd": False, "sto_ontbreekt": True, "samenvatting": melding})
            return

        regs = {
            "icmd": _uid_op_label(mc, "Current command value", ["CL_CUR_CMD_VALUE", "CL_CUR_Q_REF_VALUE", "CL_CUR_REF_VALUE"], job),
            "iact": _uid_op_label(mc, "Current actual value", ["CL_CUR_VALUE", "CL_CUR_Q_VALUE"], job),
            "ref": _uid_op_label(mc, "Reference angle value", ["COMMU_ANGLE_REF_VALUE"], job),
            "commu": _uid_op_label(mc, "Commutation angle value", ["COMMU_ANGLE_VALUE"], job),
        }
        job.log("Meetregisters: " + ", ".join(f"{k} = {v or '—'}" for k, v in regs.items()))
        try:
            job.log(f"  COMMU_ANGLE_OFFSET vóór: {_ingenia.sdo(mc.communication.get_register, 'COMMU_ANGLE_OFFSET')}")
        except Exception:  # noqa: BLE001
            pass

        # phasing in een eigen thread; hier meten we ondertussen
        uitkomst: dict = {}
        fout: list[Exception] = []

        def draai():
            try:
                if dynamisch:
                    # zelf toepassen (hieronder), alleen bij SUCCESS; ingeniamotion zou het ook bij WARNING doen
                    uitkomst["r"] = mc.tests.dynamic_forced_phasing(apply_changes=False, phasing_max_current=stroom,
                                                                    spin_frequency=frequentie)
                else:
                    uitkomst["r"] = mc.tests.commutation(apply_changes=True)
            except Exception as e:  # noqa: BLE001
                fout.append(e)

        if dynamisch:
            job.log(f"Dynamic phasing (Preset) starten: 1 omwenteling vooruit en 1 terug op {frequentie:g} Hz "
                    f"(~{2 / frequentie:.0f} s), stroom {f'{stroom:g} A' if stroom else 'automatisch'}; as vrij laten …")
        else:
            job.log("Phasing starten (de as draait een stukje; as vrij laten) …")
        reeks: list[list[float]] = []
        t0 = time.time()
        t = threading.Thread(target=draai, name="phasing", daemon=True)
        t.start()
        laatste_stand = 0.0
        while t.is_alive():
            punt = [round(time.time() - t0, 3)]
            for k in ("icmd", "iact", "ref", "commu"):
                uid = regs[k]
                try:
                    punt.append(round(float(_ingenia.sdo(mc.communication.get_register, uid)), 4) if uid else None)
                except Exception:  # noqa: BLE001
                    punt.append(None)
            reeks.append(punt)
            if len(reeks) > 2000:
                del reeks[:-2000]
            job.resultaat = {"live": True, "reeks": reeks[-600:], "t": punt[0]}
            if time.time() - laatste_stand > 1:
                laatste_stand = time.time()
                job.voortgang(int(min(90, punt[0] / verwacht * 90)))
            time.sleep(0.05)
            if time.time() - t0 > limiet:
                job.log("Phasing duurt te lang; afbreken.")
                try:
                    _ingenia.sdo(mc.motion.motor_disable)
                except Exception:  # noqa: BLE001
                    pass
                break
        t.join(timeout=5)
        duur = time.time() - t0

        if fout:
            try:
                _ingenia.sdo(mc.motion.motor_disable)
            except Exception:  # noqa: BLE001
                pass
            code = _ingenia.laatste_fout(mc, job)
            tip = ""
            if dynamisch and ("absolute" in str(fout[0]).lower() or "not the same" in str(fout[0]).lower()):
                tip = " — dynamic phasing vraagt een absolute encoder als commutatie- én referentiesensor"
            raise RuntimeError(f"Phasing mislukt na {duur:.1f} s: {fout[0]}" + (f" [{code}]" if code else "") + tip)

        r = uitkomst.get("r") or {}
        if dynamisch:
            r = _dynamisch_toepassen(mc, r, job)
        ernst = r.get("result_severity") if isinstance(r, dict) else None
        tekst = r.get("result_message", "") if isinstance(r, dict) else str(r)
        voorgesteld = r.get("suggested_registers", {}) if isinstance(r, dict) else {}
        ok = ernst in (0, "0", None) or str(ernst).upper().endswith("SUCCESS")
        job.log(f"Phasing: {tekst} (severity {ernst}, {duur:.1f} s); registers: {voorgesteld}")
        try:
            job.log(f"  COMMU_ANGLE_OFFSET ná: {_ingenia.sdo(mc.communication.get_register, 'COMMU_ANGLE_OFFSET')}")
        except Exception:  # noqa: BLE001
            pass
        if ok and opslaan:
            job.log("Opslaan in de drive (store) …")
            mc.configuration.store_configuration()
        samenvatting = (("dynamisch (Preset): " if dynamisch else "statisch: ") + f"{tekst} ({duur:.1f} s)"
                        + (" — opgeslagen; spanning uit/aan" if ok and opslaan else ""))
        job.log(("GESLAAGD: " if ok else "AFGEKEURD: ") + samenvatting)
        job.klaar({"geslaagd": ok, "samenvatting": samenvatting, "methode": "dynamisch" if dynamisch else "statisch",
                   "registers": {k: str(v) for k, v in voorgesteld.items()},
                   "meetregisters": regs, "reeks": reeks, "herstartNodig": ok and opslaan})


def _dynamisch_toepassen(mc, rapport, job) -> dict:
    """Rapport van dynamic_forced_phasing (dataclass) als dict zoals tests.commutation() geeft; bij SUCCESS de
    phasing mode op No phasing (Preset) en beide offsets op de gevonden hoek zetten, zoals MotionLab."""
    if rapport is None:
        return {"result_severity": "FAIL", "result_message": "gestopt", "suggested_registers": {}}
    ernst = getattr(rapport, "result_severity", None)
    tekst = str(getattr(rapport, "result_message", "") or "")
    hoek = getattr(rapport, "commutation_angle", None)
    modus = getattr(rapport, "commutation_phasing_mode", None)
    stroom = getattr(rapport, "phasing_max_current", None)
    succes = str(getattr(ernst, "name", ernst)).upper().endswith("SUCCESS") or ernst in (0, "0")
    if stroom is not None:
        job.log(f"  gebruikte stroom {float(stroom):.3f} A; gemeten hoek {hoek}")
    registers = {}
    if succes and hoek is not None:
        registers = {"COMMU_PHASING_MODE": int(modus) if modus is not None else 2,
                     "COMMU_ANGLE_OFFSET": float(hoek), "COMMU_ANGLE_REF_OFFSET": float(hoek)}
        for uid, w in registers.items():
            _ingenia.sdo(mc.communication.set_register, uid, w, job=job)
        job.log("  toegepast: phasing mode No phasing (Preset), commutatie- en referentie-offset " + f"{float(hoek):.4f} omw")
    elif not succes:
        job.log(f"  NIET toegepast ({getattr(ernst, 'name', ernst)}): {tekst}")
    return {"result_severity": "SUCCESS" if succes else str(getattr(ernst, "name", ernst)), "result_message": tekst or "Success",
            "suggested_registers": registers}
