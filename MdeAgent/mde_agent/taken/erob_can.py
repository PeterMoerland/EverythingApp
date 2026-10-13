"""
Taak can.erob_info: de identificatie van een ZeroErr eRob uitlezen over CAN met het eigen
ZeroErr 'CAN Custom Protocol' (handleiding 'eRob CAN Custom Protocol Manual V1.0'):

    aanvraag  0x640 + ID   lezen: [idx_hi, idx_lo] of [idx_hi, idx_lo, sub_hi, sub_lo]
    antwoord  0x5C0 + ID   [w3, w2, w1, w0, 0x3E]  (32-bit big-endian) · [0x3E] ok · [0x80] fout

Wat we lezen (index 0x232 vraagt firmware X5061715X of nieuwer):
    0x001:3  hardwareversie          0x001:4  firmwareversie      0x043  CAN-adres
    0x232:1  volgnummer (8 cijfers)  0x232:2  model-info A        0x232:5  model-info B
    0x232:6  hardwarewijziging       0x232:7  IO-functionaliteit  0x232:8  partnummer-info
en daaruit volgens hoofdstuk 5.3: serienummer (OD-code + volgnummer), modelcode
(eRob110F100I-BM-18EN), hardware-informatie (1005_1_1) en partnummer (MC2A.BC.DE).

Parameters:
    id        drive-ID (1..127). Leeg = eerst 2 s luisteren naar wie er praat (0x5C0+ID),
              anders ID 1..8 afvragen.
    baud / kanaal / apparaattype / index   zoals bij can.meeluisteren

De adapter moet kunnen zenden: sluit eRob-Studio/eTuner het liefst even, anders praten twee
masters door elkaar (de antwoorden worden dan op volgorde gekoppeld, maar zeker is het niet).
"""

import time

from . import can_bus

NAAM = "can.erob_info"
OMSCHRIJVING = "eRob identificatie (serienummer, model, hardware, partnummer) uitlezen over CAN"

# Objecten uit de handleiding, 'index:sub' (hex, zonder 0x, sub 0 = geen sub-index) → naam.
EROB_OBJECTEN: dict[str, str] = {
    "0x001:3": "Hardware Version", "0x001:4": "Firmware Version",
    "0x002:0": "Actual Position", "0x005:1": "Actual Speed", "0x008:0": "Actual Motor Current",
    "0x009:0": "U-Phase Current", "0x00A:0": "V-Phase Current", "0x00D:0": "U-Phase Voltage",
    "0x00E:0": "V-Phase Voltage", "0x00F:0": "W-Phase Voltage", "0x01F:0": "Error Codes",
    "0x020:0": "Operation State", "0x022:0": "Digital Input", "0x023:0": "Analog Input",
    "0x024:0": "DC Bus Voltage", "0x026:0": "Power Temperature", "0x033:0": "Continuous Current",
    "0x034:0": "Peak Current", "0x035:0": "Peak Current Duration", "0x043:0": "CAN Address",
    "0x044:0": "CAN Baud Rate", "0x04E:0": "Operation Mode", "0x050:0": "MAX Speed",
    "0x052:0": "Position Limit MIN", "0x053:0": "Position Limit MAX", "0x054:0": "Max Position Error",
    "0x055:0": "Max Speed Error", "0x056:0": "Struck Current", "0x057:0": "Struck Speed",
    "0x058:0": "Struck Time", "0x062:0": "MAX Phase Current", "0x063:0": "MAX Motor Current",
    "0x064:0": "Position Loop Gain", "0x066:0": "Speed Loop Gain", "0x067:0": "Speed Loop Integral",
    "0x083:0": "Start Motion (cmd)", "0x084:0": "Stop Motion (cmd)", "0x086:0": "Target Absolute Position",
    "0x087:0": "Relative Position", "0x088:0": "Profile Acceleration", "0x089:0": "Profile Deceleration",
    "0x08A:0": "Profile Speed", "0x08B:0": "Jerk Setting", "0x08C:0": "Emergency Stop Deceleration",
    "0x08D:0": "Motion Mode", "0x093:0": "Stop Time", "0x09C:0": "Motor Angle", "0x09E:0": "Limit Move",
    "0x09F:0": "Load Angle", "0x0E8:0": "Save (cmd)", "0x0FD:0": "Phase Current Sum Error",
    "0x0FE:0": "W-Phase Current", "0x0FF:0": "COM No.", "0x100:0": "Enable Motor", "0x109:0": "Positioning Threshold",
    "0x10A:0": "Positioning Time", "0x10C:0": "Target Reached State", "0x112:0": "Control Source",
    "0x114:0": "Reset Load Encoder (cmd)", "0x124:0": "Use bus to adjust PID", "0x129:0": "MAX Torque Output",
    "0x14F:0": "Disengage Brake (cmd)", "0x1FD:0": "Analog Source", "0x1FE:0": "Analog Value",
    "0x204:0": "MAX Speed Limit", "0x20D:0": "Actual Torque",
    "0x232:1": "eRob Sequence Number", "0x232:2": "eRob Model Info A", "0x232:5": "eRob Model Info B",
    "0x232:6": "eRob Hardware Change", "0x232:7": "eRob IO Functionality", "0x232:8": "eRob Part Number Info",
}


def beschikbaar(inst: dict):
    return can_bus.beschikbaar(inst)


def _naam(index: int, sub: int) -> str:
    return EROB_OBJECTEN.get(f"0x{index:03X}:{sub}", "")


def ontleed_aanvraag(data: bytes) -> dict:
    """Een 0x640+ID-frame: lezen (2/4 bytes), schrijven (6/8 bytes) of commando."""
    uit: dict = {"protocol": "erob", "hex": data.hex()}
    if len(data) < 2:
        uit.update(richting="onbekend", index="", sub="")
        return uit
    index = (data[0] << 8) | data[1]
    sub = 0
    if len(data) == 2:
        uit.update(richting="lees", index=f"0x{index:03X}", sub=0)
    elif len(data) == 4:
        sub = (data[2] << 8) | data[3]
        uit.update(richting="lees", index=f"0x{index:03X}", sub=sub)
    elif len(data) == 6:
        w = int.from_bytes(data[2:6], "big")
        uit.update(richting="schrijf", index=f"0x{index:03X}", sub=0, waarde=w,
                   signed=int.from_bytes(data[2:6], "big", signed=True))
    elif len(data) == 8:
        sub = (data[2] << 8) | data[3]
        w = int.from_bytes(data[4:8], "big")
        uit.update(richting="schrijf", index=f"0x{index:03X}", sub=sub, waarde=w,
                   signed=int.from_bytes(data[4:8], "big", signed=True))
    else:
        uit.update(richting="onbekend", index=f"0x{index:03X}", sub=0)
    naam = _naam(index, sub)
    if naam.endswith("(cmd)") and uit["richting"] == "lees":
        uit["richting"] = "commando"
    uit["naam"] = naam
    return uit


def ontleed_antwoord(data: bytes, aanvraag: dict | None) -> dict:
    """Een 0x5C0+ID-frame; 'aanvraag' is de gekoppelde vraag (voor index/sub/naam) of None."""
    uit: dict = {"protocol": "erob", "hex": data.hex(),
                 "index": (aanvraag or {}).get("index", ""), "sub": (aanvraag or {}).get("sub", ""),
                 "naam": (aanvraag or {}).get("naam", "")}
    if len(data) == 1 and data[0] == 0x3E:
        uit.update(richting="ok")
    elif len(data) == 1 and data[0] == 0x80:
        uit.update(richting="fout")
    elif len(data) == 5 and data[4] == 0x3E:
        w = int.from_bytes(data[0:4], "big")
        richting = "antwoord"
        if aanvraag is not None and aanvraag.get("richting") in ("schrijf", "commando"):
            richting = "foutcode"      # tabel 2-4 / 3-4: waarde + 3E na een schrijfopdracht = foutcode
        uit.update(richting=richting, waarde=w, signed=int.from_bytes(data[0:4], "big", signed=True))
    elif len(data) == 4:
        # sommige firmware laat de 3E weg (voorbeeld stap 11 in de handleiding)
        uit.update(richting="antwoord", waarde=int.from_bytes(data, "big"),
                   signed=int.from_bytes(data, "big", signed=True))
    else:
        uit.update(richting="antwoord?")
    return uit


# ---------------------------------------------------------------- decoderen hoofdstuk 5.3
_OD = {1: "70", 7: "70", 8: "70", 2: "80", 3: "90", 4: "110", 5: "142", 6: "170"}
_SWG = {1: "F", 2: "H"}
_GR = {1: "50", 2: "80", 3: "100", 4: "120", 5: "160"}
_FF = {1: "I", 2: "T", 3: "BI", 4: "BT"}
_BK = {1: "F", 2: "B"}
_ENC = {1: "S", 2: "M", 3: "HS", 4: "HM"}
_COM = {1: "E", 2: "C"}
_VTS = {1: "N", 2: "T"}
_SWG_VER = {1: "A", 2: "B", 3: "C", 4: "c", 5: "D", 6: "d", 7: "E", 8: "e"}
_NVMV = {1: "A", 2: "B", 3: "C", 4: "D", 5: "E"}


def _nib(w: int, bit: int) -> int:
    return (w >> bit) & 0xF


def decodeer(w: dict) -> dict:
    """w: {'0x232:1': int, ...} → serienummer, model, hardware-info, partnummer (wat er te maken valt)."""
    uit: dict = {}
    a = w.get("0x232:2")
    b = w.get("0x232:5")
    seq = w.get("0x232:1")
    if a is not None and seq is not None:
        od = _OD.get(_nib(a, 16))
        uit["od_code"] = od
        if od:
            uit["serienummer"] = f"{int(od):03d}{seq:08d}"
    if a is not None and b is not None:
        delen = [_OD.get(_nib(a, 16)), _SWG.get(_nib(b, 20)), _GR.get(_nib(a, 12)), _FF.get(_nib(b, 24)),
                 _BK.get(_nib(b, 16)), _ENC.get(_nib(b, 0)), _COM.get(_nib(b, 8)), _VTS.get(_nib(b, 4))]
        if all(delen):
            od, swg, gr, ff, bk, enc, com, vts = delen
            uit["model"] = f"eRob{od}{swg}{gr}{ff}-{bk}{enc}-18{com}{vts}"
        uit["model_delen"] = {"OD": delen[0], "SWG": delen[1], "GR": delen[2], "FF": delen[3],
                              "BK": delen[4], "ENC": delen[5], "COM": delen[6], "VTS": delen[7]}
    hv = w.get("0x001:3")
    if hv is not None:
        hci = w.get("0x232:6")
        csi = w.get("0x232:7")
        uit["hardware_versie"] = hv
        if hci is not None and csi is not None:
            uit["hardware_info"] = f"{hv:04d}_{hci}_{csi}"
    fw = w.get("0x001:4")
    if fw is not None:
        uit["firmware_versie"] = fw
    p = w.get("0x232:8")
    if p is not None:
        mv = _nib(p, 0)
        erobv = f"{_nib(p, 4):X}"
        ffv = f"{_nib(p, 8):X}"
        swg = _SWG_VER.get(_nib(p, 12), f"{_nib(p, 12):X}")
        dv = f"{_nib(p, 16):X}"
        nvmv = _NVMV.get(_nib(p, 20), f"{_nib(p, 20):X}")
        uit["partnummer"] = f"MC{mv}{erobv}.{ffv}{swg}.{dv}{nvmv}"
    if w.get("0x043:0") is not None:
        uit["can_adres"] = w["0x043:0"]
    return uit


# ---------------------------------------------------------------- lezen over de bus
_LEZEN = [("0x001:3", 0x001, 3), ("0x001:4", 0x001, 4), ("0x043:0", 0x043, 0),
          ("0x232:1", 0x232, 1), ("0x232:2", 0x232, 2), ("0x232:5", 0x232, 5),
          ("0x232:6", 0x232, 6), ("0x232:7", 0x232, 7), ("0x232:8", 0x232, 8)]


def _vraag(bus: can_bus.CanBus, node: int, index: int, sub: int) -> None:
    data = bytes([(index >> 8) & 0xFF, index & 0xFF])
    if sub:
        data += bytes([(sub >> 8) & 0xFF, sub & 0xFF])
    bus.zend(0x640 + node, data)


def _wacht_antwoord(bus: can_bus.CanBus, node: int, timeout: float, job, vreemde: list[int]) -> dict | None:
    """Eerste 0x5C0+node-frame binnen 'timeout' s. Andermans aanvragen (0x640+node) tellen we mee."""
    einde = time.time() + timeout
    for frame in bus.frames():
        if time.time() > einde:
            return None
        if frame is None:
            continue
        t, can_id, ext, data = frame
        cobid = can_id & 0x7FF
        if cobid == 0x640 + node:
            vreemde.append(cobid)            # een andere master praat mee
            continue
        if cobid == 0x5C0 + node:
            return ontleed_antwoord(data, None)
    return None


def lees_object(bus: can_bus.CanBus, node: int, index: int, sub: int, job, vreemde: list[int], pogingen: int = 3):
    """Leest een object en eist twee gelijke antwoorden (tegen door-elkaar-praten met eRob-Studio)."""
    waarden: list[int] = []
    for _ in range(pogingen + 1):
        _vraag(bus, node, index, sub)
        a = _wacht_antwoord(bus, node, 0.4, job, vreemde)
        if a is None:
            continue
        if a.get("richting") == "fout":
            return None, "fout (0x80) — object onbekend of firmware te oud"
        if "waarde" in a:
            waarden.append(a["waarde"])
            if len(waarden) >= 2 and waarden[-1] == waarden[-2]:
                return waarden[-1], ""
    if waarden:
        return waarden[-1], "onzeker (antwoorden verschilden)"
    return None, "geen antwoord"


def voer_uit(job, inst: dict, parameters: dict) -> None:
    baud = int(parameters.get("baud") or 1000)
    kanaal = int(parameters.get("kanaal") or 0)
    gevraagd_id = parameters.get("id")
    node = int(gevraagd_id) if gevraagd_id not in (None, "") else None

    bus = can_bus.CanBus.open(inst, job, baud=baud, kanaal=kanaal, luister_alleen=False, seconden=120,
                              apparaattype=parameters.get("apparaattype"), index=parameters.get("index"))
    vreemde: list[int] = []
    with bus:
        if node is None:
            # wie praat er? 2 s luisteren naar 0x5C0+ID / 0x640+ID
            job.log("Drive-ID zoeken: 2 s luisteren naar eRob-verkeer …")
            gezien: dict[int, int] = {}
            einde = time.time() + 2.0
            for frame in bus.frames():
                if time.time() > einde:
                    break
                if frame is None:
                    continue
                cobid = frame[1] & 0x7FF
                if 0x5C1 <= cobid <= 0x63F:
                    gezien[cobid - 0x5C0] = gezien.get(cobid - 0x5C0, 0) + 1
                elif 0x641 <= cobid <= 0x6BF:
                    gezien[cobid - 0x640] = gezien.get(cobid - 0x640, 0) + 1
            if gezien:
                node = max(gezien, key=gezien.get)
                job.log(f"  gezien: " + ", ".join(f"ID {k} ({v} frames)" for k, v in sorted(gezien.items())) + f" → ID {node}")
            else:
                job.log("  niets gehoord; ID 1..8 afvragen …")
                for kandidaat in range(1, 9):
                    _vraag(bus, kandidaat, 0x001, 3)
                    a = _wacht_antwoord(bus, kandidaat, 0.25, job, vreemde)
                    if a is not None:
                        node = kandidaat
                        job.log(f"  ID {kandidaat} antwoordt.")
                        break
                if node is None:
                    raise RuntimeError("Geen eRob gevonden op ID 1..8. Geef het drive-ID op, of controleer bedrading/bitrate (1 Mbit).")

        job.log(f"eRob ID {node}: identificatie lezen …")
        waarden: dict[str, int] = {}
        meldingen: dict[str, str] = {}
        for i, (sleutel, index, sub) in enumerate(_LEZEN):
            w, melding = lees_object(bus, node, index, sub, job, vreemde)
            job.voortgang(int((i + 1) / len(_LEZEN) * 95))
            naam = EROB_OBJECTEN.get(sleutel, sleutel)
            if w is None:
                meldingen[sleutel] = melding
                job.log(f"  {sleutel:<9} {naam:<24} — {melding}")
                continue
            waarden[sleutel] = w
            if melding:
                meldingen[sleutel] = melding
            job.log(f"  {sleutel:<9} {naam:<24} = {w} (0x{w:08X}){'  ' + melding if melding else ''}")

    info = decodeer(waarden)
    if vreemde:
        job.log(f"Let op: {len(vreemde)} aanvragen van een andere master gezien (eRob-Studio open?). "
                "Waarden zijn dubbel gelezen; bij twijfel Studio sluiten en opnieuw lezen.")
    for k, label in (("serienummer", "Serienummer"), ("model", "Model"), ("hardware_info", "Hardware-info"),
                     ("partnummer", "Partnummer"), ("firmware_versie", "Firmwareversie"), ("can_adres", "CAN-adres")):
        if k in info:
            job.log(f"{label}: {info[k]}")
    if "0x232:1" not in waarden:
        job.log("Index 0x232 gaf niets: die vraagt eRob-firmware X5061715X of nieuwer.")
    job.klaar({"id": node, "waarden": waarden, "meldingen": meldingen, "andere_master": len(vreemde), **info})
