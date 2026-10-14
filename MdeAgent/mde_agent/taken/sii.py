"""
EEPROM-inhoud (SII, Slave Information Interface) van een EtherCAT-slave maken uit zijn ESI-bestand, zoals
TwinCAT dat doet bij "Write EEPROM". Opbouw volgens ETG.1000.6 / ETG.2010:

    woord 0x00-0x06  ConfigData uit het ESI: PDI-control (welke interface tussen ESC en µC, bv. SPI),
                     PDI-configuratie, sync-impuls, alias (0x04)
    woord 0x07       CRC-8 over woord 0-6 (laag byte)
    woord 0x08-0x0F  vendor, productcode, revisie, serienummer (elk 32 bit)
    woord 0x14-0x17  bootstrap-mailbox (uit <BootStrap>)
    woord 0x18-0x1B  standaardmailbox (uit de Sm MBoxOut/MBoxIn)
    woord 0x1C       mailboxprotocollen (AoE 1, EoE 2, CoE 4, FoE 8, SoE 0x10, VoE 0x20)
    woord 0x3E       EEPROM-grootte in kbit - 1;  0x3F  versie (1)
    vanaf 0x40       categorieën: Strings (10), General (30), FMMU (40), SyncM (41), TxPDO (50), RxPDO (51), End

Alleen standaardbibliotheek, zodat het ook zonder pysoem te toetsen is: `maak(esi_pad, serienummer)` geeft
(bytes, samenvatting).
"""

import struct
import xml.etree.ElementTree as ET

CAT_STRINGS, CAT_GENERAL, CAT_FMMU, CAT_SYNCM, CAT_TXPDO, CAT_RXPDO = 10, 30, 40, 41, 50, 51

DATATYPE = {"BOOL": 0x01, "BIT": 0x01, "SINT": 0x02, "INT": 0x03, "DINT": 0x04, "USINT": 0x05, "UINT": 0x06,
            "UDINT": 0x07, "REAL": 0x08, "LREAL": 0x11, "LINT": 0x15, "ULINT": 0x1B, "BYTE": 0x05, "WORD": 0x06,
            "DWORD": 0x07}

# PDI-control (ESC-register 0x0140, EEPROM woord 0 laag byte): welke interface de ESC naar de µC gebruikt
PDI_TYPEN = {0x00: "geen PDI", 0x04: "digitale I/O", 0x05: "SPI", 0x07: "EtherCAT Bridge", 0x08: "16-bit async µC",
             0x09: "8-bit async µC", 0x0A: "16-bit sync µC", 0x0B: "8-bit sync µC", 0x80: "SPI (LAN9252/LAN9253-familie)",
             0x88: "HBI (LAN925x)"}


def _getal(tekst, standaard=0) -> int:
    if tekst is None:
        return standaard
    t = str(tekst).strip()
    if t.lower().startswith("#x"):
        return int(t[2:], 16)
    if t.lower().startswith("0x"):
        return int(t[2:], 16)
    return int(t) if t else standaard


def _hexbytes(tekst: str | None) -> bytes:
    t = (tekst or "").strip().replace(" ", "")
    return bytes.fromhex(t) if t else b""


def crc8(data: bytes) -> int:
    """SII-checksum: CRC-8, polynoom 0x07, startwaarde 0xFF, over woord 0..6 (14 bytes)."""
    crc = 0xFF
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = ((crc << 1) ^ 0x07) & 0xFF if crc & 0x80 else (crc << 1) & 0xFF
    return crc


class _Strings:
    def __init__(self):
        self.lijst: list[str] = []

    def idx(self, s: str | None) -> int:
        s = (s or "").strip()
        if not s:
            return 0
        b = s.encode("ascii", "replace")[:255].decode("ascii")
        if b not in self.lijst:
            self.lijst.append(b)
        return self.lijst.index(b) + 1      # 1-gebaseerd; 0 = geen naam

    def categorie(self) -> bytes:
        data = bytes([len(self.lijst)])
        for s in self.lijst:
            e = s.encode("ascii", "replace")
            data += bytes([len(e)]) + e
        return data


def _categorie(soort: int, data: bytes) -> bytes:
    if len(data) % 2:
        data += b"\x00"
    return struct.pack("<HH", soort, len(data) // 2) + data


def kies_apparaat(esi_pad: str, productcode: int | None = None):
    root = ET.parse(esi_pad).getroot()
    apparaten = root.findall(".//Descriptions/Devices/Device")
    if not apparaten:
        raise ValueError("Het ESI-bestand bevat geen <Device>.")
    if productcode is not None:
        for a in apparaten:
            if _getal(a.find("Type").get("ProductCode")) == productcode:
                return root, a
    return root, apparaten[0]


def maak(esi_pad: str, serienummer: int = 0, alias: int | None = None, productcode: int | None = None) -> tuple[bytes, dict]:
    root, dev = kies_apparaat(esi_pad, productcode)
    vendor = _getal(root.findtext("./Vendor/Id"))
    typ = dev.find("Type")
    product = _getal(typ.get("ProductCode"))
    revisie = _getal(typ.get("RevisionNo"))
    naam = (dev.findtext("Name") or typ.text or "").strip()
    groep_type = (dev.findtext("GroupType") or "").strip()
    groep_naam = ""
    for g in root.findall(".//Descriptions/Groups/Group"):
        if (g.findtext("Type") or "").strip() == groep_type:
            groep_naam = (g.findtext("Name") or groep_type).strip()
    eeprom = dev.find("Eeprom")
    grootte = _getal(eeprom.findtext("ByteSize") if eeprom is not None else None, 2048)
    config = _hexbytes(eeprom.findtext("ConfigData") if eeprom is not None else None)
    bootstrap = _hexbytes(eeprom.findtext("BootStrap") if eeprom is not None else None)

    # ---- kop (woord 0-0x3F) ----
    kop = bytearray(128)
    kop[0:14] = (config + bytes(14))[:14]
    if alias is not None:
        kop[8:10] = struct.pack("<H", alias & 0xFFFF)          # alias (woord 4) van de print behouden
    kop[14] = crc8(bytes(kop[0:14]))
    kop[15] = 0
    struct.pack_into("<IIII", kop, 0x10, vendor, product, revisie, serienummer & 0xFFFFFFFF)
    if len(bootstrap) >= 8:
        kop[0x28:0x30] = bootstrap[:8]

    sms = dev.findall("Sm")
    soorten = {"MBoxOut": 1, "MBoxIn": 2, "Outputs": 3, "Inputs": 4}
    mbx_uit = next((s for s in sms if (s.text or "").strip() == "MBoxOut"), None)
    mbx_in = next((s for s in sms if (s.text or "").strip() == "MBoxIn"), None)
    if mbx_uit is not None and mbx_in is not None:
        struct.pack_into("<HHHH", kop, 0x30,
                         _getal(mbx_uit.get("StartAddress")), _getal(mbx_uit.get("DefaultSize") or mbx_uit.get("MinSize")),
                         _getal(mbx_in.get("StartAddress")), _getal(mbx_in.get("DefaultSize") or mbx_in.get("MinSize")))
    mbx = dev.find("Mailbox")
    protocollen = 0
    coe = foe = None
    if mbx is not None:
        for tag, bit in (("AoE", 0x01), ("EoE", 0x02), ("CoE", 0x04), ("FoE", 0x08), ("SoE", 0x10), ("VoE", 0x20)):
            if mbx.find(tag) is not None:
                protocollen |= bit
        coe = mbx.find("CoE")
        foe = mbx.find("FoE")
    struct.pack_into("<H", kop, 0x38, protocollen)
    kbit = max(1, grootte * 8 // 1024)
    struct.pack_into("<HH", kop, 0x7C, kbit - 1, 1)

    # ---- categorieën ----
    st = _Strings()
    i_groep = st.idx(groep_type)
    i_naam = st.idx(naam)
    i_order = st.idx((typ.text or naam).strip())

    def waar(attr) -> bool:
        return str(attr or "").lower() in ("true", "1")

    coe_det = 0
    if coe is not None:
        coe_det = 0x01 | (0x02 if waar(coe.get("SdoInfo")) else 0) | (0x04 if waar(coe.get("PdoAssign")) else 0) \
                  | (0x08 if waar(coe.get("PdoConfig")) else 0) | (0x10 if waar(coe.get("PdoUpload")) else 0) \
                  | (0x20 if waar(coe.get("CompleteAccess")) else 0)
    poort = 0
    for i, c in enumerate((dev.get("Physics") or "")[:4]):
        poort |= {"Y": 0x1, "H": 0x1, "K": 0x3}.get(c.upper(), 0x0) << (4 * i)
    algemeen = struct.pack("<BBBBBBBBBBBBhBBHH", i_groep, 0, i_order, i_naam, 0, coe_det, 1 if foe is not None else 0,
                           0, 0, 0, 0, 0, 0, i_groep, 0, poort, 0) + bytes(12)

    fmmu = bytes({"Outputs": 1, "Inputs": 2, "MBoxState": 3}.get((f.text or "").strip(), 0) for f in dev.findall("Fmmu"))

    syncm = b""
    for s in sms:
        syncm += struct.pack("<HHBBBB", _getal(s.get("StartAddress")), _getal(s.get("DefaultSize") or s.get("MinSize")),
                             _getal(s.get("ControlByte")), 0, 1 if _getal(s.get("Enable"), 1) else 0,
                             soorten.get((s.text or "").strip(), 0))

    def pdo_cat(tag: str) -> bytes:
        data = b""
        for p in dev.findall(tag):
            entries = p.findall("Entry")
            vlag = (0x0001 if waar(p.get("Mandatory")) else 0) | (0x0010 if waar(p.get("Fixed")) else 0)
            data += struct.pack("<HBBBBH", _getal(p.findtext("Index")), len(entries), _getal(p.get("Sm"), 0), 0,
                                st.idx(p.findtext("Name")), vlag)
            for e in entries:
                idx = _getal(e.findtext("Index"))
                data += struct.pack("<HBBBBH", idx, _getal(e.findtext("SubIndex")), st.idx(e.findtext("Name")) if idx else 0,
                                    DATATYPE.get((e.findtext("DataType") or "").strip().upper(), 0) if idx else 0,
                                    _getal(e.findtext("BitLen")), 0)
        return data

    tx = pdo_cat("TxPdo")
    rx = pdo_cat("RxPdo")
    cats = _categorie(CAT_STRINGS, st.categorie()) + _categorie(CAT_GENERAL, algemeen)
    if fmmu:
        cats += _categorie(CAT_FMMU, fmmu)
    if syncm:
        cats += _categorie(CAT_SYNCM, syncm)
    if tx:
        cats += _categorie(CAT_TXPDO, tx)
    if rx:
        cats += _categorie(CAT_RXPDO, rx)
    cats += b"\xFF\xFF"
    data = bytes(kop) + cats
    if len(data) > grootte:
        raise ValueError(f"EEPROM-inhoud is {len(data)} bytes, de EEPROM maar {grootte}.")
    if len(data) % 2:
        data += b"\xFF"

    pdi = kop[0]
    samenvatting = {
        "naam": naam, "vendor": f"0x{vendor:08X}", "productcode": f"0x{product:08X}", "revisie": f"0x{revisie:08X}",
        "serienummer": serienummer, "pdi": f"0x{pdi:02X} {PDI_TYPEN.get(pdi, 'onbekend')}",
        "mailbox": f"0x{struct.unpack_from('<H', kop, 0x30)[0]:04X}/{struct.unpack_from('<H', kop, 0x32)[0]} B uit, "
                   f"0x{struct.unpack_from('<H', kop, 0x34)[0]:04X}/{struct.unpack_from('<H', kop, 0x36)[0]} B in",
        "protocollen": ", ".join(n for n, b in (("AoE", 1), ("EoE", 2), ("CoE", 4), ("FoE", 8), ("SoE", 0x10), ("VoE", 0x20)) if protocollen & b),
        "bytes": len(data), "eeprom": grootte,
    }
    return data, samenvatting


def lees_identiteit(kop: bytes) -> tuple[int, int, int, int]:
    """(vendor, productcode, revisie, serienummer) uit woord 0x08-0x0F van een EEPROM-kop."""
    return struct.unpack_from("<IIII", kop, 0x10)
