"""
MDE-profiel: de eigen EtherCAT-slaves van MDE (vendor 0x000010E3): M-Pede-WJ1 (Swivel), de 6-voudige
stappenmotorsturing, Display v2, Gripper, … Geen fabrikantbibliotheek: alles gaat met SDO's en FoE via
pysoem, in een apart werker-proces (zoals coe.py). De afspraak waar elke MDE-slave zich aan houdt:

    0x1008 / 0x1009 / 0x100A   naam, hardwareversie, firmwareversie (tekst)
    0x1018:1..4                vendor, productcode, revisie, serienummer
    0x7000 / 0x6000            control word / status word (betekenis van de bits: per print, in het recept)
    0x8000                     doorgeefluik naar seriële motoren (Feetech): 1 MotorId, 2 RegisterAddress,
                               3 Value, 4 Status  (AANNAME: eerst 1 en 2 schrijven, dan 3/4 lezen of 3 schrijven;
                               Status 0 = goed — nog te bevestigen met de firmware)
    FoE                        firmware laden (bootstrap-mailbox, BOOT-toestand)

Taak mde.identiteit     parameters: positie. Standaardobjecten lezen.
Taak mde.test           parameters: positie, stappen (lijst), naam. Een testrecept uitvoeren: elke stap is
                        een bouwsteen (zie _STAPPEN); de uitkomst is {"geslaagd", "samenvatting", "stappen": [...]}.
                        Bouwstenen:
                          identiteit   vendor, productcode, revisie?, firmware?     → vergelijken met de slave
                          lees         index, sub, type, min?, max?, gelijk?, naam? → waarde binnen de grenzen
                          schrijf      index, sub, type, waarde, naam?              → schrijven en teruglezen
                          wacht        seconden
                          beweging     doel_index, doel_sub, stand_index, stand_sub, type (INT), doel,
                                       tolerantie (5), timeout (5 s), controlword? {index, sub, aan, uit}, naam?
                          doorgeefluik motor, register, verwacht? (lezen) of waarde (schrijven), index (0x8000)
Taak mde.firmware       parameters: positie, firmware (URL/pad), versie, bestandsnaam?, wachtwoord? (0),
                        boot? (true). FoE: slave naar BOOT, bestand schrijven, terug naar INIT/PREOP, 0x100A lezen.
"""

import json
import struct
import sys
import time

from . import _hulp, coe
from .coe import TYPEN, _Drive, _draai, _int

MDE_VENDOR = 0x000010E3

NAAM = "mde.identiteit"
OMSCHRIJVING = "MDE-slave: identiteit en standaardobjecten lezen"


def beschikbaar(inst: dict):
    return coe.beschikbaar(inst)


# ---------------------------------------------------------------------------
#  Werker (apart proces): MdeAgent --werker mde <ifname> @opdracht.json
# ---------------------------------------------------------------------------

def _identiteit(d: _Drive) -> dict:
    s = d.slave
    uit = {"vendor": f"0x{s.man:08X}", "productcode": f"0x{s.id:08X}", "revisie": f"0x{s.rev:08X}",
           "eeprom_naam": (s.name.decode("utf-8", "replace") if isinstance(s.name, bytes) else str(s.name)).strip(),
           "naam": d.tekst(0x1008), "hardware": d.tekst(0x1009), "firmware": d.tekst(0x100A), "serienummer": "",
           "mde": s.man == MDE_VENDOR}
    try:
        sn = struct.unpack("<I", s.sdo_read(0x1018, 4)[:4])[0]
        uit["serienummer"] = str(sn) if sn else ""
    except Exception:  # noqa: BLE001
        pass
    for naam, idx in (("statusword", 0x6000), ("controlword", 0x7000)):
        try:
            uit[naam] = f"0x{d.lees(idx, 0, 'UINT'):04X}"
        except Exception:  # noqa: BLE001
            uit[naam] = ""
    return uit


def _hexgelijk(a, b) -> bool:
    try:
        return _int(a) == _int(b)
    except Exception:  # noqa: BLE001
        return str(a).strip().lower() == str(b).strip().lower()


def _stap_identiteit(d: _Drive, s: dict, log) -> tuple[bool, str]:
    ident = _identiteit(d)
    fouten = []
    for veld, sleutel in (("vendor", "vendor"), ("productcode", "productcode"), ("revisie", "revisie")):
        if s.get(sleutel) not in (None, "") and not _hexgelijk(ident[veld], s[sleutel]):
            fouten.append(f"{veld} {ident[veld]} (verwacht {s[sleutel]})")
    if s.get("firmware") and not coe_versie_gelijk(ident["firmware"], str(s["firmware"])):
        fouten.append(f"firmware {ident['firmware'] or '?'} (verwacht {s['firmware']})")
    tekst = f"{ident['naam'] or ident['eeprom_naam']} {ident['vendor']}/{ident['productcode']} rev {ident['revisie']}, " \
            f"hw {ident['hardware'] or '?'}, fw {ident['firmware'] or '?'}, sn {ident['serienummer'] or '-'}"
    log("  " + tekst)
    return (not fouten), (tekst if not fouten else "; ".join(fouten))


def coe_versie_gelijk(a: str, b: str) -> bool:
    def delen(s):
        d = [x for x in s.replace("-", ".").split(".") if x != ""]
        while d and d[-1].strip("0") == "":
            d.pop()
        return [x.lstrip("0") or "0" for x in d]
    return delen(a) == delen(b)


def _lees_waarde(d: _Drive, index: int, sub: int, typ: str):
    if typ in TYPEN:
        return d.lees(index, sub, typ)
    ruw = d.slave.sdo_read(index, sub)
    return ruw.split(b"\0")[0].decode("ascii", "replace") if typ == "STRING" else ruw.hex()


def _stap_lees(d: _Drive, s: dict, log) -> tuple[bool, str]:
    index, sub, typ = _int(s["index"]), _int(s.get("sub", 0)), (s.get("type") or "UINT").upper()
    w = _lees_waarde(d, index, sub, typ)
    naam = s.get("naam") or f"0x{index:04X}:{sub}"
    log(f"  {naam} = {w}")
    if s.get("gelijk") not in (None, "") and not _hexgelijk(w, s["gelijk"]):
        return False, f"{naam} = {w}, verwacht {s['gelijk']}"
    if isinstance(w, (int, float)):
        if s.get("min") not in (None, "") and w < float(s["min"]):
            return False, f"{naam} = {w}, onder minimum {s['min']}"
        if s.get("max") not in (None, "") and w > float(s["max"]):
            return False, f"{naam} = {w}, boven maximum {s['max']}"
    return True, f"{naam} = {w}"


def _stap_schrijf(d: _Drive, s: dict, log) -> tuple[bool, str]:
    index, sub, typ = _int(s["index"]), _int(s.get("sub", 0)), (s.get("type") or "UINT").upper()
    if typ not in TYPEN:
        return False, f"onbekend type {typ}"
    waarde = _int(s["waarde"]) if typ != "REAL" else float(s["waarde"])
    d.schrijf(index, sub, typ, waarde)
    terug = d.lees(index, sub, typ)
    naam = s.get("naam") or f"0x{index:04X}:{sub}"
    log(f"  {naam} ← {waarde}, teruggelezen {terug}")
    if s.get("teruglezen", True) and terug != waarde:
        return False, f"{naam}: geschreven {waarde}, teruggelezen {terug}"
    return True, f"{naam} = {waarde}"


def _stap_wacht(d: _Drive, s: dict, log) -> tuple[bool, str]:
    t = float(s.get("seconden") or 1)
    time.sleep(t)
    return True, f"{t:g} s gewacht"


def _stap_beweging(d: _Drive, s: dict, log) -> tuple[bool, str]:
    typ = (s.get("type") or "INT").upper()
    di, ds = _int(s["doel_index"]), _int(s.get("doel_sub", 0))
    si, ss = _int(s.get("stand_index") or s["doel_index"]), _int(s.get("stand_sub", s.get("doel_sub", 0)))
    doel = _int(s["doel"])
    tol = float(s.get("tolerantie") or 5)
    timeout = float(s.get("timeout") or 5)
    naam = s.get("naam") or f"0x{di:04X}:{ds}"
    cw = s.get("controlword")
    try:
        if cw:
            d.schrijf(_int(cw["index"]), _int(cw.get("sub", 0)), "UINT", _int(cw.get("aan", 1)))
        begin = _lees_waarde(d, si, ss, typ)
        log(f"  {naam}: van {begin} naar {doel} …")
        d.schrijf(di, ds, typ, doel)
        einde = time.time() + timeout
        stand = begin
        laatste = 0.0
        while time.time() < einde:
            stand = _lees_waarde(d, si, ss, typ)
            if abs(float(stand) - doel) <= tol:
                break
            if time.time() - laatste > 0.5:
                log(f"    … {stand}")
                laatste = time.time()
            time.sleep(0.05)
        else:
            return False, f"{naam}: doel {doel} niet bereikt binnen {timeout:g} s (stand {stand})"
        time.sleep(float(s.get("rust") or 0.2))
        stand = _lees_waarde(d, si, ss, typ)
        log(f"  {naam}: bereikt {stand}")
        return abs(float(stand) - doel) <= tol, f"{naam}: {begin} → {stand} (doel {doel})"
    finally:
        if cw and cw.get("uit") not in (None, ""):
            try:
                d.schrijf(_int(cw["index"]), _int(cw.get("sub", 0)), "UINT", _int(cw["uit"]))
            except Exception:  # noqa: BLE001
                pass


def _stap_doorgeefluik(d: _Drive, s: dict, log) -> tuple[bool, str]:
    """AANNAME over 0x8000 (zie moduledocstring); de Status-waarde komt altijd mee in de uitkomst."""
    index = _int(s.get("index") or 0x8000)
    motor, register = _int(s["motor"]), _int(s["register"])
    naam = s.get("naam") or f"motor {motor} reg {register}"
    d.schrijf(index, 1, "UINT", motor)
    d.schrijf(index, 2, "UINT", register)
    if s.get("waarde") not in (None, ""):
        d.schrijf(index, 3, "UINT", _int(s["waarde"]))
        time.sleep(float(s.get("wacht") or 0.1))
    else:
        time.sleep(float(s.get("wacht") or 0.1))
    waarde = d.lees(index, 3, "UINT")
    try:
        status = d.lees(index, 4, "UINT")
    except Exception:  # noqa: BLE001
        status = None
    log(f"  {naam}: waarde {waarde}, status {status}")
    if status not in (None, 0):
        return False, f"{naam}: status {status}"
    if s.get("verwacht") not in (None, "") and not _hexgelijk(waarde, s["verwacht"]):
        return False, f"{naam}: {waarde}, verwacht {s['verwacht']}"
    return True, f"{naam} = {waarde}"


_STAPPEN = {"identiteit": _stap_identiteit, "lees": _stap_lees, "schrijf": _stap_schrijf, "wacht": _stap_wacht,
            "beweging": _stap_beweging, "doorgeefluik": _stap_doorgeefluik}


def _werker_test(d: _Drive, o: dict, log) -> dict:
    stappen = o.get("stappen") or []
    uit = []
    geslaagd = True
    for nr, s in enumerate(stappen, start=1):
        soort = (s.get("stap") or "").lower()
        titel = s.get("naam") or soort
        fn = _STAPPEN.get(soort)
        log(f"Stap {nr}/{len(stappen)}: {titel}")
        if fn is None:
            ok, tekst = False, f"onbekende bouwsteen '{soort}'"
        else:
            try:
                ok, tekst = fn(d, s, log)
            except Exception as e:  # noqa: BLE001
                ok, tekst = False, f"fout: {e}"
        uit.append({"nr": nr, "stap": soort, "naam": titel, "geslaagd": ok, "tekst": tekst})
        log(("  ✓ " if ok else "  ✗ ") + tekst)
        if not ok:
            geslaagd = False
            if s.get("stoppen", True):
                log("  recept afgebroken bij deze stap.")
                break
    mis = [u for u in uit if not u["geslaagd"]]
    samenvatting = f"{len(uit)} van {len(stappen)} stappen gedaan" + (f", {len(mis)} mislukt: " + "; ".join(m["tekst"] for m in mis[:5]) if mis else ", alles goed")
    return {"geslaagd": geslaagd and len(uit) == len(stappen), "samenvatting": samenvatting, "stappen": uit, **_identiteit(d)}


def _werker_firmware(master, d: _Drive, o: dict, log) -> dict:
    import pysoem
    pad = o["bestand"]
    with open(pad, "rb") as f:
        data = f.read()
    bestandsnaam = o.get("bestandsnaam") or pad.replace("\\", "/").split("/")[-1]
    wachtwoord = _int(o.get("wachtwoord") or 0)
    s = d.slave
    ident_voor = _identiteit(d)
    log(f"Slave {ident_voor['naam'] or ident_voor['eeprom_naam']} meldt nu firmware {ident_voor['firmware'] or '?'}.")

    if o.get("boot", True):
        # INIT → BOOT: de bootstrap-mailbox komt uit de EEPROM (ESI: <BootStrap>)
        s.state = pysoem.INIT_STATE
        s.write_state()
        s.state_check(pysoem.INIT_STATE, 2_000_000)
        s.state = pysoem.BOOT_STATE
        s.write_state()
        if s.state_check(pysoem.BOOT_STATE, 5_000_000) != pysoem.BOOT_STATE:
            raise RuntimeError(f"Slave komt niet in BOOT (toestand 0x{s.state:02X}, AL-status 0x{s.al_status:04X}). Ondersteunt de bootloader FoE?")
        log("Slave in BOOT.")
    log(f"FoE: {bestandsnaam} ({len(data):,} bytes) schrijven …".replace(",", "."))
    begin = time.time()
    s.foe_write(bestandsnaam, wachtwoord, data)
    log(f"Overdracht klaar in {time.time() - begin:.1f} s.")

    # terug naar INIT; de slave start opnieuw met de nieuwe firmware
    try:
        s.state = pysoem.INIT_STATE
        s.write_state()
    except Exception:  # noqa: BLE001
        pass
    wacht = float(o.get("wachten") or 20)
    log(f"Wachten tot de slave terug is (max. {wacht:g} s) …")
    einde = time.time() + wacht
    gemeld = ""
    while time.time() < einde:
        time.sleep(2)
        try:
            n = master.config_init()
            if n >= d.positie:
                d2 = _Drive(master, d.positie)
                gemeld = d2.tekst(0x100A)
                if gemeld:
                    break
        except Exception:  # noqa: BLE001
            pass
    if not gemeld:
        raise RuntimeError("De slave kwam niet terug op de bus na het laden; controleer voeding en kabel. "
                           "De firmware is mogelijk wel geladen.")
    log(f"Slave is terug en meldt firmware {gemeld}.")
    verwacht = (o.get("versie") or "").strip()
    if verwacht and not coe_versie_gelijk(gemeld, verwacht):
        raise RuntimeError(f"Slave meldt firmware {gemeld}, verwacht {verwacht}. Laden lijkt niet gelukt.")
    return {"versie": gemeld, "positie": d.positie, "was": ident_voor["firmware"]}


def werker(ifname: str, opdracht_json: str) -> dict:
    """`MdeAgent --werker mde <ifname> <json | @bestand>`; drukt één regel JSON af."""
    import pysoem
    if opdracht_json.startswith("@"):
        with open(opdracht_json[1:], encoding="utf-8") as f:
            o = json.load(f)
    else:
        o = json.loads(opdracht_json)
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:  # noqa: BLE001
        pass

    def log(t):
        print(json.dumps({"log": str(t)}, ensure_ascii=False), flush=True)

    master = pysoem.Master()
    try:
        master.open(ifname)
    except Exception as e:  # noqa: BLE001
        return {"error": f"EtherCAT-adapter '{ifname}' openen mislukt: {e}"}
    try:
        positie = int(o.get("positie") or 1)
        d = _Drive(master, positie)
        d.positie = positie
        if o.get("eis_mde", True) and d.slave.man != MDE_VENDOR and o.get("wat") != "identiteit":
            raise RuntimeError(f"Slave op positie {positie} is geen MDE-slave (vendor 0x{d.slave.man:08X}).")
        soort = o.get("wat")
        if soort == "identiteit":
            uit = _identiteit(d)
        elif soort == "test":
            uit = _werker_test(d, o, log)
        elif soort == "firmware":
            uit = _werker_firmware(master, d, o, log)
        else:
            raise RuntimeError(f"Onbekende opdracht '{soort}'.")
        return uit
    except Exception as e:  # noqa: BLE001
        return {"error": str(e)}
    finally:
        master.close()


# ---------------------------------------------------------------------------
#  Taken (vanuit de agent)
# ---------------------------------------------------------------------------

def voer_uit(job, inst: dict, parameters: dict) -> None:
    """mde.identiteit"""
    job.klaar(_draai(inst, job, {"wat": "identiteit", "positie": int(parameters.get("positie") or 1), "eis_mde": False},
                     40, werker_naam="mde"))


class Test:
    NAAM = "mde.test"
    OMSCHRIJVING = "MDE-slave: testrecept (bouwstenen) uitvoeren"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        stappen = parameters.get("stappen") or []
        if isinstance(stappen, str):
            stappen = json.loads(stappen)
        if not stappen:
            raise RuntimeError("Het recept heeft geen stappen.")
        o = {"wat": "test", "positie": int(parameters.get("positie") or 1), "stappen": stappen,
             "eis_mde": parameters.get("eis_mde", True)}
        job.log(f"Recept '{parameters.get('naam') or ''}': {len(stappen)} stappen op slave {o['positie']}.")
        job.voortgang(5)
        timeout = sum(float(s.get("timeout") or 5) + float(s.get("seconden") or 0) for s in stappen) + 30
        res = _draai(inst, job, o, int(timeout), werker_naam="mde")
        job.log(("GESLAAGD: " if res.get("geslaagd") else "MISLUKT: ") + res.get("samenvatting", ""))
        job.klaar(res)


class Firmware:
    NAAM = "mde.firmware"
    OMSCHRIJVING = "MDE-slave: firmware laden via EtherCAT (FoE)"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        _hulp.ruim_werkmap_op()
        bestand = _hulp.haal_firmware(parameters.get("firmware") or "", job, "firmware.bin")
        o = {"wat": "firmware", "positie": int(parameters.get("positie") or 1), "bestand": bestand,
             "versie": parameters.get("versie") or "", "bestandsnaam": parameters.get("bestandsnaam") or "",
             "wachtwoord": parameters.get("wachtwoord") or 0, "boot": parameters.get("boot", True),
             "wachten": parameters.get("wachten") or 20}
        job.log(f"Firmware {o['versie']} via FoE naar slave {o['positie']} …")
        job.voortgang(10)
        res = _draai(inst, job, o, 300, werker_naam="mde")
        job.voortgang(100)
        job.klaar(res)
