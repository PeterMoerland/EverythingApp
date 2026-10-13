"""
Taak can.meeluisteren: op de CAN-bus meeluisteren met een GCAN USBCAN (ZLG/ECanVci-API) en
vertalen wat er langskomt. Bedoeld om te zien wat eRob-Studio/eTuner leest en schrijft.

Twee protocollen worden herkend:
  - CANopen-SDO's (0x600+node aanvraag, 0x580+node antwoord, altijd 8 bytes);
  - het eigen ZeroErr 'CAN Custom Protocol' van de eRob (0x640+ID aanvraag, 0x5C0+ID antwoord):
        lezen      : [index_hi, index_lo] of [index_hi, index_lo, sub_hi, sub_lo]
        schrijven  : [index_hi, index_lo, w3, w2, w1, w0] (6 bytes) of met sub (8 bytes)
        antwoord   : [w3, w2, w1, w0, 0x3E] (waarde, big-endian) · [0x3E] (ok) · [0x80] (fout)
    De eRob antwoordt in volgorde; aanvragen en antwoorden worden daarom als wachtrij gekoppeld.

De agent zendt niets (listen-only); de adapter hangt parallel aan de bus van eRob-Studio.

Parameters:
    seconden      hoe lang luisteren (standaard 30, max 600); Stop rondt eerder af
    baud          1000 / 500 / 250 / 125 kbit (standaard 1000)
    kanaal        0 = CAN1, 1 = CAN2 (standaard 0)
    apparaattype  ECanVci-apparaattype (3 = USBCAN-I, 4 = USBCAN-II; leeg = proberen)
    index         apparaatindex (leeg = 0, 1, 2 proberen)

Resultaat: {"frames": n, "sdo": [{"t", "richting", "node", "index", "sub", "waarde", "hex", "protocol"}...],
            "overig": [...], "cobids": {...}, "voorbeeld": {...}, "bestand": "...csv", "gestopt": bool}
"""

import os
import time

from . import can_bus
from .erob_can import EROB_OBJECTEN, ontleed_aanvraag, ontleed_antwoord

NAAM = "can.meeluisteren"
OMSCHRIJVING = "CAN meeluisteren (GCAN USBCAN): CANopen-SDO's en eRob CAN-protocol vertalen"

# CANopen SDO command specifiers die in byte 0 kunnen staan (initiate/segment/abort)
_SDO_CS = {0x21, 0x22, 0x23, 0x27, 0x2B, 0x2F, 0x40, 0x41, 0x42, 0x43, 0x47, 0x4B, 0x4F,
           0x60, 0x70, 0x00, 0x10, 0x01, 0x11, 0x80}


def beschikbaar(inst: dict):
    return can_bus.beschikbaar(inst)


def _sdo(data: bytes) -> dict:
    """Een CANopen SDO-frame (8 bytes) ontleden: expedited download/upload en aborts."""
    cs = data[0]
    index = data[1] | (data[2] << 8)
    sub = data[3]
    uit = {"index": f"0x{index:04X}", "sub": sub, "cs": f"0x{cs:02X}", "protocol": "canopen"}
    ccs = cs >> 5
    if cs == 0x80:
        uit["richting"] = "abort"
        uit["waarde"] = f"0x{int.from_bytes(data[4:8], 'little'):08X}"
        return uit
    if ccs == 1:      # initiate download (client → server) = schrijven
        uit["richting"] = "download"
    elif ccs == 2:    # initiate upload request = lezen
        uit["richting"] = "upload"
        return uit
    elif ccs == 3:    # download response
        uit["richting"] = "download-ok"
        return uit
    else:
        uit["richting"] = f"ccs{ccs}"
    expedited = cs & 0x02
    if expedited:
        n = 4 - ((cs >> 2) & 0x03) if cs & 0x01 else 4
        ruw = data[4:4 + n]
        uit["bytes"] = n
        uit["waarde"] = int.from_bytes(ruw, "little")
        uit["signed"] = int.from_bytes(ruw, "little", signed=True)
        uit["hex"] = ruw.hex()
    else:
        uit["waarde"] = None
        uit["hex"] = data[4:8].hex()
        uit["segmented"] = True
    return uit


def _is_canopen_sdo(data: bytes) -> bool:
    return len(data) == 8 and data[0] in _SDO_CS


def voer_uit(job, inst: dict, parameters: dict) -> None:
    seconden = max(1, min(600, int(parameters.get("seconden") or 30)))
    baud = int(parameters.get("baud") or 1000)
    kanaal = int(parameters.get("kanaal") or 0)

    from .. import instellingen
    os.makedirs(instellingen.WERKMAP, exist_ok=True)
    bestand = os.path.join(instellingen.WERKMAP, time.strftime("can_%Y%m%d_%H%M%S.csv"))

    bus = can_bus.CanBus.open(inst, job, baud=baud, kanaal=kanaal, luister_alleen=True, seconden=seconden,
                              apparaattype=parameters.get("apparaattype"), index=parameters.get("index"))
    job.log(f"Luisteren, {seconden} s … (doe nu in eRob-Studio/eTuner wat je wilt zien, bv. Apply of Information). Stop = eerder afronden.")
    job.log(f"Logbestand: {bestand}")

    frames = 0
    sdo: list[dict] = []
    overig: list[dict] = []
    per_cobid: dict[int, int] = {}
    voorbeeld: dict[int, list[str]] = {}
    wachtrij: dict[int, list[dict]] = {}       # eRob-protocol: openstaande aanvragen per drive-ID
    begin = time.time()
    laatste_log = begin
    gestopt = False
    csv = open(bestand, "w", encoding="utf-8", newline="")
    csv.write("t;cobid;node;dlc;data;protocol;richting;index;sub;waarde;hex;naam\n")
    try:
        with bus:
            for frame in bus.frames():
                if getattr(job, "stop_gevraagd", False):
                    gestopt = True
                    bus.stop()
                    break
                if time.time() - laatste_log > 5:
                    job.log(f"  … {frames} frames ({int(time.time() - begin)} s)")
                    laatste_log = time.time()
                    job.voortgang(int(min(99, (time.time() - begin) / seconden * 100)))
                if frame is None:
                    continue
                t, can_id, ext, data = frame
                frames += 1
                cobid = can_id & 0x7FF if not ext else can_id
                per_cobid[cobid] = per_cobid.get(cobid, 0) + 1
                basis = cobid & 0x780
                vb = voorbeeld.setdefault(cobid, [])
                if len(vb) < 3:
                    vb.append(f"[{len(data)}] {data.hex() or '-'}")

                rec = None
                if basis in (0x600, 0x580) and _is_canopen_sdo(data):
                    fn = cobid & 0x7F
                    rec = {"t": t, "node": fn, "cobid": f"0x{cobid:03X}", **_sdo(data)}
                    if basis == 0x580 and rec["richting"] == "download":
                        rec["richting"] = "antwoord"      # upload response = waarde uit de drive
                elif 0x640 <= cobid <= 0x6BF and 1 <= len(data) <= 8:
                    # eRob custom: aanvraag
                    node = cobid - 0x640
                    rec = {"t": t, "node": node, "cobid": f"0x{cobid:03X}", **ontleed_aanvraag(data)}
                    rij = wachtrij.setdefault(node, [])
                    rij.append(rec)
                    if len(rij) > 50:
                        del rij[:-50]
                elif 0x5C0 <= cobid <= 0x63F and 1 <= len(data) <= 8:
                    # eRob custom: antwoord → koppelen aan de oudste openstaande aanvraag
                    node = cobid - 0x5C0
                    rij = wachtrij.get(node) or []
                    aanvraag = rij.pop(0) if rij else None
                    rec = {"t": t, "node": node, "cobid": f"0x{cobid:03X}", **ontleed_antwoord(data, aanvraag)}

                if rec is not None:
                    sdo.append(rec)
                    if len(sdo) <= 600:
                        w = rec.get("waarde")
                        naam = rec.get("naam") or ""
                        job.log(f"  {t:7.3f} n{rec['node']} {rec['protocol'][:4]} {rec['richting']:<11} {rec.get('index', '')}:{rec.get('sub', '')}"
                                + (f" = {w} (0x{w:X})" if isinstance(w, int) else (f" {w}" if w else ""))
                                + (f"  {naam}" if naam else ""))
                elif len(overig) < 200 and basis not in (0x180, 0x200, 0x280, 0x300, 0x380, 0x400, 0x480, 0x500):
                    overig.append({"t": t, "cobid": f"0x{cobid:03X}", "data": data.hex()})

                csv.write(f"{t};0x{cobid:03X};{(rec or {}).get('node', '')};{len(data)};{data.hex()};"
                          + (f"{rec['protocol']};{rec['richting']};{rec.get('index', '')};{rec.get('sub', '')};{rec.get('waarde', '')};{rec.get('hex', '')};{rec.get('naam', '')}"
                             if rec else ";;;;;;")
                          + "\n")
    finally:
        csv.close()

    job.log(f"{'Gestopt' if gestopt else 'Klaar'}: {frames} frames, {len(sdo)} vertaalde berichten. COB-ID's: "
            + ", ".join(f"0x{k:03X}×{v}" for k, v in sorted(per_cobid.items())[:20]))
    for k in sorted(voorbeeld)[:20]:
        job.log(f"  0x{k:03X}: " + "  |  ".join(voorbeeld[k]))
    # Welke eRob-objecten kwamen langs (los van de constante polling)?
    gezien: dict[str, int] = {}
    for r in sdo:
        if r.get("protocol") == "erob" and r.get("richting") in ("lees", "schrijf", "commando"):
            sleutel = f"{r.get('index')}:{r.get('sub')}"
            gezien[sleutel] = gezien.get(sleutel, 0) + 1
    if gezien:
        job.log("eRob-objecten gevraagd: " + ", ".join(
            f"{k}×{v}" + (f" ({EROB_OBJECTEN[k]})" if k in EROB_OBJECTEN else "")
            for k, v in sorted(gezien.items(), key=lambda kv: -kv[1])[:30]))
    job.log(f"Alle frames staan in {bestand}")
    job.klaar({"frames": frames, "sdo": sdo, "overig": overig, "bestand": bestand, "gestopt": gestopt,
               "cobids": {f"0x{k:03X}": v for k, v in per_cobid.items()},
               "voorbeeld": {f"0x{k:03X}": v for k, v in voorbeeld.items()}})
