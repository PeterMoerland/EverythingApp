"""
Taak drives.encoderlive: de actuele encoderwaarde van een Ingenia-drive live doorgeven.

Zolang de job loopt wordt elke ~100 ms de positie gelezen en in job.resultaat gezet; de app
pollt /jobs/<id> en tekent er een wijzerplaat mee. Stop via /jobs/<id>/stop of na 'seconden'.

Parameters: positie, dictionary, seconden (standaard 120, max 900), interval (s, standaard 0.1)

resultaat (ook tussentijds): {"positie": ruw int32, "resolutie": increments/omw, "singleturn": 0..res-1,
                             "graden": 0..360, "multiturn": omwentelingen (overloop-veilig), "snelheid": rev/s,
                             "nabijGrens": bool, "t": seconden sinds start, "n": aantal metingen}
"""

import time

from . import _hulp, _ingenia

NAAM = "drives.encoderlive"
OMSCHRIJVING = "Encoderwaarde live (wijzerplaat)"


def beschikbaar(inst: dict):
    return _ingenia.beschikbaar()


def voer_uit(job, inst: dict, parameters: dict) -> None:
    positie = int(parameters.get("positie") or 1)
    xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")
    seconden = max(5, min(900, int(_ingenia.getal(parameters, "seconden", 120))))
    interval = max(0.05, _ingenia.getal(parameters, "interval", 0.1))

    with _ingenia.verbinding(inst, positie, xdf, job) as mc:
        res = 0
        try:
            sensor = mc.configuration.get_position_feedback()
            res = int(mc.configuration.get_feedback_resolution(sensor))
            job.log(f"Positieterugkoppeling {getattr(sensor, 'name', sensor)}: {res} increments per omwenteling.")
        except Exception as e:  # noqa: BLE001
            job.log(f"  resolutie niet te lezen ({e}); 131072 aangenomen")
        if res <= 0:
            res = 131072
        # Extra registers (MotionLab-termen): BiSS-positie 0x204A, Full frame 0x204B, Position offset 0x2438
        extra = {}
        extra_dtype = {}
        for naam, idx, sub in (("bissPosition", 0x204A, 0), ("fullFrame", 0x204B, 0), ("positionOffset", 0x2438, 0)):
            r = _ingenia.uid_voor(mc, idx, sub)
            if r:
                extra[naam] = r
                dtype = ""
                try:
                    reg = mc._get_drive("default").dictionary.registers(r[1])[r[0]]
                    dtype = str(getattr(getattr(reg, "dtype", None), "name", getattr(reg, "dtype", "")))
                except Exception:  # noqa: BLE001
                    pass
                extra_dtype[naam] = dtype
                job.log(f"  {naam}: 0x{idx:04X}:{sub} = {r[0]} (subnode {r[1]}, type {dtype or '?'})")
            else:
                job.log(f"  {naam}: 0x{idx:04X}:{sub} niet in de dictionary")
        job.log(f"Live meten, {seconden} s (Stop = eerder klaar) …")
        begin = time.time()
        n = 0
        laatste = None
        while time.time() - begin < seconden and not getattr(job, "stop_gevraagd", False):
            try:
                p = int(mc.motion.get_actual_position())
            except Exception as e:  # noqa: BLE001
                job.log(f"  positie lezen mislukt: {e}")
                time.sleep(0.5)
                continue
            try:
                v = float(mc.motion.get_actual_velocity())
            except Exception:  # noqa: BLE001
                v = 0.0
            single = p % res                      # Python: altijd 0..res-1, ook bij negatieve p
            multi = (p - single) // res
            n += 1
            laatste = {"positie": p, "resolutie": res, "singleturn": single, "graden": round(single / res * 360, 2),
                       "multiturn": multi, "snelheid": round(v, 3),
                       "nabijGrens": abs(p) > 2**31 - 2 * res,
                       "t": round(time.time() - begin, 2), "n": n}
            for naam, (uid, sn) in extra.items():
                try:
                    w = mc.communication.get_register(uid, axis=sn)
                    laatste[naam] = int(w) if isinstance(w, (int, float)) else str(w)
                    laatste[naam + "Uid"] = uid
                    laatste[naam + "Dtype"] = extra_dtype.get(naam, "")
                except Exception:  # noqa: BLE001
                    pass
            job.resultaat = laatste
            job.voortgang(int(min(99, (time.time() - begin) / seconden * 100)))
            time.sleep(interval)
    if laatste is None:
        raise RuntimeError("Geen positie gelezen.")
    job.log(f"Klaar: {n} metingen; laatste positie {laatste['positie']} ({laatste['graden']}°, omwenteling {laatste['multiturn']}).")
    job.klaar(laatste)
