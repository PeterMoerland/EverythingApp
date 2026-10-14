"""
Taak esp32.flashen: een ESP32 (alle varianten: ESP32, S2, S3, C3, C6 …)
programmeren via USB-UART met esptool.

Parameters (JSON in de POST):
    firmware   URL (van de app) of lokaal pad van de .bin of .hex
               (.hex: Intel HEX met de adressen erin, bv. uit `esptool merge-bin
               --format hex`; esptool splitst hem zelf op — offset wordt dan genegeerd)
    offset     adres voor een .bin, standaard "0x0" (een samengevoegd bestand:
               bootloader + partities + app). Voor alleen de app: "0x10000".
    bestanden  óf een lijst [{"firmware": url, "offset": "0x1000"}, ...]
    poort      COM-poort; leeg = instelling esptool_poort of automatisch zoeken
    chip       "auto" (standaard) of bv. "esp32s3"
    wissen     true = eerst hele flash wissen (erase_flash)

Resultaat: {"mac": "aa:bb:cc:dd:ee:ff", "chip": "...", "poort": "COM5"}
De MAC is het unieke nummer van de chip en wordt als serienummer bewaard.
"""

import os
import re

from . import _hulp

NAAM = "esp32.flashen"
OMSCHRIJVING = "ESP32 flashen via USB (esptool)"

USB_UART_KENMERKEN = ("CP210", "CH340", "CH9102", "FT232", "USB Serial", "USB-SERIAL", "USB JTAG", "Silicon Labs")


def beschikbaar(inst: dict):
    try:
        import esptool  # noqa: F401
        import serial  # noqa: F401
    except Exception as e:  # noqa: BLE001
        return False, f"esptool/pyserial ontbreekt: {e}"
    return True, ""


def poorten() -> list[dict]:
    try:
        from serial.tools import list_ports
        return [{"poort": p.device, "omschrijving": p.description, "hwid": p.hwid}
                for p in list_ports.comports()]
    except Exception:  # noqa: BLE001
        return []


def kies_poort(inst: dict, gevraagd: str, job) -> str:
    if gevraagd:
        return gevraagd
    if inst.get("esptool_poort"):
        return inst["esptool_poort"]
    lijst = poorten()
    for p in lijst:
        if any(k.lower() in (p["omschrijving"] + p["hwid"]).lower() for k in USB_UART_KENMERKEN):
            job.log(f"USB-UART gevonden: {p['poort']} ({p['omschrijving']})")
            return p["poort"]
    if len(lijst) == 1:
        return lijst[0]["poort"]
    if not lijst:
        raise RuntimeError("Geen COM-poort gevonden. Zit de USB-kabel erin en is de driver geïnstalleerd?")
    raise RuntimeError("Meerdere COM-poorten, geen herkenbare USB-UART: geef de poort op. "
                       + ", ".join(f"{p['poort']} ({p['omschrijving']})" for p in lijst))


# ---------------------------------------------------------------------------
#  Werker: esptool in-process draaien (apart proces, dus veilig)
# ---------------------------------------------------------------------------

def werker(*argv: str) -> int:
    """`MdeAgent --werker esptool <esptool-argumenten>`; uitvoer gaat naar stdout."""
    import esptool
    try:
        esptool.main(list(argv))
        return 0
    except SystemExit as e:
        return int(e.code or 0)


def _esptool(inst, job, argv: list[str], timeout: int, voortgang_regex: str | None = None) -> int:
    return _hulp.volg_proces(_hulp.werker_commando("esptool", *argv), job, timeout, voortgang_regex)


def _cmd(naam: str) -> str:
    """esptool v5 schrijft commando's met een streepje (read-mac); v4 met een underscore."""
    try:
        import esptool
        hoofd = int(str(getattr(esptool, "__version__", "4")).split(".")[0])
    except Exception:  # noqa: BLE001
        hoofd = 4
    return naam.replace("_", "-") if hoofd >= 5 else naam


def lees_mac(inst, job, poort: str, chip: str) -> tuple[str, str]:
    """MAC en chipnaam ophalen met `esptool read_mac` (via het werker-proces)."""
    class Vanger:
        def __init__(self): self.regels = []
        def log(self, t): self.regels.append(t); job.log(t)
        def voortgang(self, p): pass
    v = Vanger()
    _hulp.volg_proces(_hulp.werker_commando("esptool", "--chip", chip, "--port", poort, _cmd("read_mac")), v, 60)
    tekst = "\n".join(v.regels)
    mac = ""
    m = re.search(r"MAC:\s*([0-9a-fA-F:]{17})", tekst)
    if m:
        mac = m.group(1).lower()
    chipnaam = ""
    m = re.search(r"(?:Chip is|Detecting chip type\.\.\.)\s*([^\n(]+)", tekst)
    if m:
        chipnaam = m.group(1).strip()
    return mac, chipnaam


APP_OFFSET = 0x10000          # standaard partitietabel van ESP-IDF/Arduino: eerste app-partitie (factory/ota_0)


def soort_image(pad: str) -> str:
    """'app' (ESP-IDF app-image: 0xE9 + esp_app_desc_t-magic 0xABCD5432 op 0x20), 'samengevoegd' (bootloader op 0x0
    én partitietabel 0xAA50 op 0x8000, zoals `idf.py merge-bin`), 'bootloader' (0xE9 zonder app-beschrijving) of ''."""
    try:
        with open(pad, "rb") as f:
            kop = f.read(0x30)
            f.seek(0x8000)
            pt = f.read(2)
    except OSError:
        return ""
    if len(kop) < 0x30 or kop[0] != 0xE9:
        return ""
    if pt == b"\xAA\x50":
        return "samengevoegd"
    if int.from_bytes(kop[0x20:0x24], "little") == 0xABCD5432:
        return "app"
    return "bootloader"


def _juiste_offset(pad: str, gevraagd: str, job) -> str:
    """Een los app-image op 0x0 overschrijft de bootloader: de ROM laadt de app dan als bootloader, met
    'SHA-256 comparison failed' en een watchdog-lus (gezien op de Swivel, 14-10-2026). Dan naar 0x10000."""
    soort = soort_image(pad)
    try:
        offset = int(gevraagd, 0)
    except ValueError:
        offset = 0
    naam = os.path.basename(pad)
    if soort == "app" and offset == 0:
        job.log(f"{naam} is een los app-image: niet op 0x0 (daar staat de bootloader) maar op 0x{APP_OFFSET:X}.")
        return f"0x{APP_OFFSET:X}"
    if soort == "samengevoegd" and offset != 0:
        job.log(f"{naam} is een samengevoegd image (bootloader + partitietabel + app): op 0x0 in plaats van {gevraagd}.")
        return "0x0"
    if soort:
        job.log(f"{naam}: {soort}-image op {gevraagd}.")
    return gevraagd


def voer_uit(job, inst: dict, parameters: dict) -> None:
    _hulp.ruim_werkmap_op()
    chip = (parameters.get("chip") or "auto").lower()
    poort = kies_poort(inst, parameters.get("poort") or "", job)
    baud = str(parameters.get("baud") or inst.get("esptool_baud") or 460800)

    # te flashen bestanden verzamelen
    delen = parameters.get("bestanden") or [{"firmware": parameters.get("firmware"),
                                              "offset": parameters.get("offset") or "0x0"}]
    paren: list[str] = []
    for d in delen:
        pad = _hulp.haal_firmware(d.get("firmware") or "", job)
        if pad.lower().endswith(".hex"):
            job.log(f"Intel HEX-bestand: adressen komen uit het bestand ({os.path.basename(pad)}).")
            paren += ["0x0", pad]
        else:
            paren += [_juiste_offset(pad, str(d.get("offset") or "0x0"), job), pad]

    job.voortgang(5)
    mac, chipnaam = lees_mac(inst, job, poort, chip)
    if not mac:
        raise RuntimeError("Geen verbinding met de ESP32 (geen MAC gelezen). "
                           "Staat hij in downloadmodus (BOOT ingedrukt bij reset)?")
    job.log(f"Chip: {chipnaam or chip}, MAC {mac}")
    job.voortgang(10)

    basis = ["--chip", chip, "--port", poort, "--baud", baud, "--before", "default_reset", "--after", "hard_reset"]
    bestanden = [p for i, p in enumerate(paren) if i % 2 == 1]
    if parameters.get("wissen") and bestanden and all(soort_image(p) == "app" for p in bestanden):
        # erase_flash wist ook bootloader en partitietabel; met alleen een app-image start de chip daarna niet meer
        raise RuntimeError("'Eerst flash wissen' met alleen een app-image zou de bootloader en partitietabel weghalen. "
                           "Gebruik een samengevoegd image (idf.py merge-bin) of laat 'wissen' uit.")
    if parameters.get("wissen"):
        job.log("Flash wissen …")
        if _esptool(inst, job, basis + [_cmd("erase_flash")], 120) != 0:
            raise RuntimeError("erase_flash mislukt.")
        job.voortgang(20)

    job.log("Schrijven …")
    code = _esptool(inst, job, basis + [_cmd("write_flash"), "-z", *paren], 300,
                    voortgang_regex=r"(\d+(?:\.\d+)?)\s*%")
    if code != 0:
        raise RuntimeError(f"write_flash mislukt (code {code}).")

    job.klaar({"mac": mac, "chip": chipnaam or chip, "poort": poort,
               "bestanden": [p for i, p in enumerate(paren) if i % 2 == 1]})
