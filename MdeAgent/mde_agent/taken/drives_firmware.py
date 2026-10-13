"""
Taak drives.firmware: drive-firmware (.lfu/.sfu) via EtherCAT (FoE) in een
drive laden met ingeniamotion. Duurt enkele minuten; de drive herstart daarna.

Parameters:
    positie      1 = eerste drive op de EtherCAT-bus
    firmware     URL of pad van het firmwarebestand
    versie       ter info
    bootInApp    optioneel; standaard uit de instellingen (drive_boot_in_app).
                 Leeg/None = ingeniamotion bepaalt het zelf uit de bestandsextensie
                 (.lfu/.sfu) — de veiligste keuze. Alleen expliciet zetten als
                 Novanta dat voorschrijft.

Resultaat: {"versie": "...", "positie": n}
"""

import time

from . import _hulp, _ingenia, drives

NAAM = "drives.firmware"
OMSCHRIJVING = "Drive-firmware laden via EtherCAT (ingeniamotion)"


def beschikbaar(inst: dict):
    return _ingenia.beschikbaar()


def voer_uit(job, inst: dict, parameters: dict) -> None:
    from ingeniamotion import MotionController

    positie = int(parameters.get("positie") or 1)
    bestand = _hulp.haal_firmware(parameters.get("firmware") or "", job, "firmware.lfu")
    boot_in_app = parameters.get("bootInApp")
    if boot_in_app is None:
        boot_in_app = inst.get("drive_boot_in_app")          # None = laat ingeniamotion kiezen

    ifname = _ingenia.adapter(inst)
    job.log(f"Firmware {parameters.get('versie', '')} naar drive {positie} via {ifname} "
            f"(boot_in_app={'automatisch' if boot_in_app is None else boot_in_app}) …")
    job.log("Dit duurt enkele minuten; de drive is daarna even weg en komt zelf terug.")
    job.voortgang(5)

    mc = MotionController()
    # ingeniamotion doet zelf verbinden, FoE-overdracht en herstart; geen dictionary nodig.
    # Signatuur (0.10.x): load_firmware_ecat(ifname, fw_file, slave=1, boot_in_app=None, password=None)
    # Na de overdracht herstart de drive; de bibliotheek wacht daar maar kort op en
    # kan dan een time-out geven terwijl de firmware wél goed staat. Daarom: die
    # melding als waarschuwing loggen en zelf controleren of de drive terugkomt.
    laadfout = None
    try:
        gil = _ingenia.gil_config()
        if gil is not None:
            mc.communication.load_firmware_ecat(ifname, bestand, slave=positie, boot_in_app=boot_in_app,
                                                gil_release_config=gil)
        else:
            mc.communication.load_firmware_ecat(ifname, bestand, slave=positie, boot_in_app=boot_in_app)
        job.log("Overdracht klaar; drive herstart.")
    except Exception as e:  # noqa: BLE001
        tekst = str(e)
        if "timeout" in tekst.lower() or "time-out" in tekst.lower() or "timed out" in tekst.lower():
            job.log(f"Bibliotheek meldde een time-out na de overdracht ({tekst}); wachten tot de drive terug is …")
            laadfout = tekst
        else:
            raise
    job.voortgang(70)

    # Wachten tot de drive weer op de bus staat en kijken welke firmware hij nu meldt.
    verwacht = (parameters.get("versie") or "").strip()
    gemeld = ""
    begin = time.time()
    while time.time() - begin < 180:
        time.sleep(10)
        uit = drives.lees(inst, ifname, lambda _t: None)
        lijst = uit.get("drives", [])
        if len(lijst) >= positie:
            gemeld = (lijst[positie - 1].get("software_version") or "").strip()
            job.log(f"Drive {positie} is terug en meldt firmware {gemeld or '?'}.")
            break
        job.log(f"  nog niet terug ({int(time.time() - begin)} s) …")
    else:
        raise RuntimeError("De drive kwam na 3 minuten niet terug op de bus. Controleer voeding en kabel; "
                           "de firmware is mogelijk wel geladen.")

    job.voortgang(95)
    if verwacht and gemeld and not _versie_gelijk(gemeld, verwacht):
        raise RuntimeError(f"Drive meldt firmware {gemeld}, verwacht {verwacht}. Laden lijkt niet gelukt.")
    job.log("Firmware geladen" + (" (ondanks time-out van de bibliotheek)." if laadfout else "."))
    job.klaar({"versie": gemeld or verwacht, "positie": positie, "waarschuwing": laadfout or ""})


def _versie_gelijk(a: str, b: str) -> bool:
    """'2.10.0.000' en '2.10.0' zijn dezelfde versie."""
    def delen(s):
        d = [x for x in s.replace("-", ".").split(".") if x != ""]
        while d and d[-1].strip("0") == "":
            d.pop()
        return [x.lstrip("0") or "0" for x in d]
    return delen(a) == delen(b)
