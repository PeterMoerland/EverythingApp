"""
Generieke CoE-drives (CANopen over EtherCAT, CiA402) — in het bijzonder de ZeroErr
eRob-actuatoren (eRob70/80/90/110; vendor 0x5A65726F, productcode 0x00029252).
Geen Ingenia-bibliotheek: alles gaat met SDO's via pysoem, in een apart werker-proces.

Taak coe.parameters
    parameters: positie (1 = eerste slave op de bus), parameters (URL/pad van het JSON-bestand)
    JSON-formaat (zelfde idee als bij Feetech):
        {"protocol": "coe", "model": "eRob110", "omschrijving": "…",
         "registers": [{"index": "0x6081", "sub": 0, "type": "UDINT", "naam": "Profile velocity", "waarde": 100000}, …],
         "opslaan": true}
    Schrijft alle registers, leest ze terug, bewaart in EEPROM (0x1010:01 = 'save'; klaar bij 2)
    en leest daarna nog eens terug. Resultaat: {"registers": n, "afwijkingen": m, "afwijkend": [...],
    "firmware": "…", "serienummer": "…"}.

Taak coe.hoek
    parameters: positie, graden (doelhoek, absoluut op de uitgaande as), snelheid (°/s, standaard 10),
                versnelling (°/s², standaard 20), relatief (false), tolerantie (°, standaard 0.05),
                timeout (s, standaard 60), telling_per_omw (standaard 524288 = eRob-lastzijde),
                vrijgeven_na (true = na afloop weer uitschakelen)
    Profile-position-mode volgens de handleiding: 0x6060=1, doel/snelheid/acc, controlword 6→7→15,
    bit 4 pulsen, wachten op statusword bit 10 (target reached). Resultaat: {"hoek": gemeten °, "positie": counts,
    "doel": counts, "afwijking": °}.

Taak coe.positie
    parameters: positie. Leest 0x6064 (positie), 0x6041 (statusword), 0x603F (foutcode), 0x22A2 (temperatuur).

Taak coe.vrijgeven
    parameters: positie. Drive naar 'switch on disabled' (motor los, rem valt in als die er is).
"""

import json
import struct
import subprocess
import sys
import threading
import time

from . import _hulp

NAAM = "coe.parameters"
OMSCHRIJVING = "CoE-drive (eRob) parameters schrijven"

TYPEN = {
    "BOOL": ("<B", 1), "USINT": ("<B", 1), "SINT": ("<b", 1), "UINT": ("<H", 2), "INT": ("<h", 2),
    "UDINT": ("<I", 4), "DINT": ("<i", 4), "ULINT": ("<Q", 8), "LINT": ("<q", 8), "REAL": ("<f", 4),
}
SAVE = 0x65766173          # 'save' voor 0x1010:01
TELLING_PER_OMW = 524288   # eRob: single-turn resolutie van de encoder aan de lastzijde (19 bit)


def beschikbaar(inst: dict):
    try:
        import pysoem  # noqa: F401
    except Exception as e:  # noqa: BLE001
        return False, f"pysoem ontbreekt: {e}"
    return True, ""


def _ifname(inst: dict) -> str:
    ifname = inst.get("ethercat_interface_gevonden") or inst.get("ethercat_interface") or ""
    if not ifname:
        raise RuntimeError("Geen EtherCAT-adapter bekend; lees eerst de drives uit (Automatisch uitlezen).")
    return ifname


def _int(v) -> int:
    if isinstance(v, str):
        return int(v.strip(), 0)
    return int(v)


# ---------------------------------------------------------------------------
#  Werker: draait in een apart proces (MdeAgent --werker coe <ifname> <json-opdracht>)
# ---------------------------------------------------------------------------

class _Drive:
    """Eén slave op de bus, met SDO-helpers in gewone Python-types."""

    def __init__(self, master, positie: int):
        n = master.config_init()
        if n < positie:
            raise RuntimeError(f"{n} drive(s) op de bus, positie {positie} bestaat niet.")
        self.slave = master.slaves[positie - 1]
        self.master = master

    def lees(self, index: int, sub: int, typ: str):
        fmt, n = TYPEN[typ.upper()]
        ruw = self.slave.sdo_read(index, sub)
        return struct.unpack(fmt, ruw[:n])[0]

    def schrijf(self, index: int, sub: int, typ: str, waarde) -> None:
        fmt, _ = TYPEN[typ.upper()]
        self.slave.sdo_write(index, sub, struct.pack(fmt, waarde))

    def tekst(self, index: int, sub: int = 0) -> str:
        try:
            return self.slave.sdo_read(index, sub).split(b"\0")[0].decode("ascii", "ignore").strip()
        except Exception:  # noqa: BLE001
            return ""

    def identiteit(self) -> dict:
        try:
            sn = str(struct.unpack("<I", self.slave.sdo_read(0x1018, 4)[:4])[0])
        except Exception:  # noqa: BLE001
            sn = ""
        return {"naam": self.tekst(0x1008), "firmware": self.tekst(0x100A), "serienummer": sn,
                "productcode": f"0x{self.slave.id:08X}", "vendor": f"0x{self.slave.man:08X}"}

    # ---- CiA402 ----
    def status(self) -> int:
        return self.lees(0x6041, 0, "UINT")

    def controle(self, woord: int) -> None:
        self.schrijf(0x6040, 0, "UINT", woord)

    def wacht_status(self, masker: int, waarde: int, seconden: float, wat: str) -> int:
        einde = time.time() + seconden
        while time.time() < einde:
            sw = self.status()
            if (sw & masker) == waarde:
                return sw
            time.sleep(0.02)
        raise RuntimeError(f"Drive kwam niet in toestand '{wat}' (statusword 0x{self.status():04X}, foutcode 0x{self.lees(0x603F, 0, 'UINT'):04X}).")

    def inschakelen(self, log) -> None:
        sw = self.status()
        if sw & 0x0008:                      # fault → reset (bit 7 flank)
            log(f"Drive in fout (0x{self.lees(0x603F, 0, 'UINT'):04X}); fout resetten …")
            self.controle(0x0080); time.sleep(0.1); self.controle(0x0000); time.sleep(0.1)
        self.controle(0x0006); self.wacht_status(0x006F, 0x0021, 2, "ready to switch on")
        self.controle(0x0007); self.wacht_status(0x006F, 0x0023, 2, "switched on")
        self.controle(0x000F); self.wacht_status(0x006F, 0x0027, 3, "operation enabled")

    def uitschakelen(self) -> None:
        try:
            self.controle(0x0006)   # shutdown → ready to switch on
            time.sleep(0.05)
            self.controle(0x0000)   # → switch on disabled
        except Exception:  # noqa: BLE001
            pass


def _werker_parameters(d: _Drive, opdracht: dict, log) -> dict:
    regels = opdracht.get("registers") or []
    afwijkend = []
    n = 0
    for r in regels:
        if not r.get("index") or r.get("waarde") in (None, ""):
            continue
        index, sub, typ = _int(r["index"]), _int(r.get("sub", 0)), (r.get("type") or "UDINT").upper()
        if typ not in TYPEN:
            afwijkend.append({"index": f"0x{index:04X}:{sub}", "naam": r.get("naam", ""), "fout": f"onbekend type {typ}"})
            continue
        if index in (0x1010, 0x6040, 0x607A, 0x2242, 0x2420):
            # opslaan, controlword, doelpositie en encoder-reset horen niet in een parameterset
            log(f"  0x{index:04X} overgeslagen (wordt niet via een parameterset gezet).")
            continue
        waarde = _int(r["waarde"]) if typ != "REAL" else float(r["waarde"])
        try:
            d.schrijf(index, sub, typ, waarde)
            terug = d.lees(index, sub, typ)
            n += 1
            if terug != waarde:
                afwijkend.append({"index": f"0x{index:04X}:{sub}", "naam": r.get("naam", ""), "gewenst": waarde, "gelezen": terug})
                log(f"  0x{index:04X}:{sub} {r.get('naam', '')}: geschreven {waarde}, gelezen {terug}  ← AFWIJKING")
            else:
                log(f"  0x{index:04X}:{sub} {r.get('naam', '')} = {waarde}")
        except Exception as e:  # noqa: BLE001
            afwijkend.append({"index": f"0x{index:04X}:{sub}", "naam": r.get("naam", ""), "fout": str(e)})
            log(f"  0x{index:04X}:{sub} {r.get('naam', '')}: FOUT {e}")

    if opdracht.get("opslaan", True) and n > 0:
        log("Opslaan in EEPROM (0x1010:01 = 'save') …")
        d.schrijf(0x1010, 1, "UDINT", SAVE)
        einde = time.time() + 10
        stand = 0
        while time.time() < einde:
            time.sleep(0.5)
            try:
                stand = d.lees(0x1010, 1, "UDINT")
            except Exception:  # noqa: BLE001
                stand = 0
            if stand == 2:
                break
        if stand != 2:
            afwijkend.append({"index": "0x1010:1", "naam": "Save parameters", "fout": f"opslaan niet bevestigd (stand {stand})"})
            log(f"LET OP: opslaan niet bevestigd door de drive (stand {stand}).")
        else:
            log("Opgeslagen.")
            # na het opslaan nog eens teruglezen: wat er níet in EEPROM zit valt hier door de mand
            for r in regels:
                try:
                    index, sub, typ = _int(r["index"]), _int(r.get("sub", 0)), (r.get("type") or "UDINT").upper()
                    if typ not in TYPEN or index in (0x1010, 0x6040, 0x607A, 0x2242, 0x2420) or r.get("waarde") in (None, ""):
                        continue
                    waarde = _int(r["waarde"]) if typ != "REAL" else float(r["waarde"])
                    if d.lees(index, sub, typ) != waarde and not any(a["index"] == f"0x{index:04X}:{sub}" for a in afwijkend):
                        afwijkend.append({"index": f"0x{index:04X}:{sub}", "naam": r.get("naam", ""), "fout": "na opslaan anders dan geschreven"})
                except Exception:  # noqa: BLE001
                    pass
    ident = d.identiteit()
    return {"registers": n, "afwijkingen": len(afwijkend), "afwijkend": afwijkend, **ident}


def _werker_hoek(d: _Drive, o: dict, log) -> dict:
    telling = int(o.get("telling_per_omw") or TELLING_PER_OMW)
    per_graad = telling / 360.0
    graden = float(o.get("graden", 0))
    snelheid = max(0.5, float(o.get("snelheid") or 10))
    versnelling = max(1.0, float(o.get("versnelling") or 20))
    tolerantie = float(o.get("tolerantie") or 0.05)
    timeout = float(o.get("timeout") or 60)

    huidig = d.lees(0x6064, 0, "DINT")
    doel = int(round((huidig if o.get("relatief") else 0) + graden * per_graad))
    log(f"Huidige positie {huidig} counts ({huidig / per_graad:.3f}°); doel {doel} counts ({doel / per_graad:.3f}°).")

    d.schrijf(0x6060, 0, "SINT", 1)                              # profile position mode
    d.schrijf(0x6081, 0, "UDINT", int(snelheid * per_graad))     # counts/s
    d.schrijf(0x6083, 0, "UDINT", int(versnelling * per_graad))
    d.schrijf(0x6084, 0, "UDINT", int(versnelling * per_graad))
    d.schrijf(0x607A, 0, "DINT", doel)
    d.inschakelen(log)
    try:
        d.controle(0x000F); time.sleep(0.02)
        d.controle(0x003F)                                       # new set-point (bit 4) + change immediately (bit 5)
        time.sleep(0.05)
        d.controle(0x000F)                                       # bit 4 weer laag (flank-getriggerd)
        einde = time.time() + timeout
        laatste_log = 0.0
        while time.time() < einde:
            sw = d.status()
            pos = d.lees(0x6064, 0, "DINT")
            if sw & 0x0008:
                raise RuntimeError(f"Drive in fout tijdens beweging (foutcode 0x{d.lees(0x603F, 0, 'UINT'):04X}).")
            if (sw & 0x0400) and abs(pos - doel) <= tolerantie * per_graad:
                break
            if time.time() - laatste_log > 1.0:
                log(f"  … {pos / per_graad:.2f}°")
                laatste_log = time.time()
            time.sleep(0.05)
        else:
            raise RuntimeError(f"Doel niet bereikt binnen {timeout:.0f} s (positie {pos / per_graad:.3f}°, statusword 0x{sw:04X}).")
        time.sleep(0.2)
        pos = d.lees(0x6064, 0, "DINT")
    finally:
        if o.get("vrijgeven_na", True):
            d.uitschakelen()
    hoek = pos / per_graad
    log(f"Bereikt: {hoek:.3f}° ({pos} counts), afwijking {hoek - doel / per_graad:+.3f}°.")
    return {"hoek": round(hoek, 3), "positie": pos, "doel": doel, "afwijking": round(hoek - doel / per_graad, 3)}


# CoE-datatypen (ETG.1000.6) → struct-formaat; strings en onbekende typen komen als hex.
_DT = {0x0001: ("<B", "BOOL"), 0x0002: ("<b", "SINT"), 0x0003: ("<h", "INT"), 0x0004: ("<i", "DINT"),
       0x0005: ("<B", "USINT"), 0x0006: ("<H", "UINT"), 0x0007: ("<I", "UDINT"), 0x0008: ("<f", "REAL"),
       0x0011: ("<d", "LREAL"), 0x0015: ("<q", "LINT"), 0x001B: ("<Q", "ULINT"),
       0x0009: (None, "STRING"), 0x000A: (None, "OCTET_STRING"), 0x000B: (None, "UNICODE_STRING")}


def _decodeer(ruw: bytes, dt: int, bits: int):
    fmt, naam = _DT.get(dt, (None, f"0x{dt:04X}"))
    if fmt and len(ruw) >= struct.calcsize(fmt):
        return struct.unpack(fmt, ruw[:struct.calcsize(fmt)])[0], naam
    if naam == "STRING":
        return ruw.split(b"\0")[0].decode("ascii", "replace"), naam
    # onbekend type: klein getal little-endian, anders hex
    if 0 < len(ruw) <= 8 and bits in (8, 16, 32, 64):
        return int.from_bytes(ruw[:bits // 8], "little"), naam
    return ruw.hex(), naam


def _werker_scan(d: _Drive, o: dict, log) -> dict:
    """De objectdictionary in kaart brengen.

    Standaard 'aftasten': elk index in de opgegeven reeksen proberen te lezen (sub 0, daarna
    sub 1.. tot de eerste afwijzing). Een niet-bestaand object geeft meteen een SDO-abort,
    dus dat gaat snel; zo vinden we óók objecten die niet in het ESI/de handleiding staan.
    Optie methode='info' gebruikt SDO Info (pysoem slave.od) — sneller als de drive het
    ondersteunt, maar bij de eRob bleef dat hangen (per object een mailbox-time-out).
    Namen/typen plakt de app er daarna bij uit het ESI-bestand."""
    if (o.get("methode") or "aftasten") != "info":
        return _werker_aftasten(d, o, log)
    try:
        od = d.slave.od
    except Exception as e:  # noqa: BLE001
        raise RuntimeError(f"SDO Info (objectdictionary opvragen) mislukt: {e}. Ondersteunt de drive het niet, dan blijft alleen de handleiding over.")
    van = _int(o.get("van", 0x1000)); tot = _int(o.get("tot", 0xFFFF))
    met_waarden = o.get("waarden", True)
    regels = []
    for obj in od:
        idx = obj.index
        if idx < van or idx > tot:
            continue
        entries = list(getattr(obj, "entries", []) or [])
        if not entries:
            entries = [obj]  # VAR zonder subentries
        for sub, e in enumerate(entries):
            naam = getattr(e, "name", "") or getattr(obj, "name", "")
            if isinstance(naam, bytes):
                naam = naam.decode("ascii", "replace")
            dt = int(getattr(e, "data_type", 0) or 0)
            bits = int(getattr(e, "bit_length", 0) or 0)
            toegang = getattr(e, "obj_access", None)
            toegang_s = ""
            if isinstance(toegang, int):
                # bits: 0 ro(PreOP) 1 ro(SafeOP) 2 ro(OP) 3 rw(PreOP) 4 rw(SafeOP) 5 rw(OP) 6 RxPDO 7 TxPDO 8 backup 9 setting
                r = toegang & 0x07; w = (toegang >> 3) & 0x07
                toegang_s = ("r" if r else "-") + ("w" if w else "-") + (" B" if toegang & 0x100 else "") + (" S" if toegang & 0x200 else "")
            regel = {"index": f"0x{idx:04X}", "sub": sub, "naam": naam.strip(), "type": _DT.get(dt, (None, f"0x{dt:04X}"))[1],
                     "bits": bits, "toegang": toegang_s, "waarde": None, "fout": ""}
            if len(entries) > 1 and sub == 0 and not naam:
                regel["naam"] = "aantal subindexen"
            if met_waarden and (not toegang_s or "r" in toegang_s):
                try:
                    ruw = d.slave.sdo_read(idx, sub)
                    regel["waarde"], _ = _decodeer(ruw, dt, bits)
                    if isinstance(regel["waarde"], int) and regel["type"] not in ("SINT", "INT", "DINT", "LINT", "REAL", "LREAL"):
                        regel["hex"] = f"0x{regel['waarde']:X}"
                except Exception as ex:  # noqa: BLE001
                    regel["fout"] = str(ex)[:120]
            regels.append(regel)
        if len(regels) % 40 == 0:
            log(f"  … {len(regels)} regels (t/m 0x{idx:04X})")
    log(f"{len(regels)} objecten/subindexen gelezen.")
    return {"objecten": regels, "aantal": len(regels), **d.identiteit()}


# Reeksen die we aftasten: communicatieprofiel (selectief), vendor-gebied (volledig) en het CiA402-profiel.
_AFTAST_REEKSEN = ((0x1000, 0x1030), (0x1400, 0x1420), (0x1600, 0x1620), (0x1800, 0x1820), (0x1A00, 0x1A20),
                   (0x1C00, 0x1C40), (0x2000, 0x5FFF), (0x6000, 0x6FFF), (0xF000, 0xF020))


def _ruw_naar_waarde(ruw: bytes) -> tuple[str, str]:
    """Zonder typeinformatie: getal (little-endian) als de lengte 1/2/4/8 is, anders tekst of hex."""
    n = len(ruw)
    if n in (1, 2, 4, 8):
        u = int.from_bytes(ruw, "little")
        s = int.from_bytes(ruw, "little", signed=True)
        return (str(u) if s == u else f"{u} (signed {s})"), f"0x{u:0{n * 2}X}"
    tekst = ruw.split(b"\0")[0]
    if tekst and all(32 <= b < 127 for b in tekst):
        return tekst.decode("ascii"), ruw.hex()
    return ruw.hex(), ruw.hex()


def _werker_aftasten(d: _Drive, o: dict, log) -> dict:
    van = _int(o.get("van", 0)); tot = _int(o.get("tot", 0xFFFF))
    max_sub = int(o.get("max_sub") or 64)
    grondig = bool(o.get("grondig", False))   # ook sub 1 proberen als sub 0 niet bestaat (kost ~2× zo lang)
    regels = []
    gevonden = 0
    begin = time.time()
    mis_tijd: list[float] = []

    def lees_subs(idx: int, vanaf: int, tot_sub: int | None = None, doorgaan_na_fout: int = 0) -> int:
        """Sub 'vanaf' t/m 'tot_sub' (standaard max_sub) lezen; stopt na 'doorgaan_na_fout'+1 afwijzingen op rij.
        Geeft het aantal gevonden subindexen."""
        n = 0
        fouten_op_rij = 0
        for sub in range(vanaf, (tot_sub or max_sub) + 1):
            try:
                r2 = d.slave.sdo_read(idx, sub)
            except Exception as e:  # noqa: BLE001
                fouten_op_rij += 1
                # bij records met een teller registreren we het gat; anders gewoon stoppen
                if fouten_op_rij > doorgaan_na_fout:
                    break
                regels.append({"index": f"0x{idx:04X}", "sub": sub, "naam": "", "type": "", "bits": 0,
                               "toegang": "", "waarde": "", "hex": "", "fout": str(e)[:100]})
                continue
            fouten_op_rij = 0
            n += 1
            w, h = _ruw_naar_waarde(r2)
            regels.append({"index": f"0x{idx:04X}", "sub": sub, "naam": "", "type": f"{len(r2)} B", "bits": len(r2) * 8,
                           "toegang": "r", "waarde": w, "hex": h, "fout": ""})
        return n
    for lo, hi in _AFTAST_REEKSEN:
        lo, hi = max(lo, van), min(hi, tot)
        if lo > hi:
            continue
        log(f"Aftasten 0x{lo:04X}–0x{hi:04X} …")
        laatste = time.time()
        for idx in range(lo, hi + 1):
            # Even lucht tussen de aanvragen: pysoem hamert anders op de netwerkkaart en op
            # een pc waar de EtherCAT-poort ook de gewone netwerkpoort is, hapert dan de app.
            time.sleep(0.002)
            try:
                t0 = time.time()
                ruw = d.slave.sdo_read(idx, 0)
            except Exception:  # noqa: BLE001
                # geen object (abort) — verder. Antwoordt de drive niet met een abort maar met
                # stilte (time-out per index), dan wordt dit uren: dan stoppen met uitleg.
                mis_tijd.append(time.time() - t0)
                if len(mis_tijd) == 40 and sum(mis_tijd) / 40 > 0.25:
                    raise RuntimeError(f"De drive geeft geen snelle SDO-abort op onbekende objecten ({sum(mis_tijd) / 40 * 1000:.0f} ms per index); "
                                       "aftasten zou uren duren. Gebruik een smallere reeks (van/tot) of de objectlijst van ZeroErr.")
                if grondig and lees_subs(idx, 1) > 0:
                    gevonden += 1            # object zonder sub 0, wél data op sub 1+
                if time.time() - laatste > 5:
                    log(f"  … bij 0x{idx:04X}, {gevonden} objecten tot nu toe ({time.time() - begin:.0f} s)")
                    laatste = time.time()
                continue
            gevonden += 1
            w, h = _ruw_naar_waarde(ruw)
            regels.append({"index": f"0x{idx:04X}", "sub": 0, "naam": "", "type": f"{len(ruw)} B", "bits": len(ruw) * 8,
                           "toegang": "r", "waarde": w, "hex": h, "fout": ""})
            # Subindexen altijd proberen, ongeacht wat sub 0 is: bij een record/array (sub 0 = 1-byte
            # teller) lezen we tot de teller en registreren we gaten; anders tot de eerste afwijzing.
            if len(ruw) == 1 and 0 < ruw[0] <= max_sub:
                lees_subs(idx, 1, tot_sub=ruw[0], doorgaan_na_fout=ruw[0])   # record: alle subs t/m de teller, gaten melden
                lees_subs(idx, ruw[0] + 1)                                    # en kijken of er stiekem nog meer is
            else:
                lees_subs(idx, 1)
    log(f"Klaar: {gevonden} objecten, {len(regels)} regels in {time.time() - begin:.0f} s.")
    return {"objecten": regels, "aantal": len(regels), "methode": "aftasten", "grondig": grondig, **d.identiteit()}


def _werker_sdo(d: _Drive, o: dict, log) -> dict:
    """Eén SDO lezen of schrijven (testfunctie): index, sub, type, waarde (leeg = alleen lezen)."""
    index, sub, typ = _int(o["index"]), _int(o.get("sub", 0)), (o.get("type") or "UDINT").upper()
    uit = {"index": f"0x{index:04X}", "sub": sub, "type": typ}
    if o.get("waarde") not in (None, ""):
        if typ not in TYPEN:
            raise RuntimeError(f"Onbekend type {typ}.")
        waarde = _int(o["waarde"]) if typ != "REAL" else float(o["waarde"])
        d.schrijf(index, sub, typ, waarde)
        uit["geschreven"] = waarde
        log(f"0x{index:04X}:{sub} ← {waarde} ({typ})")
    ruw = d.slave.sdo_read(index, sub)
    if typ in TYPEN:
        fmt, n = TYPEN[typ]
        uit["gelezen"] = struct.unpack(fmt, ruw[:n])[0] if len(ruw) >= n else ruw.hex()
    else:
        uit["gelezen"] = ruw.split(b"\0")[0].decode("ascii", "replace") if typ == "STRING" else ruw.hex()
    uit["ruw"] = ruw.hex()
    log(f"0x{index:04X}:{sub} = {uit['gelezen']} (ruw {ruw.hex()})")
    if o.get("opslaan"):
        d.schrijf(0x1010, 1, "UDINT", SAVE)
        einde = time.time() + 10; stand = 0
        while time.time() < einde and stand != 2:
            time.sleep(0.5)
            try: stand = d.lees(0x1010, 1, "UDINT")
            except Exception: stand = 0  # noqa: BLE001
        uit["opgeslagen"] = stand == 2
        log("Opgeslagen." if stand == 2 else f"Opslaan niet bevestigd (stand {stand}).")
    return uit


def _werker_positie(d: _Drive, o: dict, log) -> dict:
    telling = int(o.get("telling_per_omw") or TELLING_PER_OMW)
    pos = d.lees(0x6064, 0, "DINT")
    uit = {"positie": pos, "hoek": round(pos / (telling / 360.0), 3), "statusword": f"0x{d.status():04X}",
           "foutcode": f"0x{d.lees(0x603F, 0, 'UINT'):04X}", **d.identiteit()}
    try:
        uit["temperatuur"] = d.lees(0x22A2, 0, "UINT")
    except Exception:  # noqa: BLE001
        pass
    try:
        uit["spanning"] = d.lees(0x6079, 0, "UDINT")
    except Exception:  # noqa: BLE001
        pass
    return uit


def werker(ifname: str, opdracht_json: str) -> dict:
    """`MdeAgent --werker coe <ifname> <json | @bestand>`; drukt één regel JSON af."""
    import pysoem
    if opdracht_json.startswith("@"):
        with open(opdracht_json[1:], encoding="utf-8") as f:
            o = json.load(f)
    else:
        o = json.loads(opdracht_json)
    # Logregels gaan meteen als JSON-regel naar stdout, zodat de agent (en het scherm) de
    # voortgang ziet terwijl het aftasten nog loopt; de laatste regel is het resultaat.
    regels: list[str] = []
    try:
        sys.stdout.reconfigure(encoding="utf-8")   # de agent leest ons als UTF-8; anders wordt '…' onleesbaar
    except Exception:  # noqa: BLE001
        pass
    def log(t):
        print(json.dumps({"log": str(t)}, ensure_ascii=False), flush=True)
    master = pysoem.Master()
    try:
        master.open(ifname)
    except Exception as e:  # noqa: BLE001
        return {"error": f"EtherCAT-adapter '{ifname}' openen mislukt: {e}", "log": regels}
    try:
        d = _Drive(master, int(o.get("positie") or 1))
        soort = o.get("wat")
        if soort == "parameters":
            uit = _werker_parameters(d, o, log)
        elif soort == "hoek":
            uit = _werker_hoek(d, o, log)
        elif soort == "positie":
            uit = _werker_positie(d, o, log)
        elif soort == "vrijgeven":
            d.uitschakelen(); uit = {"vrijgegeven": True}
        elif soort == "scan":
            uit = _werker_scan(d, o, log)
        elif soort == "sdo":
            uit = _werker_sdo(d, o, log)
        else:
            raise RuntimeError(f"Onbekende opdracht '{soort}'.")
        uit["log"] = regels
        return uit
    except Exception as e:  # noqa: BLE001
        return {"error": str(e), "log": regels}
    finally:
        master.close()


# ---------------------------------------------------------------------------
#  Vanuit de agent: werker starten en resultaat doorgeven
# ---------------------------------------------------------------------------

def _draai(inst: dict, job, opdracht: dict, timeout: int, werker_naam: str = "coe") -> dict:
    """Werker-proces starten. De opdracht gaat via een tijdelijk bestand (geen aanhalingstekens
    of lengtegrenzen op de Windows-commandoregel); het antwoord is de laatste regel JSON op stdout.
    `werker_naam` kiest het werker-programma (coe, of mde voor de eigen MDE-slaves)."""
    import os
    import tempfile
    ifname = _ifname(inst)
    fd, pad = tempfile.mkstemp(prefix=f"mde-{werker_naam}-", suffix=".json")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(opdracht, f)
    cmd = [sys.executable] + ([] if getattr(sys, "frozen", False) else ["-m", "mde_agent"]) \
          + ["--werker", werker_naam, ifname, "@" + pad]
    begin = time.time()
    job.log(f"Werker gestart ({opdracht.get('wat')}, drive {opdracht.get('positie')}) …")
    laatste = None
    try:
        # stderr mee in stdout: een aparte, niet-gelezen stderr-pijp kan vollopen en het kind blokkeren
        proces = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                  text=True, encoding="utf-8", errors="replace", bufsize=1,
                                  creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        klaar = threading.Event()
        stop_sinds: list[float] = []

        def doorgeven() -> None:
            """Invoer van de app (POST /jobs/<id>/invoer) naar de werker, en Stop: de mde-werker stopt dan zelf netjes
            (uitgangen uit, resultaat); andere werkers lezen geen stdin en worden meteen afgebroken (na 8 s ook mde)."""
            while not klaar.wait(0.2):
                try:
                    for tekst in (job.lees_invoer() if hasattr(job, "lees_invoer") else []):
                        proces.stdin.write(str(tekst).replace("\n", " ") + "\n")
                        proces.stdin.flush()
                    if getattr(job, "stop_gevraagd", False):
                        if not stop_sinds:
                            stop_sinds.append(time.time())
                            if werker_naam == "mde":
                                proces.stdin.write('{"stop": true}\n')
                                proces.stdin.flush()
                            else:
                                proces.kill()
                        elif time.time() - stop_sinds[0] > 8:
                            proces.kill()
                except Exception:  # noqa: BLE001
                    pass
        threading.Thread(target=doorgeven, daemon=True).start()
        try:
            assert proces.stdout is not None
            # regel voor regel meelezen: {"log": …} → direct in het joblog, de laatste regel is het resultaat
            for regel in proces.stdout:
                regel = regel.strip()
                if not regel:
                    continue
                try:
                    obj = json.loads(regel)
                except Exception:  # noqa: BLE001
                    job.log(regel)
                    continue
                if isinstance(obj, dict) and set(obj.keys()) == {"log"}:
                    job.log(obj["log"])
                elif isinstance(obj, dict) and set(obj.keys()) == {"live"} and isinstance(obj["live"], dict):
                    # tussenstand (mde: monitor-waarden): de app pollt job.resultaat zolang de job loopt
                    job.resultaat = {**obj["live"], "live": True}
                else:
                    laatste = obj
                if time.time() - begin > timeout:
                    proces.kill()
                    raise RuntimeError(f"De drive antwoordde niet binnen {timeout} s.")
            code = proces.wait(timeout=10)
            stderr = ""
            if stop_sinds and not isinstance(laatste, dict):
                raise RuntimeError("Gestopt door de gebruiker.")
        finally:
            klaar.set()
            if proces.poll() is None:
                proces.kill()
    finally:
        job.log(f"Werker klaar na {time.time() - begin:.1f} s.")
        try:
            os.remove(pad)
        except OSError:
            pass
    if not isinstance(laatste, dict):
        raise RuntimeError(f"Werker gaf geen resultaat terug (code {code}): " + (stderr or "(geen uitvoer)")[-800:])
    res = laatste
    for regel in res.pop("log", []) or []:
        job.log(regel)
    if res.get("error"):
        raise RuntimeError(res["error"])
    return res


def voer_uit(job, inst: dict, parameters: dict) -> None:
    """coe.parameters"""
    _hulp.ruim_werkmap_op()
    pad = _hulp.haal_firmware(parameters.get("parameters") or "", job, "parameters.json")
    with open(pad, encoding="utf-8-sig") as f:
        bestand = json.load(f)
    regels = bestand.get("registers") or []
    job.log(f"Parameterset '{bestand.get('omschrijving') or bestand.get('model') or ''}': {len(regels)} registers naar drive {parameters.get('positie', 1)}.")
    job.voortgang(10)
    res = _draai(inst, job, {"wat": "parameters", "positie": int(parameters.get("positie") or 1),
                             "registers": regels, "opslaan": bestand.get("opslaan", True)}, 120)
    job.voortgang(100)
    job.klaar(res)


class Hoek:
    NAAM = "coe.hoek"
    OMSCHRIJVING = "CoE-drive (eRob) naar een hoek bewegen"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        o = {"wat": "hoek", **{k: v for k, v in parameters.items() if v not in (None, "")}}
        o["positie"] = int(parameters.get("positie") or 1)
        job.log(f"Drive {o['positie']} naar {float(o.get('graden', 0)):.2f}° …")
        res = _draai(inst, job, o, int(float(parameters.get("timeout") or 60)) + 15)
        job.klaar(res)


class Positie:
    NAAM = "coe.positie"
    OMSCHRIJVING = "CoE-drive (eRob) positie/status lezen"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        job.klaar(_draai(inst, job, {"wat": "positie", "positie": int(parameters.get("positie") or 1)}, 30))


class Scan:
    NAAM = "coe.scan"
    OMSCHRIJVING = "CoE-drive: hele objectdictionary lezen (SDO Info)"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        o = {"wat": "scan", "positie": int(parameters.get("positie") or 1),
             "van": parameters.get("van") or 0, "tot": parameters.get("tot") or 0xFFFF,
             "waarden": parameters.get("waarden", True), "methode": parameters.get("methode") or "aftasten",
             "grondig": bool(parameters.get("grondig", False)), "max_sub": parameters.get("max_sub") or 64}
        job.log("Objectdictionary in kaart brengen: elk index proberen te lezen (0x1000…, 0x2000–0x5FFF, 0x6000–0x6FFF) …")
        job.klaar(_draai(inst, job, o, 900))


class Sdo:
    NAAM = "coe.sdo"
    OMSCHRIJVING = "CoE-drive: één SDO lezen/schrijven (test)"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        o = {"wat": "sdo", "positie": int(parameters.get("positie") or 1),
             **{k: v for k, v in parameters.items() if k in ("index", "sub", "type", "waarde", "opslaan")}}
        job.klaar(_draai(inst, job, o, 40))


class Vrijgeven:
    NAAM = "coe.vrijgeven"
    OMSCHRIJVING = "CoE-drive (eRob) uitschakelen"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        job.klaar(_draai(inst, job, {"wat": "vrijgeven", "positie": int(parameters.get("positie") or 1)}, 30))
