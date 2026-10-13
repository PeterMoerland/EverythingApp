"""
Feetech STS/SMS-servo's over de half-duplex TTL-bus (USB-adapter FE-URT-1 of
vergelijkbaar). Eigen, kleine implementatie van het protocol met pyserial:

    FF FF | ID | LEN | INSTR | params… | CHK      CHK = ~(ID+LEN+INSTR+Σparams) & 0xFF
    INSTR: 0x01 PING, 0x02 READ (adres, aantal), 0x03 WRITE (adres, bytes…)
    Antwoord: FF FF | ID | LEN | ERROR | params… | CHK

STS-serie (STS3215): 2-bytewaarden little-endian. SMS-serie is big-endian; dat
staat in de registertabel als 'endian'.

Registertabel voor de STS3215 (adres, naam, bytes, eeprom/sram, rw). Alleen
rw-registers worden geprogrammeerd; 'r' wordt teruggelezen ter info.
"""

import json
import time

import serial

PING, READ, WRITE = 0x01, 0x02, 0x03

# adres: (naam, bytes, geheugen, rw, signed)
STS3215 = {
    0:  ("Firmware main version", 1, "eeprom", "r", False),
    1:  ("Firmware sub version", 1, "eeprom", "r", False),
    3:  ("Servo main version", 1, "eeprom", "r", False),
    4:  ("Servo sub version", 1, "eeprom", "r", False),
    5:  ("ID", 1, "eeprom", "rw", False),
    6:  ("Baud rate", 1, "eeprom", "rw", False),
    7:  ("Return delay", 1, "eeprom", "rw", False),
    8:  ("Ack level", 1, "eeprom", "rw", False),
    9:  ("Minimum angle", 2, "eeprom", "rw", True),
    11: ("Maximum angle", 2, "eeprom", "rw", True),
    13: ("Maximum temperature", 1, "eeprom", "rw", False),
    14: ("Maximum voltage", 1, "eeprom", "rw", False),
    15: ("Minimum voltage", 1, "eeprom", "rw", False),
    16: ("Maximum torque", 2, "eeprom", "rw", False),
    19: ("Unloading condition", 1, "eeprom", "rw", False),
    20: ("LED alarm condition", 1, "eeprom", "rw", False),
    21: ("P", 1, "eeprom", "rw", False),
    22: ("D", 1, "eeprom", "rw", False),
    23: ("I", 1, "eeprom", "rw", False),
    24: ("Punch", 2, "eeprom", "rw", False),
    26: ("CW dead band", 1, "eeprom", "rw", False),
    27: ("CCW dead band", 1, "eeprom", "rw", False),
    28: ("Maximum current", 2, "eeprom", "rw", False),
    30: ("Resolution", 1, "eeprom", "rw", False),
    31: ("Position offset", 2, "eeprom", "rw", True),
    33: ("Run mode", 1, "eeprom", "rw", False),
    34: ("Protective torque", 1, "eeprom", "rw", False),
    35: ("Protection time", 1, "eeprom", "rw", False),
    36: ("Overload torque", 1, "eeprom", "rw", False),
    38: ("Current protection time", 1, "eeprom", "rw", False),
    40: ("Torque enable", 1, "sram", "rw", False),
    41: ("Acceleration", 1, "sram", "rw", False),
    42: ("Target position", 2, "sram", "rw", True),
    46: ("Running speed", 2, "sram", "rw", False),
    48: ("Torque limit", 2, "sram", "rw", False),
    55: ("Lock", 1, "sram", "rw", False),
    56: ("Current position", 2, "sram", "r", True),
    58: ("Current speed", 2, "sram", "r", True),
    60: ("Current load", 2, "sram", "r", True),
    62: ("Current voltage", 1, "sram", "r", False),
    63: ("Current temperature", 1, "sram", "r", False),
    66: ("Moving", 1, "sram", "r", False),
    69: ("Current current", 2, "sram", "r", False),
}

BAUDS = {0: 1_000_000, 1: 500_000, 2: 250_000, 3: 128_000, 4: 115_200, 5: 76_800, 6: 57_600, 7: 38_400}

ADRES_ID, ADRES_BAUD, ADRES_LOCK = 5, 6, 55


FOUTBITS = {0x01: "spanning", 0x02: "hoeksensor", 0x04: "temperatuur", 0x08: "stroom", 0x20: "overbelasting"}


def foutbits_tekst(fout: int) -> str:
    return ", ".join(naam for bit, naam in FOUTBITS.items() if fout & bit) or f"0x{fout:02X}"


class FeetechBus:
    def __init__(self, poort: str, baud: int = 1_000_000, timeout: float = 0.03, endian: str = "little"):
        self.ser = serial.Serial(poort, baud, timeout=timeout, write_timeout=0.2)
        self.endian = endian
        # Statusbyte van het laatste antwoord: de servo meldt daarin zijn foutvlaggen
        # (spanning, temperatuur …). Dat is informatie, geen reden om niet te praten.
        self.laatste_fout = 0
        self._laatst_gezonden = b""

    def close(self):
        try:
            self.ser.close()
        except Exception:  # noqa: BLE001
            pass

    # -- laag niveau ---------------------------------------------------------
    def _zend(self, sid: int, instr: int, params: bytes = b"") -> None:
        lengte = len(params) + 2
        chk = (~(sid + lengte + instr + sum(params))) & 0xFF
        pakket = bytes([0xFF, 0xFF, sid, lengte, instr]) + params + bytes([chk])
        self.ser.reset_input_buffer()
        self.ser.write(pakket)
        self.ser.flush()
        self._laatst_gezonden = pakket

    def _ontvang(self, sid: int) -> bytes:
        """
        Antwoord lezen; geeft de parameters terug. Half-duplex adapters (CH340/CH343
        op de FE-URT-1) geven onze eigen zendbytes ook terug; die echo herkennen we
        aan de inhoud en slaan we over. Daarna zoeken we FF FF <id> <len> en
        controleren de checksum.
        """
        # USB-serieel levert data in blokjes van ~16 ms (CH340/CH343); ruim wachten dus.
        einde = time.time() + 0.15
        buf = b""
        while time.time() < einde:
            buf += self.ser.read(64)
            # Eigen echo vooraan (half-duplex adapter)? Alleen weggooien als er ná de
            # echo nog een compleet pakket staat — bij een PING is het antwoord van
            # een servo met foutvlag namelijk byte-voor-byte gelijk aan wat we zonden.
            echo = self._laatst_gezonden
            if echo and buf.startswith(echo) and len(buf) >= len(echo) + 6:
                buf = buf[len(echo):]
            i = buf.find(bytes([0xFF, 0xFF, sid]))
            if i < 0 or len(buf) < i + 4:
                continue
            lengte = buf[i + 3]
            if len(buf) < i + 4 + lengte:
                continue
            rest = buf[i + 4:i + 4 + lengte]
            fout, params, chk = rest[0], rest[1:-1], rest[-1]
            if ((~(sid + lengte + fout + sum(params))) & 0xFF) != chk:
                # geen geldig pakket op deze plek (rest van een echo?): verder zoeken
                buf = buf[i + 2:]
                continue
            self.laatste_fout = fout
            return params
        raise TimeoutError(f"geen (geldig) antwoord van servo {sid}" + (f"; ontvangen: {buf.hex(' ')}" if buf else ""))

    # -- hoog niveau ---------------------------------------------------------
    def ping(self, sid: int) -> bool:
        try:
            self._zend(sid, PING)
            self._ontvang(sid)
            return True
        except Exception:  # noqa: BLE001
            return False

    def lees(self, sid: int, adres: int, aantal: int) -> bytes:
        self._zend(sid, READ, bytes([adres, aantal]))
        return self._ontvang(sid)

    def schrijf(self, sid: int, adres: int, data: bytes) -> None:
        self._zend(sid, WRITE, bytes([adres]) + data)
        if sid != 0xFE:                      # broadcast krijgt geen antwoord
            self._ontvang(sid)

    def lees_waarde(self, sid: int, adres: int, nbytes: int, signed: bool = False) -> int:
        ruw = self.lees(sid, adres, nbytes)
        w = int.from_bytes(ruw, self.endian)
        if signed and nbytes == 2 and w & 0x8000:      # Feetech: bit 15 = teken (geen two's complement)
            w = -(w & 0x7FFF)
        return w

    def schrijf_waarde(self, sid: int, adres: int, nbytes: int, waarde: int, signed: bool = False) -> None:
        if signed and waarde < 0:
            waarde = (-waarde) | 0x8000
        self.schrijf(sid, adres, int(waarde).to_bytes(nbytes, self.endian))

    def scan(self, ids=range(1, 254)) -> list[int]:
        return [i for i in ids if self.ping(i)]

    def ruw_antwoord(self, sid: int = 0xFE, wacht: float = 0.15) -> bytes:
        """Alles wat er binnen 'wacht' seconden terugkomt op een PING — voor diagnose."""
        self._zend(sid, PING)
        einde = time.time() + wacht
        buf = b""
        while time.time() < einde:
            buf += self.ser.read(32)
        return buf

    def broadcast_ping(self) -> int | None:
        """
        PING naar 0xFE: elke servo antwoordt met zijn eigen ID in de kop. Met één
        servo op de bus (de programmeersituatie) is dat meteen het antwoord; met
        meerdere botsen de antwoorden en valt de aanroeper terug op een scan.
        """
        self._zend(0xFE, PING)
        einde = time.time() + 0.15
        buf = b""
        while time.time() < einde:
            buf += self.ser.read(6)
            i = buf.find(b"\xff\xff")
            if i >= 0 and len(buf) >= i + 4:
                sid = buf[i + 2]
                if 0 < sid < 0xFE:
                    return sid
                buf = buf[i + 2:]
        return None


def registertabel(model: str) -> dict:
    """Voorlopig één tabel; andere modellen kunnen hier bij."""
    return STS3215


def lees_alles(bus: FeetechBus, sid: int, tabel: dict) -> list[dict]:
    uit = []
    for adres, (naam, nbytes, geheugen, rw, signed) in sorted(tabel.items()):
        try:
            w = bus.lees_waarde(sid, adres, nbytes, signed)
        except Exception as e:  # noqa: BLE001
            w = None
            fout = str(e)
        else:
            fout = ""
        uit.append({"adres": adres, "naam": naam, "bytes": nbytes, "geheugen": geheugen, "rw": rw,
                    "waarde": w, **({"fout": fout} if fout else {})})
    return uit


def lees_parameterset(pad: str) -> dict:
    """
    Ons JSON-formaat:
    {
      "protocol": "feetech", "model": "STS3215", "endian": "little",
      "omschrijving": "R-as motor v1",
      "registers": [ {"adres": 6, "naam": "Baud rate", "waarde": 4}, … ]
    }
    Alleen rw-registers worden geschreven; 'naam' is ter documentatie.
    """
    with open(pad, encoding="utf-8-sig") as f:
        d = json.load(f)
    if d.get("protocol", "feetech") != "feetech":
        raise ValueError(f"Dit is geen Feetech-parameterset (protocol={d.get('protocol')}).")
    if not isinstance(d.get("registers"), list):
        raise ValueError("Parameterset zonder 'registers'-lijst.")
    return d


def voorbeeld_parameterset() -> dict:
    """Sjabloon met de rw-EEPROM-registers van de STS3215 en gangbare standaardwaarden."""
    standaard = {5: 1, 6: 4, 7: 250, 8: 1, 9: 0, 11: 4095, 13: 80, 14: 254, 15: 90, 16: 1000,
                 19: 44, 20: 44, 21: 32, 22: 32, 23: 0, 24: 0, 26: 0, 27: 0, 28: 500, 30: 1, 31: 0,
                 33: 0, 34: 20, 35: 200, 36: 80, 38: 250}
    return {
        "protocol": "feetech", "model": "STS3215", "endian": "little",
        "omschrijving": "Sjabloon STS3215 — pas de waarden aan en bewaar als parameterset bij het motorproduct",
        "registers": [{"adres": a, "naam": STS3215[a][0], "waarde": v} for a, v in standaard.items()],
    }
