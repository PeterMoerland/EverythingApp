"""
Taken die de agent kan uitvoeren. Elke taak is een module (of object) met:

    NAAM        korte sleutel, bv. "drives.uitlezen"
    OMSCHRIJVING
    def beschikbaar(inst) -> (bool, reden)     kan deze pc de taak doen?
    def voer_uit(job, inst, parameters)        draait in een eigen thread;
                                               job.log(), job.voortgang(), job.klaar(resultaat)

Een nieuwe taak toevoegen = een module hier neerzetten en in ALLE opnemen.
"""

from . import can_sniff, coe, drives, ecat_alias, erob_can, drives_dual, drives_encoderlive, drives_firmware, drives_motion, drives_nulpunt, drives_parameters, drives_phasing, drives_registers, drives_stroomlus, esp32, feetech, mde, serieel, stm32, test, uitgangen

ALLE = {t.NAAM: t for t in (drives, drives_firmware, drives_parameters, drives_stroomlus,
                            drives_motion.Encodertest_, drives_motion.Motiontest_, drives_encoderlive, drives_nulpunt, drives_phasing, drives_registers, drives_dual, esp32, stm32, test, uitgangen,
                            feetech, feetech.Parameters,
                            coe, coe.Hoek, coe.Positie, coe.Vrijgeven, coe.Scan, coe.Sdo,
                            mde, mde.Test, mde.Monitor, mde.Firmware, mde.Serienummer, mde.Eeprom, serieel,
                            can_sniff, erob_can, ecat_alias)}
