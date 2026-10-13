"""
Taak drives.phasing: motor-phasing (commutatie-uitlijning) van een Ingenia-drive, zoals de
Phasing-wizard in MotionLab, mét live meetreeks:

  - de phasing zelf is ingeniamotion's tests.commutation() (past COMMU_ANGLE_OFFSET toe);
  - ondertussen worden ~20×/s vier registers gelezen en als tussenstand doorgegeven:
        stroom:  Current command value, Current actual value
        hoek:    Reference angle value (COMMU_ANGLE_REF_VALUE), Commutation angle value (COMMU_ANGLE_VALUE)
    (ingenialink serialiseert SDO's met een lock, dus meten naast de phasing is veilig);
  - bij succes: store, en de melding dat spanning uit/aan nodig is.

Parameters: positie, dictionary, opslaan (true)
Resultaat: {"geslaagd", "samenvatting", "registers": {...}, "reeks": [[t, Icmd, Iact, hoekRef, hoekCommu], ...],
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
            mc.communication.get_register(uid)
            return uid
        except Exception:  # noqa: BLE001
            continue
    job.log(f"  register '{label}' niet gevonden (ook niet als {', '.join(terugval)})")
    return None


def voer_uit(job, inst: dict, parameters: dict) -> None:
    positie = int(parameters.get("positie") or 1)
    xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")
    opslaan = str(parameters.get("opslaan", "1")).lower() not in ("0", "false", "nee")

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
            job.log(f"  COMMU_ANGLE_OFFSET vóór: {mc.communication.get_register('COMMU_ANGLE_OFFSET')}")
        except Exception:  # noqa: BLE001
            pass

        # phasing in een eigen thread; hier meten we ondertussen
        uitkomst: dict = {}
        fout: list[Exception] = []

        def draai():
            try:
                uitkomst["r"] = mc.tests.commutation(apply_changes=True)
            except Exception as e:  # noqa: BLE001
                fout.append(e)

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
                    punt.append(round(float(mc.communication.get_register(uid)), 4) if uid else None)
                except Exception:  # noqa: BLE001
                    punt.append(None)
            reeks.append(punt)
            if len(reeks) > 2000:
                del reeks[:-2000]
            job.resultaat = {"live": True, "reeks": reeks[-600:], "t": punt[0]}
            if time.time() - laatste_stand > 1:
                laatste_stand = time.time()
                job.voortgang(int(min(90, punt[0] / 12 * 90)))
            time.sleep(0.05)
            if time.time() - t0 > 120:
                job.log("Phasing duurt te lang; afbreken.")
                try:
                    mc.motion.motor_disable()
                except Exception:  # noqa: BLE001
                    pass
                break
        t.join(timeout=5)
        duur = time.time() - t0

        if fout:
            try:
                mc.motion.motor_disable()
            except Exception:  # noqa: BLE001
                pass
            code = _ingenia.laatste_fout(mc, job)
            raise RuntimeError(f"Phasing mislukt na {duur:.1f} s: {fout[0]}" + (f" [{code}]" if code else ""))

        r = uitkomst.get("r") or {}
        ernst = r.get("result_severity") if isinstance(r, dict) else None
        tekst = r.get("result_message", "") if isinstance(r, dict) else str(r)
        voorgesteld = r.get("suggested_registers", {}) if isinstance(r, dict) else {}
        ok = ernst in (0, "0", None) or str(ernst).upper().endswith("SUCCESS")
        job.log(f"Phasing: {tekst} (severity {ernst}, {duur:.1f} s); registers: {voorgesteld}")
        try:
            job.log(f"  COMMU_ANGLE_OFFSET ná: {mc.communication.get_register('COMMU_ANGLE_OFFSET')}")
        except Exception:  # noqa: BLE001
            pass
        if ok and opslaan:
            job.log("Opslaan in de drive (store) …")
            mc.configuration.store_configuration()
        samenvatting = f"{tekst} ({duur:.1f} s)" + (" — opgeslagen; spanning uit/aan" if ok and opslaan else "")
        job.log(("GESLAAGD: " if ok else "AFGEKEURD: ") + samenvatting)
        job.klaar({"geslaagd": ok, "samenvatting": samenvatting, "registers": {k: str(v) for k, v in voorgesteld.items()},
                   "meetregisters": regs, "reeks": reeks, "herstartNodig": ok and opslaan})
