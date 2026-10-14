"""
Taak serieel.monitor: de seriële uitvoer van een ESP32 (of een andere print aan de USB-UART) live
meelezen, zoals de seriële monitor van de Arduino-IDE / idf.py monitor. Elke ontvangen regel komt als
logregel in de job; de app toont ze in het taakvenster. Tekst terugsturen kan met POST /jobs/<id>/invoer
({"tekst": "..."}); de taak stuurt dat met een regeleinde naar de poort.

Parameters:
    poort       COM-poort; leeg = instelling esptool_poort of automatisch zoeken (zoals esp32.flashen)
    baud        standaard 115200
    seconden    hoe lang (standaard 600, max 3600); Stop = eerder klaar
    reset       true = de ESP32 eerst resetten via DTR/RTS (zoals esptool), zodat je de opstartregels ziet
    regeleinde  wat er achter verstuurde tekst komt: "\\n" (standaard), "\\r\\n" of ""

Resultaat: {"poort": "COM5", "regels": n, "bytes": n}
"""

import time

from . import esp32

NAAM = "serieel.monitor"
OMSCHRIJVING = "Seriële monitor (ESP32 via USB-UART): uitvoer live meelezen, tekst sturen"


def beschikbaar(inst: dict):
    try:
        import serial  # noqa: F401
    except Exception as e:  # noqa: BLE001
        return False, f"pyserial ontbreekt: {e}"
    return True, ""


def _reset_esp32(ser) -> None:
    """Zelfde truc als esptool 'default_reset': EN laag via RTS met IO0 hoog (DTR), dan loslaten → gewone herstart."""
    ser.dtr = False
    ser.rts = True
    time.sleep(0.1)
    ser.rts = False
    time.sleep(0.05)


def voer_uit(job, inst: dict, parameters: dict) -> None:
    import serial

    poort = esp32.kies_poort(inst, (parameters.get("poort") or "").strip(), job)
    baud = int(parameters.get("baud") or 115200)
    seconden = max(5, min(3600, int(float(parameters.get("seconden") or 600))))
    regeleinde = parameters.get("regeleinde")
    if regeleinde is None:
        regeleinde = "\n"
    reset = str(parameters.get("reset", "0")).lower() in ("1", "true", "ja", "yes")

    job.log(f"Seriële monitor op {poort}, {baud} baud, {seconden} s (Stop = eerder klaar).")
    try:
        ser = serial.Serial(poort, baud, timeout=0.1)
    except Exception as e:  # noqa: BLE001
        raise RuntimeError(f"Poort {poort} openen mislukt: {e}. Staat er nog een andere monitor of esptool op de poort?") from e

    n_regels = 0
    n_bytes = 0
    begin = time.time()
    rest = b""
    try:
        # Zonder reset laten we DTR/RTS met rust: anders houdt de USB-UART de ESP32 in reset of in de bootloader.
        if reset:
            job.log("ESP32 resetten …")
            _reset_esp32(ser)
        else:
            ser.dtr = False
            ser.rts = False
        laatste_voortgang = 0.0
        while time.time() - begin < seconden and not getattr(job, "stop_gevraagd", False):
            # eerst wat de gebruiker wil sturen
            for tekst in job.lees_invoer():
                data = (tekst + regeleinde).encode("utf-8", "replace")
                ser.write(data)
                job.log("» " + tekst)
            # dan wat er binnenkomt
            data = ser.read(4096)
            if data:
                n_bytes += len(data)
                rest += data
                while b"\n" in rest:
                    regel, rest = rest.split(b"\n", 1)
                    job.log(regel.decode("utf-8", "replace").rstrip("\r"))
                    n_regels += 1
                if len(rest) > 2000:            # geen regeleinde (binaire rommel of verkeerde baud): toch tonen
                    job.log(rest.decode("utf-8", "replace"))
                    rest = b""
            if time.time() - laatste_voortgang > 2:
                job.voortgang(int(min(99, (time.time() - begin) / seconden * 100)))
                laatste_voortgang = time.time()
    finally:
        try:
            ser.close()
        except Exception:  # noqa: BLE001
            pass
    if rest:
        job.log(rest.decode("utf-8", "replace"))
        n_regels += 1
    job.log(f"Klaar: {n_regels} regels, {n_bytes} bytes in {time.time() - begin:.0f} s.")
    job.klaar({"poort": poort, "regels": n_regels, "bytes": n_bytes, "gestopt": bool(getattr(job, "stop_gevraagd", False))})
