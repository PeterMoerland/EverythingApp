"""
NIET IN GEBRUIK — bewaard als naslag. Het KingKong/Mosrac USB-kastje gaf op geen enkele poort/baudrate
antwoord op de RS485-commando's uit de datasheet; het spreekt een eigen, ongedocumenteerd protocol.
Niet opgenomen in taken/__init__.py. Verwijder dit bestand gerust.
"""
"""
Taak encoder.usb: een Mosrac S-serie encoder uitlezen (en eventueel op nul zetten) via het
KingKong/Mosrac USB-kastje (USB VID 0x6B6B, meldt zich als 'USB Serial Device').

Het protocol over die COM-poort is niet gedocumenteerd; de aanname is dat het kastje de
RS485-commando's uit de datasheet doorgeeft (hoofdstuk RS485/RS422):

    '1' (0x31)  positie           → 2 bytes multiturn (MSB) + n bytes singleturn (MSB) + CRC-8
    'd' (0x64)  positie + status  → … + 1 byte status + CRC-8
    's' (0x73)  positie + rpm     → … + 2 bytes rpm (×10, signed) + CRC-8
    't' (0x74)  positie + temp    → … + 2 bytes °C×10 (signed) + CRC-8
    '0' (0x30)  nulstellen        → 1 byte telwaarde + CRC-8; tien keer '1','0' afwisselen, bij
                                    telwaarde 10 zet de encoder de huidige positie op 0 (40 ms stil)
    n = 3 bij 17 bits singleturn. CRC-8: x^8+x^7+x^4+x^2+x+1 (0x97). Baudrate A = 115200, B = 2,5 Mbit.

Acties (parameter 'actie'):
    zoek     poorten en baudrates aflopen tot er een geldig antwoord komt; onthouden in agent.json
    lees     één keer lezen met de onthouden (of opgegeven) poort/baud
    ruw      hex uit 'data' sturen en teruggeven wat er binnenkomt (om het protocol te ontdekken)
    nulstel  de nulstel-reeks sturen (alleen als 'bevestigd' = true)

Parameters: actie, poort, baud, data, bevestigd, singleturnBits (17)
"""

import time

NAAM = "encoder.usb"
OMSCHRIJVING = "Mosrac-encoder via USB-kastje lezen / nulstellen"

VID_KINGKONG = 0x6B6B
BAUDS = [2500000, 115200, 1000000, 921600, 460800, 230400, 57600, 9600]


def beschikbaar(inst: dict):
    try:
        import serial  # noqa: F401
        return True, ""
    except Exception as e:  # noqa: BLE001
        return False, f"pyserial ontbreekt: {e}"


def crc8(data: bytes, poly: int = 0x97, init: int = 0x00) -> int:
    crc = init
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = ((crc << 1) ^ poly) & 0xFF if crc & 0x80 else (crc << 1) & 0xFF
    return crc


def _poorten(job) -> list[str]:
    from serial.tools import list_ports
    alle = list(list_ports.comports())
    kk = [p.device for p in alle if (p.vid or 0) == VID_KINGKONG]
    rest = [p.device for p in alle if p.device not in kk]
    for p in alle:
        job.log(f"  poort {p.device}: {p.description} (VID {p.vid:04X} PID {p.pid:04X})" if p.vid else f"  poort {p.device}: {p.description}")
    return kk + rest


def _ontleed(antw: bytes, cmd: bytes, st_bits: int) -> dict | None:
    """Antwoord volgens de datasheet ontleden; None als het niet past of de CRC niet klopt."""
    n_st = 2 if st_bits <= 16 else 3
    extra = {b"1": 0, b"d": 1, b"s": 2, b"t": 2}.get(cmd)
    if extra is None:
        return None
    lengte = 2 + n_st + extra + 1
    if len(antw) < lengte:
        return None
    frame = antw[:lengte]
    crc_ok = crc8(frame[:-1]) == frame[-1]
    crc_ok_ff = crc8(frame[:-1], init=0xFF) == frame[-1]
    if not (crc_ok or crc_ok_ff):
        return None
    mt = int.from_bytes(frame[0:2], "big")
    st = int.from_bytes(frame[2:2 + n_st], "big")
    uit = {"multiturn": mt, "multiturnSigned": mt - 65536 if mt >= 32768 else mt, "singleturn": st,
           "graden": round(st / (1 << st_bits) * 360, 3), "crc": "ok" if crc_ok else "ok (init 0xFF)", "ruw": frame.hex()}
    if cmd == b"d":
        s = frame[2 + n_st]
        uit["status"] = f"0x{s:02X}"
        uit["batterijLeeg"] = bool(s & 0x20)
        uit["batterijLaag"] = bool(s & 0x10)
    elif cmd == b"s":
        uit["rpm"] = int.from_bytes(frame[2 + n_st:4 + n_st], "big", signed=True) / 10
    elif cmd == b"t":
        uit["temperatuur"] = int.from_bytes(frame[2 + n_st:4 + n_st], "big", signed=True) / 10
    return uit


def _vraag(ser, cmd: bytes, wacht: float = 0.05) -> bytes:
    ser.reset_input_buffer()
    ser.write(cmd)
    ser.flush()
    time.sleep(wacht)
    return ser.read(64)


def voer_uit(job, inst: dict, parameters: dict) -> None:
    import serial
    actie = (parameters.get("actie") or "zoek").lower()
    st_bits = int(parameters.get("singleturnBits") or 17)
    poort = parameters.get("poort") or inst.get("encoder_usb_poort") or ""
    baud = int(parameters.get("baud") or inst.get("encoder_usb_baud") or 0)

    if actie == "ruw":
        data = bytes.fromhex(str(parameters.get("data") or "31").replace(" ", ""))
        if not poort or not baud:
            raise RuntimeError("Voor 'ruw' zijn poort en baud nodig (eerst zoeken).")
        with serial.Serial(poort, baud, timeout=0.2) as ser:
            antw = _vraag(ser, data, 0.1)
        job.log(f"{poort} @ {baud}: gestuurd {data.hex()} → ontvangen {antw.hex() or '(niets)'}")
        job.klaar({"gestuurd": data.hex(), "ontvangen": antw.hex(), "poort": poort, "baud": baud})
        return

    if actie == "zoek" or not poort or not baud:
        poorten = [poort] if poort else _poorten(job)
        bauds = [baud] if baud else BAUDS
        if not poorten:
            raise RuntimeError("Geen seriële poorten gevonden. Zit het USB-kastje erin?")
        gevonden = None
        for p in poorten:
            for b in bauds:
                try:
                    with serial.Serial(p, b, timeout=0.2) as ser:
                        antw = _vraag(ser, b"1")
                        uit = _ontleed(antw, b"1", st_bits)
                        job.log(f"  {p} @ {b}: '1' → {antw.hex() or '(niets)'}{'  ✓ geldig' if uit else ''}")
                        if uit:
                            gevonden = (p, b)
                            break
                except Exception as e:  # noqa: BLE001
                    job.log(f"  {p} @ {b}: {e}")
            if gevonden:
                break
        if not gevonden:
            raise RuntimeError("Geen geldig antwoord op '1' op enige poort/baudrate. Het kastje spreekt dan een ander protocol; "
                               "probeer 'ruw' met andere bytes, of vraag Mosrac om het protocol.")
        poort, baud = gevonden
        inst["encoder_usb_poort"], inst["encoder_usb_baud"] = poort, baud
        try:
            from .. import instellingen
            instellingen.bewaar(inst)
            job.log(f"Onthouden: {poort} @ {baud} (agent.json).")
        except Exception:  # noqa: BLE001
            pass

    with serial.Serial(poort, baud, timeout=0.2) as ser:
        if actie == "nulstel":
            if str(parameters.get("bevestigd", "")).lower() not in ("1", "true", "ja"):
                raise RuntimeError("Nulstellen vereist bevestiging.")
            job.log("Nulstel-reeks: tien keer '1' en '0' afwisselend …")
            teller = None
            for i in range(10):
                _vraag(ser, b"1", 0.03)
                antw = _vraag(ser, b"0", 0.03)
                if len(antw) >= 2 and crc8(antw[:1]) == antw[1]:
                    teller = antw[0]
                job.log(f"  ronde {i + 1}: '0' → {antw.hex() or '(niets)'} (telwaarde {teller})")
            time.sleep(0.1)
            if teller != 10:
                raise RuntimeError(f"Nulstellen niet bevestigd door de encoder (laatste telwaarde {teller}).")
            job.log("Encoder heeft de huidige positie op 0 gezet.")

        uit = {"poort": poort, "baud": baud}
        for cmd in (b"d", b"s", b"t"):
            antw = _vraag(ser, cmd)
            o = _ontleed(antw, cmd, st_bits)
            job.log(f"  '{cmd.decode()}' → {antw.hex() or '(niets)'}" + (f"  → {o}" if o else "  (niet te ontleden)"))
            if o:
                uit.update({k: v for k, v in o.items() if k not in uit})
        if "singleturn" not in uit:
            raise RuntimeError("Encoder antwoordt niet (meer) geldig.")
        job.log(f"Positie: omwenteling {uit.get('multiturnSigned')}, singleturn {uit['singleturn']} ({uit['graden']}°)"
                + (f", status {uit['status']}" if "status" in uit else "") + (f", {uit['temperatuur']} °C" if "temperatuur" in uit else ""))
        job.klaar(uit)
