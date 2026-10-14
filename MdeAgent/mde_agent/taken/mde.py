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
                          pauze        = wacht
                          wacht_tot    index, sub, type, gelijk? / min? / max?, timeout (10 s): wachten tot de
                                       waarde aan de voorwaarde voldoet (bv. druk onder 50 binnen 5 s)
                          herhaal      keer (n), stappen [ … ]: het blok n keer uitvoeren; stopt bij de eerste
                                       mislukte ronde (tenzij "stoppen": false op het blok)
                          monitor      objecten [{index, sub, type, naam}] — vanaf deze stap leest de werker die
                                       objecten elke 0,2 s (tijdens wacht/beweging en na elke stap) en stuurt ze
                                       live naar de app ({"live": {"t": s, "waarden": {naam: waarde}}})
Taak mde.monitor        parameters: positie, objecten (zoals bij de bouwsteen monitor), seconden (120), interval (0.2).
                        Alleen lezen, live naar de app; Stop = eerder klaar.
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


# ---- monitor: objecten live meelezen tijdens het recept (of los, taak mde.monitor) ----

def _monitor_meet(d: _Drive, altijd: bool = False) -> dict | None:
    """De monitor-objecten lezen en als live-tussenstand naar de app sturen (hoogstens elke 0,2 s,
    tenzij `altijd`). Geeft de gelezen waarden terug, of None als er geen monitor ingesteld is."""
    objs = getattr(d, "monitor", None)
    live = getattr(d, "live", None)
    if not objs or live is None:
        return None
    nu = time.time()
    if not altijd and nu - getattr(d, "laatste_meting", 0.0) < 0.2:
        return None
    d.laatste_meting = nu
    waarden = {}
    for o in objs:
        try:
            index, sub = _int(o["index"]), _int(o.get("sub", 0))
        except Exception:  # noqa: BLE001
            continue
        naam = o.get("naam") or f"0x{index:04X}:{sub}"
        try:
            waarden[naam] = _lees_waarde(d, index, sub, (o.get("type") or "UINT").upper())
        except Exception:  # noqa: BLE001
            waarden[naam] = None
    live({"t": round(nu - getattr(d, "t0", nu), 2), "waarden": waarden})
    return waarden


def _slaap(d: _Drive, seconden: float) -> None:
    """Wachten in stukjes, zodat de monitor intussen blijft meten."""
    einde = time.time() + seconden
    while True:
        rest = einde - time.time()
        if rest <= 0:
            break
        time.sleep(min(0.2, rest))
        _monitor_meet(d)


def _stap_monitor(d: _Drive, s: dict, log) -> tuple[bool, str]:
    objs = s.get("objecten") or []
    if not objs:
        return False, "geen objecten opgegeven"
    d.monitor = objs
    w = _monitor_meet(d, altijd=True) or {}
    namen = ", ".join(w.keys())
    log(f"  meekijken: {namen}")
    return True, f"meekijken met {len(objs)} objecten: {namen}"


def _stap_wacht(d: _Drive, s: dict, log) -> tuple[bool, str]:
    t = float(s.get("seconden") or 1)
    _slaap(d, t)
    return True, f"{t:g} s gewacht"


def _voldoet(w, s: dict) -> bool:
    if s.get("gelijk") not in (None, "") and not _hexgelijk(w, s["gelijk"]):
        return False
    if isinstance(w, (int, float)):
        if s.get("min") not in (None, "") and w < float(s["min"]):
            return False
        if s.get("max") not in (None, "") and w > float(s["max"]):
            return False
    return True


def _stap_wacht_tot(d: _Drive, s: dict, log) -> tuple[bool, str]:
    index, sub, typ = _int(s["index"]), _int(s.get("sub", 0)), (s.get("type") or "UINT").upper()
    timeout = float(s.get("timeout") or 10)
    naam = s.get("naam") or f"0x{index:04X}:{sub}"
    eis = ", ".join(f"{k} {s[k]}" for k in ("gelijk", "min", "max") if s.get(k) not in (None, ""))
    begin = time.time()
    w = None
    while time.time() - begin < timeout:
        w = _lees_waarde(d, index, sub, typ)
        if _voldoet(w, s):
            log(f"  {naam} = {w} na {time.time() - begin:.1f} s")
            return True, f"{naam} = {w} na {time.time() - begin:.1f} s ({eis})"
        _monitor_meet(d)
        time.sleep(0.05)
    return False, f"{naam} = {w} na {timeout:g} s, niet {eis}"


def _stap_herhaal(d: _Drive, s: dict, log) -> tuple[bool, str]:
    keer = max(1, int(s.get("keer") or 1))
    binnen = s.get("stappen") or []
    if not binnen:
        return False, "geen stappen in het blok"
    mislukt = 0
    for ronde in range(1, keer + 1):
        log(f"  ronde {ronde}/{keer}")
        uit = []
        ok = _voer_stappen(d, binnen, log, uit, f"{ronde}.")
        if not ok:
            mislukt += 1
            if s.get("stoppen", True):
                return False, f"ronde {ronde} van {keer} mislukt: " + "; ".join(u["tekst"] for u in uit if not u["geslaagd"])
    return mislukt == 0, f"{keer}× uitgevoerd" + (f", {mislukt} ronde(s) mislukt" if mislukt else "")


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
            _monitor_meet(d)
            time.sleep(0.05)
        else:
            return False, f"{naam}: doel {doel} niet bereikt binnen {timeout:g} s (stand {stand})"
        _slaap(d, float(s.get("rust") or 0.2))
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
            "pauze": _stap_wacht, "wacht_tot": _stap_wacht_tot, "herhaal": _stap_herhaal,
            "beweging": _stap_beweging, "doorgeefluik": _stap_doorgeefluik, "monitor": _stap_monitor}


def _voer_stappen(d: _Drive, stappen: list, log, uit: list, prefix: str = "") -> bool:
    """De stappen na elkaar; `uit` krijgt per stap de uitkomst. Geeft False zodra een stap mislukt en het recept
    daar moet stoppen (standaard), of als er een stap mislukt is met "stoppen": false."""
    geslaagd = True
    for nr, s in enumerate(stappen, start=1):
        soort = (s.get("stap") or "").lower()
        titel = s.get("naam") or soort
        fn = _STAPPEN.get(soort)
        log(f"Stap {prefix}{nr}/{len(stappen)}: {titel}")
        if fn is None:
            ok, tekst = False, f"onbekende bouwsteen '{soort}'"
        else:
            try:
                ok, tekst = fn(d, s, log)
            except Exception as e:  # noqa: BLE001
                ok, tekst = False, f"fout: {e}"
        _monitor_meet(d, altijd=True)
        uit.append({"nr": f"{prefix}{nr}", "stap": soort, "naam": titel, "geslaagd": ok, "tekst": tekst})
        log(("  ✓ " if ok else "  ✗ ") + tekst)
        if not ok:
            geslaagd = False
            if s.get("stoppen", True):
                log("  afgebroken bij deze stap.")
                return False
    return geslaagd


def _werker_test(d: _Drive, o: dict, log) -> dict:
    stappen = o.get("stappen") or []
    uit = []
    geslaagd = _voer_stappen(d, stappen, log, uit)
    mis = [u for u in uit if not u["geslaagd"]]
    samenvatting = f"{len(uit)} stappen gedaan" + (f", {len(mis)} mislukt: " + "; ".join(m["tekst"] for m in mis[:5]) if mis else ", alles goed")
    return {"geslaagd": geslaagd, "samenvatting": samenvatting, "stappen": uit, **_identiteit(d)}


def _werker_monitor(d: _Drive, o: dict, log) -> dict:
    """Alleen lezen: de objecten `seconden` lang elke `interval` s meten en live doorgeven."""
    d.monitor = o.get("objecten") or []
    if not d.monitor:
        raise RuntimeError("Geen objecten om mee te kijken.")
    seconden = max(1.0, min(1800.0, float(o.get("seconden") or 120)))
    interval = max(0.05, float(o.get("interval") or 0.2))
    log(f"Meekijken met {len(d.monitor)} objecten, {seconden:g} s (Stop = eerder klaar) …")
    einde = time.time() + seconden
    n = 0
    laatste = {}
    while time.time() < einde:
        w = _monitor_meet(d, altijd=True)
        if w is not None:
            laatste = w
            n += 1
        time.sleep(interval)
    log(f"Klaar: {n} metingen.")
    return {"metingen": n, "waarden": laatste, **_identiteit(d)}


_TOESTANDEN = {0x01: "INIT", 0x02: "PRE-OP", 0x03: "BOOT", 0x04: "SAFE-OP", 0x08: "OP"}
# AL-statuscodes (ETG.1000.6) die bij een geweigerde toestandsovergang voorkomen
_AL_STATUS = {0x0000: "geen fout", 0x0011: "ongeldige toestandsovergang", 0x0012: "onbekende gevraagde toestand",
              0x0013: "bootstrap niet ondersteund", 0x0014: "geen geldige firmware", 0x0015: "ongeldige mailboxconfiguratie",
              0x0016: "ongeldige mailboxconfiguratie (PRE-OP)", 0x0017: "ongeldige sync-managerconfiguratie",
              0x001A: "synchronisatiefout", 0x001B: "sync-manager-watchdog", 0x0024: "ongeldige input-configuratie",
              0x0025: "ongeldige output-configuratie", 0x002D: "geen geldige inputs en outputs"}


def _toestand(code: int) -> str:
    basis = code & 0x0F
    return f"{_TOESTANDEN.get(basis, f'0x{basis:02X}')}{' + foutvlag' if code & 0x10 else ''}"


def _al_status(code: int) -> str:
    return f"0x{code:04X} {_AL_STATUS.get(code, '')}".strip()


def _bootstrap_mailbox(s, log) -> None:
    """De bootstrap-mailbox (SII BOOT_RX_MBX / BOOT_TX_MBX: adres in het lage woord, grootte in het hoge) in
    SM0/SM1 van de ESC programmeren en de master dezelfde adressen laten gebruiken (pysoem amend_mbx; alleen
    in INIT). Mislukt het, dan gaan we door met de standaardmailbox — bij de M-Pede-WJ1 zijn ze gelijk."""
    import pysoem
    try:
        rx = s.eeprom_read(int(pysoem.SiiOffset.BOOT_RX_MBX))
        tx = s.eeprom_read(int(pysoem.SiiOffset.BOOT_TX_MBX))
        rx_adr, rx_len = struct.unpack("<HH", rx[:4])
        tx_adr, tx_len = struct.unpack("<HH", tx[:4])
    except Exception as e:  # noqa: BLE001
        log(f"Bootstrap-mailbox uit de EEPROM lezen mislukt ({e}); standaardmailbox blijft.")
        return
    if not (0 < rx_len < 0x1000 and 0 < tx_len < 0x1000 and rx_adr and tx_adr):
        log(f"Geen bruikbare bootstrap-mailbox in de EEPROM (uit 0x{rx_adr:04X}/{rx_len}, in 0x{tx_adr:04X}/{tx_len}); standaardmailbox blijft.")
        return
    try:
        s.amend_mbx("out", rx_adr, rx_len)
        s.amend_mbx("in", tx_adr, tx_len)
        log(f"Bootstrap-mailbox in SM0/SM1 gezet: uit 0x{rx_adr:04X} ({rx_len} B), in 0x{tx_adr:04X} ({tx_len} B).")
    except Exception as e:  # noqa: BLE001
        log(f"SM0/SM1 op de bootstrap-mailbox zetten mislukt ({e}); standaardmailbox blijft.")


def _vraag_toestand(s, log, toestand: int, wacht_s: float) -> int:
    """Een toestand vragen en laten zien wat er gebeurt: de schrijfactie (wkc), AL control meteen teruggelezen
    (komt onze vraag aan?), en tijdens het wachten elke verandering van AL control/status/code. Komt de
    schrijfactie niet aan (wkc 0), dan nog eens rechtstreeks met FPWR, die een duidelijke fout geeft."""
    import pysoem
    s.state = toestand
    wkc = s.write_state()
    time.sleep(0.05)
    ctl = _al_control(s)
    log(f"AL control ← {_toestand(toestand)} (0x{toestand:04X}): wkc {wkc}, teruggelezen "
        f"{'0x%04X' % ctl if ctl is not None else '?'}.")
    if ctl is not None and (ctl & 0x0F) != toestand:
        log("  De vraag staat niet in het register; nog eens rechtstreeks schrijven (FPWR 0x0120) …")
        try:
            s._fpwr(0x0120, struct.pack("<H", toestand))
            ctl = _al_control(s)
            log(f"  teruggelezen {'0x%04X' % ctl if ctl is not None else '?'}.")
        except Exception as e:  # noqa: BLE001
            log(f"  FPWR naar 0x0120 mislukt: {e}")
    einde = time.time() + wacht_s
    vorige = None
    bereikt = None
    while True:
        try:
            ctl = struct.unpack("<H", s._fprd(0x0120, 2)[:2])[0]
            st = struct.unpack("<H", s._fprd(0x0130, 2)[:2])[0]
            code = struct.unpack("<H", s._fprd(0x0134, 2)[:2])[0]
            nu = (ctl, st, code)
            if nu != vorige:
                log(f"  t+{wacht_s - (einde - time.time()):.1f}s: AL control 0x{ctl:04X}, AL status 0x{st:04X} ({_toestand(st)}), code {_al_status(code)}")
                vorige = nu
            bereikt = st & 0x0F
            if bereikt == toestand:
                break
        except Exception as e:  # noqa: BLE001
            log(f"  registers lezen mislukt: {e}")
        if time.time() >= einde:
            break
        time.sleep(0.25)
    if bereikt is None:
        bereikt = s.state_check(toestand, 200_000)
    else:
        s.state_check(toestand, 100_000)     # SOEM's eigen beeld van de slave bijwerken (ec_slave[].state)
    return bereikt


def _al_control(s) -> int | None:
    try:
        return struct.unpack("<H", s._fprd(0x0120, 2)[:2])[0]
    except Exception:  # noqa: BLE001
        return None


def _esc_registers(s) -> str:
    """AL control (0x0120), AL status (0x0130) en AL-statuscode (0x0134) rechtstreeks uit de ESC, voor de diagnose."""
    try:
        ctl = struct.unpack("<H", s._fprd(0x0120, 2)[:2])[0]
        st = struct.unpack("<H", s._fprd(0x0130, 2)[:2])[0]
        code = struct.unpack("<H", s._fprd(0x0134, 2)[:2])[0]
        return f"; ESC: AL control 0x{ctl:04X}, AL status 0x{st:04X}, code {_al_status(code)}"
    except Exception:  # noqa: BLE001
        return ""


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

    in_boot = False
    if o.get("boot", True):
        # INIT → BOOT, zoals SOEM's firm_update-voorbeeld en TwinCAT het doen: in INIT eerst de bootstrap-
        # mailbox uit de EEPROM (SII woord 0x14/0x16; ESI <BootStrap>) in SM0/SM1 zetten, dan pas BOOT vragen.
        # Zonder die stap weigert een slave-stack de overgang stil (blijft INIT, AL-status 0): dat zagen we bij
        # de Swivel, terwijl TwinCAT hem wél in BOOT kreeg. Lukt BOOT dan nog niet, dan FoE in PRE-OP proberen.
        _vraag_toestand(s, log, pysoem.INIT_STATE, 2.0)
        _bootstrap_mailbox(s, log)
        bereikt = _vraag_toestand(s, log, pysoem.BOOT_STATE, 10.0)
        if bereikt == pysoem.BOOT_STATE:
            in_boot = True
            log("Slave in BOOT.")
        else:
            # Staat er in AL control (0x0120) iets anders dan onze BOOT-vraag terwijl die wél was aangekomen, dan
            # schrijft een ándere master dat register: doorgaan heeft dan geen zin, die trekt de slave steeds terug.
            ctl = _al_control(s)
            if ctl is not None and (ctl & 0x0F) != pysoem.BOOT_STATE:
                raise RuntimeError(f"AL control staat op 0x{ctl:04X} ({_toestand(ctl)}) terwijl wij BOOT (0x0003) schreven: een andere "
                                   "EtherCAT-master schrijft dit register, of onze schrijfactie komt niet aan (zie de regels hierboven: "
                                   "'teruggelezen'). Koppel andere masters los en probeer opnieuw.")
            log(f"Slave gaat niet naar BOOT (toestand {_toestand(bereikt)}, AL-status {_al_status(s.al_status)}"
                f"{_esc_registers(s)}); FoE via de gewone mailbox in PRE-OP proberen …")
            _vraag_toestand(s, log, pysoem.INIT_STATE, 2.0)
    if not in_boot:
        s.state = pysoem.PREOP_STATE
        s.write_state()
        if s.state_check(pysoem.PREOP_STATE, 5_000_000) != pysoem.PREOP_STATE:
            raise RuntimeError(f"Slave komt niet in PRE-OP (toestand {_toestand(s.state)}, AL-status {_al_status(s.al_status)}).")
    log(f"FoE: {bestandsnaam} ({len(data):,} bytes) schrijven …".replace(",", "."))
    begin = time.time()
    try:
        s.foe_write(bestandsnaam, wachtwoord, data)
    except Exception as e:  # noqa: BLE001
        if in_boot:
            raise RuntimeError(f"FoE-overdracht mislukt in BOOT: {e}. Klopt de bestandsnaam of het wachtwoord dat de bootloader verwacht?") from e
        raise RuntimeError(f"FoE-overdracht mislukt ({e}). De slave gaat niet naar BOOT en neemt in PRE-OP geen FoE aan: "
                           "de firmware van de print moet de bootstrap-toestand en FoE ondersteunen (slave-stack: "
                           "BOOTSTRAPMODE_SUPPORTED en FOE_SUPPORTED), of FoE in PRE-OP toestaan.") from e
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
    terug = False
    vendor, product = s.man, s.id
    while time.time() < einde:
        time.sleep(2)
        try:
            n = master.config_init()
            if n >= d.positie:
                d2 = _Drive(master, d.positie)
                # Terug = dezelfde slave op dezelfde positie. Niet eisen dat 0x100A gevuld is: niet elke
                # firmware vult die (de eerste Swivel-firmware niet), en dan stond de slave er gewoon.
                if d2.slave.man == vendor and d2.slave.id == product:
                    terug = True
                    gemeld = d2.tekst(0x100A)
                    break
        except Exception:  # noqa: BLE001
            pass
    if not terug:
        raise RuntimeError("De slave kwam niet terug op de bus na het laden; controleer voeding en kabel. "
                           "De firmware is mogelijk wel geladen.")
    log(f"Slave is terug en meldt firmware {gemeld or '? (0x100A leeg)'}.")
    verwacht = (o.get("versie") or "").strip()
    if verwacht and gemeld and not coe_versie_gelijk(gemeld, verwacht):
        raise RuntimeError(f"Slave meldt firmware {gemeld}, verwacht {verwacht}. Laden lijkt niet gelukt.")
    if verwacht and not gemeld:
        log(f"Versie niet te controleren: de slave vult 0x100A niet (verwacht {verwacht}).")
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

    def live(w):
        print(json.dumps({"live": w}, ensure_ascii=False, default=str), flush=True)

    master = pysoem.Master()
    try:
        master.open(ifname)
    except Exception as e:  # noqa: BLE001
        return {"error": f"EtherCAT-adapter '{ifname}' openen mislukt: {e}"}
    try:
        positie = int(o.get("positie") or 1)
        d = _Drive(master, positie)
        d.positie = positie
        d.live = live
        d.t0 = time.time()
        d.monitor = None
        if o.get("eis_mde", True) and d.slave.man != MDE_VENDOR and o.get("wat") != "identiteit":
            raise RuntimeError(f"Slave op positie {positie} is geen MDE-slave (vendor 0x{d.slave.man:08X}).")
        soort = o.get("wat")
        if soort == "identiteit":
            uit = _identiteit(d)
        elif soort == "test":
            uit = _werker_test(d, o, log)
        elif soort == "monitor":
            uit = _werker_monitor(d, o, log)
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


class Monitor:
    NAAM = "mde.monitor"
    OMSCHRIJVING = "MDE-slave: objecten live meekijken (alleen lezen)"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        objecten = parameters.get("objecten") or []
        if isinstance(objecten, str):
            objecten = json.loads(objecten)
        seconden = max(1, min(1800, int(float(parameters.get("seconden") or 120))))
        o = {"wat": "monitor", "positie": int(parameters.get("positie") or 1), "objecten": objecten,
             "seconden": seconden, "interval": parameters.get("interval") or 0.2, "eis_mde": parameters.get("eis_mde", True)}
        try:
            res = _draai(inst, job, o, seconden + 30, werker_naam="mde")
        except RuntimeError as e:
            if "Gestopt" not in str(e):
                raise
            # Stop is bij meekijken gewoon 'klaar': de laatste tussenstand is het resultaat.
            res = {**(job.resultaat if isinstance(job.resultaat, dict) else {}), "gestopt": True}
            res.pop("live", None)
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
