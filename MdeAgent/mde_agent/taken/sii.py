"""
EEPROM-inhoud (SII, Slave Information Interface) van een EtherCAT-slave maken uit zijn ESI-bestand, zoals
TwinCAT dat doet bij "Write EEPROM". Opbouw volgens ETG.1000.6 / ETG.2010 V1.0.3:

    woord 0x00-0x06  ConfigData uit het ESI: PDI-control (welke interface tussen ESC en µC, bv. SPI),
                     PDI-configuratie, sync-impuls, alias (0x04; uit het ESI telt als 0, ETG.2000)
    woord 0x07       CRC-8 over woord 0-6 (laag byte)
    woord 0x08-0x0F  vendor, productcode, revisie, serienummer (elk 32 bit)
    woord 0x14-0x17  bootstrap-mailbox (uit <BootStrap>)
    woord 0x18-0x1B  standaardmailbox (uit de Sm MBoxOut/MBoxIn)
    woord 0x1C       mailboxprotocollen (AoE 1, EoE 2, CoE 4, FoE 8, SoE 0x10, VoE 0x20)
    woord 0x28-0x2F  ConfigData2 (tweede ESC-configuratie, alleen als het ESI hem heeft) met eigen CRC
    woord 0x3E       EEPROM-grootte in kbit - 1;  0x3F  versie (1)
    vanaf 0x40       categorieën, oplopend op type (Compendium §4.2): apparaatspecifiek (1-9, van de slave
                     zelf, bv. kalibratie), Strings (10), General (30), FMMU (40), SyncM (41), TxPDO (50, één per
                     PDO), RxPDO (51), DC (60), eigen categorieën uit <Eeprom><Category>, End (0xFFFF)

Verschillen die een collega (M-Pede Tool Configurator, 30-10-2026) met ETG.2010 vond en die hier zijn opgelost:
General-flags (bit 2 MboxDataLinkLayer e.d., Table 7), PDO-flags (0x0002 PdoDefault bij een @Sm, Table 15),
alleen PDO's met een @Sm, de DC-categorie, en het bewaren van apparaatspecifieke categorieën.

Alleen standaardbibliotheek, zodat het ook zonder pysoem te toetsen is: `maak(esi_pad, serienummer)` geeft
(bytes, samenvatting).
"""

import struct
import xml.etree.ElementTree as ET

CAT_STRINGS, CAT_GENERAL, CAT_FMMU, CAT_SYNCM, CAT_TXPDO, CAT_RXPDO, CAT_DC, CAT_EINDE = 10, 30, 40, 41, 50, 51, 60, 0xFFFF

DATATYPE = {"BOOL": 0x01, "BIT": 0x01, "SINT": 0x02, "INT": 0x03, "DINT": 0x04, "USINT": 0x05, "UINT": 0x06,
            "UDINT": 0x07, "REAL": 0x08, "STRING": 0x09, "INT24": 0x10, "LREAL": 0x11, "INT40": 0x12, "INT48": 0x13,
            "INT56": 0x14, "LINT": 0x15, "UINT24": 0x16, "UINT40": 0x18, "UINT48": 0x19, "UINT56": 0x1A,
            "ULINT": 0x1B, "BYTE": 0x05, "WORD": 0x06, "DWORD": 0x07, "LWORD": 0x1B,
            **{f"BIT{n}": 0x2F + n for n in range(1, 9)}}

# Device/@Physics, per poort een letter (ETG.2000) -> poortsoort in General (ETG.2010 Table 7)
FYSICA = {"Y": 0x1, "K": 0x3, "H": 0x4}       # MII, E-Bus, Fast Hot Connect

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


def _waar(attr) -> bool:
    return str(attr or "").strip().lower() in ("true", "1")


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


def categorieen(data: bytes) -> list[tuple[int, bytes]]:
    """(type, inhoud) van elke categorie vanaf woord 0x40 tot de End-markering (of tot de data op is)."""
    uit, w = [], 0x40
    while w * 2 + 4 <= len(data):
        soort, lengte = struct.unpack_from("<HH", data, w * 2)
        if soort in (CAT_EINDE, 0) or (w + 2 + lengte) * 2 > len(data):
            break
        uit.append((soort, data[(w + 2) * 2:(w + 2 + lengte) * 2]))
        w += 2 + lengte
    return uit


def is_apparaatspecifiek(soort: int) -> bool:
    """Type 1-9: 'shall not be overwritten by MainDevice or configuration tool' (ETG.2010 Table 5; kalibratie)."""
    return 1 <= (soort & 0x7FFF) <= 9


def is_leverancierspecifiek(soort: int) -> bool:
    """Vendor-/applicatiespecifiek (bit 15, of 0x0800 en hoger): kan nodig zijn voor de werking (ETG.2010 Table 5)."""
    return bool(soort & 0x8000) or (soort & 0x7FFF) >= 0x0800


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


def maak(esi_pad: str, serienummer: int = 0, alias: int = 0, productcode: int | None = None,
         behouden: list[tuple[int, bytes]] | None = None) -> tuple[bytes, dict]:
    """EEPROM-beeld uit het ESI, tot en met de End-markering. `alias` = node-adres in woord 4 (0 = geen; het
    ESI telt niet mee, ETG.2000), `behouden` = apparaatspecifieke categorieën (type 1-9) van de slave die
    mee moeten in het nieuwe beeld."""
    root, dev = kies_apparaat(esi_pad, productcode)
    vendor = _getal(root.findtext("./Vendor/Id"))
    typ = dev.find("Type")
    product = _getal(typ.get("ProductCode"))
    revisie = _getal(typ.get("RevisionNo"))
    naam = (dev.findtext("Name") or typ.text or "").strip()
    groep_type = (dev.findtext("GroupType") or "").strip()
    eeprom = dev.find("Eeprom")
    grootte = _getal(eeprom.findtext("ByteSize") if eeprom is not None else None, 2048)
    config = _hexbytes(eeprom.findtext("ConfigData") if eeprom is not None else None)
    config2 = _hexbytes(eeprom.findtext("ConfigData2") if eeprom is not None else None)
    bootstrap = _hexbytes(eeprom.findtext("BootStrap") if eeprom is not None else None)

    # ---- kop (woord 0-0x3F) ----
    kop = bytearray(128)
    kop[0:14] = (config + bytes(14))[:14]
    kop[8:10] = struct.pack("<H", (alias or 0) & 0xFFFF)     # alias uit het ESI telt als 0000 (ETG.2000)
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
    coe = None
    if mbx is not None:
        for tag, bit in (("AoE", 0x01), ("EoE", 0x02), ("CoE", 0x04), ("FoE", 0x08), ("SoE", 0x10), ("VoE", 0x20)):
            if mbx.find(tag) is not None:
                protocollen |= bit
        coe = mbx.find("CoE")
    struct.pack_into("<H", kop, 0x38, protocollen)
    if config2:
        b = (config2 + bytes(14))[:14]
        kop[0x50:0x5E] = b
        kop[0x5E] = crc8(b)
    kbit = max(1, grootte * 8 // 1024)
    struct.pack_into("<HH", kop, 0x7C, kbit - 1, 1)

    # ---- categorieën ----
    st = _Strings()
    i_groep = st.idx(groep_type)
    i_beeld = st.idx(dev.findtext("Image16x14"))
    i_order = st.idx((typ.text or naam).strip())
    i_naam = st.idx(naam)

    coe_det = 0
    if coe is not None:
        coe_det = 0x01 | (0x02 if _waar(coe.get("SdoInfo")) else 0) | (0x04 if _waar(coe.get("PdoAssign")) else 0) \
                  | (0x08 if _waar(coe.get("PdoConfig")) else 0) | (0x10 if _waar(coe.get("PdoUpload")) else 0) \
                  | (0x20 if _waar(coe.get("CompleteAccess")) else 0)
    # Flags (ETG.2010 Table 7/8): 0x01 Enable SafeOp, 0x02 Enable notLRW, 0x04 MboxDataLinkLayer (mailbox-
    # herhaling, ETG.2000 verplicht bij een mailbox), 0x08 ID in AL-statuscode, 0x10 ID op een geheugenadres
    info = dev.find("Info")
    gedrag = info.find("StateMachine/Behavior") if info is not None else None
    vlaggen = (0x01 if gedrag is not None and _waar(gedrag.get("StartToSafeopNoSync")) else 0) \
              | (0x02 if _waar(typ.get("UseLrdLwr")) else 0) \
              | (0x04 if mbx is not None and _waar(mbx.get("DataLinkLayer")) else 0)
    id_adres = 0
    if info is not None and info.find("IdentificationReg134") is not None:
        vlaggen |= 0x08
    if info is not None and info.find("IdentificationAdo") is not None:
        vlaggen |= 0x10
        id_adres = _getal(info.findtext("IdentificationAdo"))
    ebus = _getal(info.findtext("Electrical/EBusCurrent")) if info is not None else 0
    poort = 0
    for i, c in enumerate((dev.get("Physics") or "")[:4]):
        poort |= FYSICA.get(c.upper(), 0x0) << (4 * i)
    heeft = lambda tag: mbx is not None and mbx.find(tag) is not None  # noqa: E731
    algemeen = struct.pack("<BBBBBBBBBBBBhBBHH", i_groep, i_beeld, i_order, i_naam, 0, coe_det, int(heeft("FoE")),
                           int(heeft("EoE")), 0, 0, 0, vlaggen, ebus, i_groep, 0, poort, id_adres) + bytes(12)

    fmmu = bytes({"Outputs": 1, "Inputs": 2, "MBoxState": 3}.get((f.text or "").strip(), 0) for f in dev.findall("Fmmu"))

    syncm = b""
    for s in sms:
        aan = (0x01 if _getal(s.get("Enable"), 1) else 0) | (0x04 if _waar(s.get("Virtual")) else 0) \
              | (0x08 if _waar(s.get("OpOnly")) else 0)
        syncm += struct.pack("<HHBBBB", _getal(s.get("StartAddress")), _getal(s.get("DefaultSize") or s.get("MinSize")),
                             _getal(s.get("ControlByte")), 0, aan, soorten.get((s.text or "").strip(), 0))

    def pdo_cats(tag: str, soort: int) -> list[tuple[int, bytes]]:
        """Eén categorie per PDO, alleen PDO's die standaard aan een SyncManager hangen (@Sm; ETG.2010 Table 15)."""
        uit = []
        for p in dev.findall(tag):
            if p.get("Sm") is None:
                continue
            index = p.find("Index")
            vlag = (0x0001 if _waar(p.get("Mandatory")) else 0) | 0x0002 \
                   | (0x0010 if _waar(p.get("Fixed")) else 0) | (0x0020 if _waar(p.get("Virtual")) else 0) \
                   | (0x0200 if index is not None and _waar(index.get("DependOnSlot")) else 0) \
                   | (0x0400 if index is not None and _waar(index.get("DependOnSlotGroup")) else 0) \
                   | (0x0800 if _waar(p.get("OverwrittenByModule")) else 0)
            entries = p.findall("Entry")
            data = struct.pack("<HBBBBH", _getal(p.findtext("Index")), len(entries), _getal(p.get("Sm")), 0,
                               st.idx(p.findtext("Name")), vlag)
            for e in entries:
                idx = _getal(e.findtext("Index"))
                data += struct.pack("<HBBBBH", idx, _getal(e.findtext("SubIndex")), st.idx(e.findtext("Name")) if idx else 0,
                                    DATATYPE.get((e.findtext("DataType") or "").strip().upper(), 0) if idx else 0,
                                    _getal(e.findtext("BitLen")), 0)
            uit.append((soort, data))
        return uit

    pdos = pdo_cats("TxPdo", CAT_TXPDO) + pdo_cats("RxPdo", CAT_RXPDO)

    # DC (ETG.2010 Table 12): 24 bytes per Dc/OpMode; zonder <Dc> geen categorie
    dc = b""
    for op in dev.findall("Dc/OpMode"):
        c0, c1 = op.find("CycleTimeSync0"), op.find("CycleTimeSync1")
        dc += struct.pack("<IIIhHhBB4x", _getal(op.findtext("CycleTimeSync0")), _getal(op.findtext("ShiftTimeSync0")),
                          _getal(op.findtext("ShiftTimeSync1")), _getal(c1.get("Factor")) if c1 is not None else 0,
                          _getal(op.findtext("AssignActivate")), _getal(c0.get("Factor")) if c0 is not None else 0,
                          st.idx(op.findtext("Name")), st.idx(op.findtext("Desc")))

    eigen = [(_getal(c.findtext("CatNo")), _hexbytes(c.findtext("Data"))) for c in (eeprom.findall("Category") if eeprom is not None else [])]
    behouden = [c for c in (behouden or []) if is_apparaatspecifiek(c[0])]
    if behouden:        # die van de slave gaan voor wat het ESI als apparaatspecifiek heeft
        eigen = [c for c in eigen if not is_apparaatspecifiek(c[0])]

    lijst = behouden + [(CAT_STRINGS, st.categorie()), (CAT_GENERAL, algemeen)] \
            + ([(CAT_FMMU, fmmu)] if fmmu else []) + ([(CAT_SYNCM, syncm)] if syncm else []) + pdos \
            + ([(CAT_DC, dc)] if dc else []) + eigen
    lijst.sort(key=lambda c: c[0] & 0x7FFF)            # stabiel: de volgorde van de PDO's blijft
    cats = b"".join(_categorie(t, d) for t, d in lijst) + struct.pack("<H", CAT_EINDE)
    data = bytes(kop) + cats
    if len(data) > grootte:
        raise ValueError(f"EEPROM-inhoud is {len(data)} bytes, de EEPROM maar {grootte}.")
    if len(data) % 2:
        data += b"\xFF"

    pdi = kop[0]
    samenvatting = {
        "naam": naam, "vendor": f"0x{vendor:08X}", "productcode": f"0x{product:08X}", "revisie": f"0x{revisie:08X}",
        "serienummer": serienummer, "alias": alias or 0, "pdi": f"0x{pdi:02X} {PDI_TYPEN.get(pdi, 'onbekend')}",
        "mailbox": f"0x{struct.unpack_from('<H', kop, 0x30)[0]:04X}/{struct.unpack_from('<H', kop, 0x32)[0]} B uit, "
                   f"0x{struct.unpack_from('<H', kop, 0x34)[0]:04X}/{struct.unpack_from('<H', kop, 0x36)[0]} B in",
        "protocollen": ", ".join(n for n, b in (("AoE", 1), ("EoE", 2), ("CoE", 4), ("FoE", 8), ("SoE", 0x10), ("VoE", 0x20)) if protocollen & b),
        "vlaggen": f"0x{vlaggen:02X}", "behouden": [t & 0x7FFF for t, _ in behouden],
        "bytes": len(data), "eeprom": grootte,
    }
    return data, samenvatting


def lees_identiteit(kop: bytes) -> tuple[int, int, int, int]:
    """(vendor, productcode, revisie, serienummer) uit woord 0x08-0x0F van een EEPROM-kop."""
    return struct.unpack_from("<IIII", kop, 0x10)
