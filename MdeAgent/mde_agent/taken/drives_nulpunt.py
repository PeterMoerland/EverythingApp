"""
Taak drives.nulpunt: nulpunt van de absolute encoder instellen en motor-phasing — de procedure
van Ingenia voor de Summit (positie-offset register 0x2438 'Serial 1 Sensor 1 position offset'):

  wat = "offset"   : actuele positie lezen, nieuwe offset = oude offset − actuele positie, schrijven,
                     store. Daarna moet de drive spanning uit/aan (de app meldt dat).
  wat = "phasing"  : MotionLab's motor-phasing (ingeniamotion tests.commutation) draaien — verplicht
                     na het wijzigen van de offset als de encoder ook voor commutatie dient — en store.
                     De as moet vrij kunnen draaien.
  wat = "lees"     : alleen actuele positie, offset en polariteit tonen.

Let op (Ingenia): het positiebereik is 31 bits zonder teken; negatieve posities overleven een
power-cycle niet. Leg het nulpunt dus op het meest negatieve einde van de slag, of laat de PLC
na het opstarten homen.

Parameters: positie, dictionary, wat
"""

from . import _hulp, _ingenia

NAAM = "drives.nulpunt"
OMSCHRIJVING = "Nulpunt (positie-offset) instellen en motor-phasing — Everest/Capitan"

OFFSET_IDX = 0x2438


def beschikbaar(inst: dict):
    return _ingenia.beschikbaar()


def _offset_uid(mc, job) -> str:
    """Registernaam van 0x2438 uit de dictionary (verschilt per firmware); terugval op bekende namen."""
    try:
        drive = mc._get_drive("default")
        for uid, reg in drive.dictionary.registers(1).items():
            if getattr(reg, "idx", None) == OFFSET_IDX:
                return uid
    except Exception as e:  # noqa: BLE001
        job.log(f"  dictionary doorzoeken: {e}")
    for uid in ("FBK_BISS1_POS_OFFSET", "FBK_SSI1_POS_OFFSET", "FBK_SERIAL1_POS_OFFSET", "CL_POS_FBK_OFFSET"):
        try:
            _ingenia.sdo(mc.communication.get_register, uid)
            return uid
        except Exception:  # noqa: BLE001
            continue
    raise RuntimeError("Positie-offsetregister (0x2438) niet in de dictionary gevonden.")


def voer_uit(job, inst: dict, parameters: dict) -> None:
    positie = int(parameters.get("positie") or 1)
    xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")
    wat = (parameters.get("wat") or "lees").lower()

    with _ingenia.verbinding(inst, positie, xdf, job) as mc:
        uid = _offset_uid(mc, job)
        actueel = int(_ingenia.sdo(mc.motion.get_actual_position))
        oud = int(_ingenia.sdo(mc.communication.get_register, uid))
        pol = None
        for puid in ("FBK_BISS1_POS_POLARITY", "FBK_SSI1_POS_POLARITY"):
            try:
                pol = int(_ingenia.sdo(mc.communication.get_register, puid))
                break
            except Exception:  # noqa: BLE001
                continue
        job.log(f"Actuele positie {actueel}; offset ({uid}) = {oud}; polariteit {'omgekeerd' if pol == 1 else 'normaal' if pol == 0 else '?'}.")

        if wat == "lees":
            job.klaar({"positie": actueel, "offset": oud, "offsetRegister": uid, "polariteit": pol})
            return

        if wat == "offset":
            melding = _ingenia.sto_vrij(mc, job)   # niet nodig voor schrijven, wel informatief
            nieuw = ((oud - actueel + 2**31) % 2**32) - 2**31
            job.log(f"Nieuwe offset = {oud} − {actueel} = {nieuw} schrijven …")
            _ingenia.sdo(mc.communication.set_register, uid, nieuw)
            terug = int(_ingenia.sdo(mc.communication.get_register, uid))
            if terug != nieuw:
                raise RuntimeError(f"Offset leest {terug} terug in plaats van {nieuw}.")
            job.log("Opslaan in de drive (store) …")
            mc.configuration.store_configuration()
            na = int(_ingenia.sdo(mc.motion.get_actual_position))
            job.log(f"Positie nu {na} (geldt definitief na spanning uit/aan). Daarna: phasing uitvoeren!")
            job.klaar({"geslaagd": True, "offset": nieuw, "offsetRegister": uid, "positieVoor": actueel, "positieNa": na,
                       "herstartNodig": True, "phasingNodig": True,
                       "samenvatting": f"offset {nieuw} opgeslagen (positie was {actueel}); spanning uit/aan en daarna phasing"})
            return

        if wat == "phasing":
            melding = _ingenia.sto_vrij(mc, job)
            if melding:
                job.klaar({"geslaagd": False, "sto_ontbreekt": True, "samenvatting": melding})
                return
            job.log("Motor-phasing (commutatie-uitlijning) starten — de as draait een stukje …")
            job.voortgang(20)
            uit = mc.tests.commutation(apply_changes=True)
            ernst = uit.get("result_severity") if isinstance(uit, dict) else None
            tekst = uit.get("result_message", "") if isinstance(uit, dict) else str(uit)
            voorgesteld = uit.get("suggested_registers", {}) if isinstance(uit, dict) else {}
            job.log(f"Phasing: {tekst} (severity {ernst}); registers: {voorgesteld}")
            ok = ernst in (0, "0", None) or str(ernst).upper().endswith("SUCCESS")
            if ok:
                job.log("Opslaan in de drive (store) …")
                mc.configuration.store_configuration()
            job.klaar({"geslaagd": ok, "samenvatting": tekst + (" — opgeslagen; spanning uit/aan" if ok else ""),
                       "registers": {k: str(v) for k, v in voorgesteld.items()}, "herstartNodig": ok})
            return

    raise RuntimeError(f"Onbekende opdracht '{wat}' (lees | offset | phasing).")
