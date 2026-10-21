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
                        Het recept loopt in OP met een procesdata-cyclus (2 ms): lees/schrijf op een object dat in
                        de PDO-toewijzing staat (0x1C12/0x1C13) gaat via de in-/uitgangsdata, de rest via SDO. In
                        PRE-OP zet een SDO op bv. 0x7020 (kleppen Swivel) niets in werking. Komt de slave niet in OP,
                        of "procesdata": false, dan alles via SDO in PRE-OP. Na afloop uitgangen uit, terug naar PRE-OP.
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
                          monitor      objecten [{index, sub, type, naam, min?, max?}] — vanaf deze stap leest de
                                       werker die objecten elke 0,2 s (tijdens wacht/beweging en na elke stap) en stuurt
                                       ze live naar de app ({"live": {"t": s, "waarden": {naam: waarde}}}); min/max
                                       tekent de app als grenslijn in de grafiek
                          bediening    knoppen [{naam, index, sub, type, waarde}], schakelaars [{naam, index, sub,
                                       type?, aan? (1), uit? (0)}], invoer [{naam, index, sub, type, min?, max?}],
                                       seconden (3600): de werker wacht op opdrachten van de app (HMI) en schrijft
                                       alleen wat in deze stap staat; Klaar = verder met de volgende stap
                        Opdrachten via stdin (POST /jobs/<id>/invoer, één JSON per regel): {"knop": n},
                        {"schakelaar": n, "aan": true}, {"invoer": n, "waarde": w}, {"klaar": true}, {"stop": true}.
                        Stop = netjes stoppen (uitgangen uit), ook buiten een bediening-stap.
Taak mde.monitor        parameters: positie, objecten (zoals bij de bouwsteen monitor), seconden (120), interval (0.2).
                        Alleen lezen, live naar de app; Stop = eerder klaar.
Taak mde.serienummer    parameters: positie, serienummer (getal), overschrijven? (false). Het serienummer in de
                        EEPROM (SII) van de ESC zetten, woord 0x0E/0x0F: de vaste plek uit ETG.1000.6, buiten
                        de checksum en onafhankelijk van de firmware. Staat er al een ánder nummer (niet 0),
                        dan alleen met overschrijven. Leest daarna SII en 0x1018:4 terug.
Taak mde.firmware       parameters: positie, firmware (URL/pad), versie, bestandsnaam?, wachtwoord? (0),
                        boot? (true). FoE: slave naar BOOT, bestand schrijven, terug naar INIT/PREOP, 0x100A lezen.
"""

import json
import queue
import struct
import sys
import threading
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
           "naam": d.tekst(0x1008), "hardware": kale_versie(d.tekst(0x1009)), "firmware": kale_versie(d.tekst(0x100A)), "serienummer": "",
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


# ---- opdrachten van de app via stdin (test en monitor): Stop, en de bediening-stap ----
_INVOER: "queue.Queue[dict]" = queue.Queue()
_STOP = threading.Event()


class _Gestopt(Exception):
    """Stop van de gebruiker: het recept houdt op, de werker ruimt netjes op (uitgangen uit)."""


def _lees_stdin() -> None:
    for regel in sys.stdin:
        try:
            c = json.loads(regel)
        except Exception:  # noqa: BLE001
            continue
        if isinstance(c, dict):
            if c.get("stop"):
                _STOP.set()
            else:
                _INVOER.put(c)


def _controleer_stop() -> None:
    if _STOP.is_set():
        raise _Gestopt()


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


def kale_versie(s: str) -> str:
    """Versie zonder 'v' ervoor: de Swivel-firmware meldt "v1.0.5" in 0x100A, wij schrijven overal "1.0.5"."""
    s = (s or "").strip()
    return s[1:] if len(s) > 1 and s[0] in "vV" and s[1].isdigit() else s


def coe_versie_gelijk(a: str, b: str) -> bool:
    def delen(s):
        d = [x for x in kale_versie(s).replace("-", ".").split(".") if x != ""]
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
    objs = getattr(d, "monitor", None) or []
    geschreven = getattr(d, "geschreven", None)
    live = getattr(d, "live", None)
    if (not objs and not geschreven) or live is None:
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
    uit = {"t": round(nu - getattr(d, "t0", nu), 2), "waarden": waarden}
    if geschreven:
        uit["geschreven"] = geschreven          # laatst geschreven waarde per bedienelement (schakelaars, invoer)
    live(uit)
    return waarden


def _slaap(d: _Drive, seconden: float) -> None:
    """Wachten in stukjes, zodat de monitor intussen blijft meten."""
    einde = time.time() + seconden
    while True:
        rest = einde - time.time()
        if rest <= 0:
            break
        _controleer_stop()
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
        _controleer_stop()
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
            _controleer_stop()
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


def _getal(waarde, typ: str):
    if typ == "REAL":
        return float(waarde)
    if isinstance(waarde, str) and waarde.strip().lower().startswith("0x"):
        return int(waarde.strip(), 16)
    return int(round(float(waarde)))


def _stap_bediening(d: _Drive, s: dict, log) -> tuple[bool, str]:
    """HMI: wachten op opdrachten van de app en alleen de elementen uit deze stap schrijven (met hun grenzen)."""
    knoppen, schakelaars, invoer = s.get("knoppen") or [], s.get("schakelaars") or [], s.get("invoer") or []
    if not (knoppen or schakelaars or invoer):
        return False, "geen knoppen, schakelaars of invoervelden"
    seconden = max(1.0, min(4 * 3600.0, float(s.get("seconden") or 3600)))
    if getattr(d, "geschreven", None) is None:
        d.geschreven = {}
    while True:                                 # oude opdrachten (van voor deze stap) niet uitvoeren
        try:
            _INVOER.get_nowait()
        except queue.Empty:
            break
    log(f"  bediening: {len(knoppen)} knoppen, {len(schakelaars)} schakelaars, {len(invoer)} invoervelden; "
        "wacht op de gebruiker (Klaar = verder, Stop = recept stoppen) …")
    _monitor_meet(d, altijd=True)
    einde = time.time() + seconden
    n = 0
    while time.time() < einde:
        _controleer_stop()
        try:
            c = _INVOER.get(timeout=0.05)
        except queue.Empty:
            _monitor_meet(d)
            continue
        if c.get("klaar"):
            log("  bediening klaar.")
            return True, f"bediening klaar, {n}× geschreven"
        el, waarde = None, None
        try:
            if "knop" in c:
                el = knoppen[int(c["knop"])]
                waarde = el.get("waarde", 1)
            elif "schakelaar" in c:
                el = schakelaars[int(c["schakelaar"])]
                waarde = el.get("aan", 1) if c.get("aan") else el.get("uit", 0)
            elif "invoer" in c:
                el = invoer[int(c["invoer"])]
                waarde = c.get("waarde")
        except (IndexError, ValueError, TypeError):
            el = None
        if el is None or waarde in (None, ""):
            log(f"  » onbekende opdracht: {json.dumps(c, ensure_ascii=False)[:80]}")
            continue
        naam = el.get("naam") or f"0x{_int(el['index']):04X}:{_int(el.get('sub', 0))}"
        try:
            typ = (el.get("type") or ("BOOL" if "schakelaar" in c else "UINT")).upper()
            w = _getal(waarde, typ)
            if el.get("min") not in (None, "") and w < float(el["min"]) or el.get("max") not in (None, "") and w > float(el["max"]):
                log(f"  » {naam} ← {w} geweigerd: buiten {el.get('min', '…')} … {el.get('max', '…')}")
                continue
            d.schrijf(_int(el["index"]), _int(el.get("sub", 0)), typ, w)
            d.geschreven[naam] = w
            n += 1
            log(f"  » {naam} ← {w}")
        except Exception as e:  # noqa: BLE001
            log(f"  » {naam}: schrijven mislukt: {fouttekst(e)}")
        _monitor_meet(d, altijd=True)
    log(f"  bediening na {seconden:g} s beëindigd.")
    return True, f"bediening na {seconden:g} s beëindigd, {n}× geschreven"


_STAPPEN = {"identiteit": _stap_identiteit, "lees": _stap_lees, "schrijf": _stap_schrijf, "wacht": _stap_wacht,
            "pauze": _stap_wacht, "wacht_tot": _stap_wacht_tot, "herhaal": _stap_herhaal,
            "beweging": _stap_beweging, "doorgeefluik": _stap_doorgeefluik, "monitor": _stap_monitor,
            "bediening": _stap_bediening}


# Veelvoorkomende SDO-abortcodes (ETG.1000.6 / CiA 301) in woorden
_SDO_ABORT = {0x05040000: "time-out (geen antwoord van de slave)", 0x06010000: "toegang niet ondersteund",
              0x06010001: "alleen schrijven (lezen kan niet)", 0x06010002: "alleen lezen (schrijven kan niet)",
              0x06020000: "object bestaat niet in de dictionary", 0x06040041: "object kan niet in een PDO",
              0x06070010: "datatype/lengte klopt niet", 0x06090011: "subindex bestaat niet",
              0x06090030: "waarde buiten bereik", 0x08000000: "algemene fout", 0x08000020: "kan nu niet (toestand)",
              0x08000021: "kan nu niet (lokale besturing)", 0x08000022: "kan nu niet (toestand van het apparaat)"}


def fouttekst(e: Exception) -> str:
    """Leesbare fout, ook als pysoem een exceptie zonder tekst geeft: soort, abortcode en omschrijving."""
    delen = [type(e).__name__]
    code = getattr(e, "abort_code", None)
    if isinstance(code, int):
        delen.append(f"abortcode 0x{code:08X} {_SDO_ABORT.get(code, '')}".strip())
    for naam in ("desc", "wkc"):
        w = getattr(e, naam, None)
        if w not in (None, ""):
            delen.append(f"{naam} {w}")
    tekst = str(e).strip()
    if tekst and not isinstance(code, int):
        delen.append(tekst)
    if len(delen) == 1:
        delen.append("geen omschrijving (meestal: de slave antwoordde niet, mailbox-time-out)")
    return ", ".join(delen)


def _voer_stappen(d: _Drive, stappen: list, log, uit: list, prefix: str = "") -> bool:
    """De stappen na elkaar; `uit` krijgt per stap de uitkomst. Geeft False zodra een stap mislukt en het recept
    daar moet stoppen (standaard), of als er een stap mislukt is met "stoppen": false."""
    geslaagd = True
    for nr, s in enumerate(stappen, start=1):
        soort = (s.get("stap") or "").lower()
        titel = s.get("naam") or soort
        fn = _STAPPEN.get(soort)
        if _STOP.is_set():
            return False
        log(f"Stap {prefix}{nr}/{len(stappen)}: {titel}")
        if fn is None:
            ok, tekst = False, f"onbekende bouwsteen '{soort}'"
        else:
            try:
                ok, tekst = fn(d, s, log)
            except _Gestopt:
                ok, tekst = False, "gestopt door de gebruiker"
            except Exception as e:  # noqa: BLE001
                ok, tekst = False, "fout: " + fouttekst(e)
        _monitor_meet(d, altijd=True)
        uit.append({"nr": f"{prefix}{nr}", "stap": soort, "naam": titel, "geslaagd": ok, "tekst": tekst})
        log(("  ✓ " if ok else "  ✗ ") + tekst)
        if not ok:
            geslaagd = False
            if _STOP.is_set():
                log("  gestopt door de gebruiker.")
                return False
            if s.get("stoppen", True):
                log("  afgebroken bij deze stap.")
                return False
    return geslaagd


# ---- procesdata: recept in OP, PDO-objecten via de cyclische in-/uitgangsdata ----

def _bits_lezen(data: bytes, pos: int, bits: int) -> int:
    w = 0
    for k in range(bits):
        p = pos + k
        if p // 8 < len(data):
            w |= ((data[p // 8] >> (p % 8)) & 1) << k
    return w


def _bits_zetten(beeld: bytearray, pos: int, bits: int, w: int) -> None:
    for k in range(bits):
        p = pos + k
        if p // 8 < len(beeld):
            if (w >> k) & 1:
                beeld[p // 8] |= 1 << (p % 8)
            else:
                beeld[p // 8] &= ~(1 << (p % 8)) & 0xFF


class _Procesdata:
    """Procesdata-cyclus (ca. elke 2 ms, in een eigen draad) zodat de slave in OP komt. In PRE-OP doet een SDO op
    een PDO-object (bv. 0x7020 kleppen, 0x6020 druk bij de Swivel) niets aan de echte uitgangen: de firmware neemt
    de uitgangen alleen uit de procesdata. Alle slaves op de bus gaan mee naar OP (andere krijgen nullen)."""

    def __init__(self, master, slave, log, cyclus_ms: float = 2.0):
        self.master, self.slave, self.log = master, slave, log
        self.cyclus = max(1.0, min(10.0, cyclus_ms)) / 1000.0
        self.uit: dict[tuple[int, int], tuple[int, int]] = {}    # (index, sub) → (bitpositie, bits) in het uitgangsbeeld
        self.inn: dict[tuple[int, int], tuple[int, int]] = {}    # idem ingangsbeeld
        self.beeld = bytearray()
        self.slot = threading.Lock()
        self._stop = threading.Event()
        self._draad = None
        self._winmm = None
        self.cycli = 0
        self.langste = 0.0
        self.wkc_fout = 0

    @staticmethod
    def _plekken(kaart) -> dict:
        uit, pos = {}, 0
        for index, sub, bits in kaart or []:
            if index:                                  # index 0 = opvulling
                uit[(index, sub)] = (pos, bits)
            pos += bits
        return uit

    def start(self) -> bool:
        """Mapping lezen, naar SAFE-OP en OP. False (en terug naar PRE-OP) als het niet lukt; het recept gaat dan via SDO."""
        import pysoem
        from .ecat_io import _mapping
        kaart_uit = _mapping(self.slave, 0x1C12, self.log)
        kaart_in = _mapping(self.slave, 0x1C13, self.log)
        self.uit, self.inn = self._plekken(kaart_uit), self._plekken(kaart_in)
        if not self.uit and not self.inn:
            self.log("Geen PDO-toewijzing gevonden; het recept loopt in PRE-OP via SDO.")
            return False
        try:
            import ctypes
            self._winmm = ctypes.windll.winmm
            self._winmm.timeBeginPeriod(1)             # anders slaapt time.sleep 15 ms
        except Exception:  # noqa: BLE001
            self._winmm = None
        self.master.config_map()
        if self.master.state_check(pysoem.SAFEOP_STATE, 200_000) != pysoem.SAFEOP_STATE:
            self.master.read_state()
            self.log(f"Slave komt niet in SAFE-OP ({_toestand(self.slave.state)}, AL-status {_al_status(self.slave.al_status)}).")
            self.stop()
            return False
        self.beeld = bytearray(len(self.slave.output or b""))
        self.slave.output = bytes(self.beeld)
        self._draad = threading.Thread(target=self._loop, daemon=True)
        self._draad.start()
        self.master.state = pysoem.OP_STATE
        self.master.write_state()
        einde = time.time() + 5
        while time.time() < einde:
            if self.master.state_check(pysoem.OP_STATE, 50_000) == pysoem.OP_STATE:
                break
        self.master.read_state()
        if self.slave.state != pysoem.OP_STATE:
            self.log(f"Slave komt niet in OP ({_toestand(self.slave.state)}, AL-status {_al_status(self.slave.al_status)}); "
                     "het recept loopt in PRE-OP via SDO, uitgangen in de procesdata werken dan niet.")
            self.stop()
            return False
        namen = lambda d: ", ".join(f"0x{i:04X}:{s}" for (i, s) in d) or "-"  # noqa: E731
        self.log(f"Slave in OP, procesdata elke {self.cyclus * 1000:g} ms. Uit: {namen(self.uit)}; in: {namen(self.inn)}.")
        return True

    def _loop(self) -> None:
        verwacht = getattr(self.master, "expected_wkc", None)
        volgende = time.perf_counter()
        vorige = volgende
        while not self._stop.is_set():
            with self.slot:
                self.slave.output = bytes(self.beeld)
            self.master.send_processdata()
            wkc = self.master.receive_processdata(1000)
            if verwacht is not None and wkc != verwacht:
                self.wkc_fout += 1
            self.cycli += 1
            nu = time.perf_counter()
            self.langste = max(self.langste, nu - vorige)
            vorige = nu
            volgende += self.cyclus
            wacht = volgende - time.perf_counter()
            if wacht > 0:
                time.sleep(wacht)
            else:
                volgende = time.perf_counter()         # achter geraakt: niet inhalen

    def wacht_cycli(self, n: int = 3) -> None:
        """Wachten tot de cyclus n keer rond is (geschreven uitgang is verstuurd, ingang ververst)."""
        doel = self.cycli + n
        einde = time.time() + 0.5
        while self.cycli < doel and time.time() < einde:
            time.sleep(self.cyclus)

    def lees(self, index: int, sub: int, typ: str):
        """Waarde uit het ingangsbeeld (of wat wij in het uitgangsbeeld zetten); None als het geen PDO-object is."""
        if (index, sub) in self.inn:
            pos, bits = self.inn[(index, sub)]
            data = bytes(self.slave.input or b"")
        elif (index, sub) in self.uit:
            pos, bits = self.uit[(index, sub)]
            with self.slot:
                data = bytes(self.beeld)
        else:
            return None
        fmt, n = TYPEN[typ.upper()]
        if typ.upper() == "REAL" and bits == 32 and pos % 8 == 0:
            return struct.unpack(fmt, data[pos // 8:pos // 8 + 4])[0]
        w = _bits_lezen(data, pos, bits)
        if fmt[1].islower() and bits > 1 and w >> (bits - 1):    # type met teken
            w -= 1 << bits
        return w

    def schrijf(self, index: int, sub: int, typ: str, waarde) -> bool:
        """In het uitgangsbeeld zetten; False als het geen uitgang in de procesdata is."""
        if (index, sub) not in self.uit:
            return False
        pos, bits = self.uit[(index, sub)]
        fmt, _ = TYPEN[typ.upper()]
        w = struct.unpack("<I", struct.pack(fmt, waarde))[0] if typ.upper() == "REAL" else int(waarde)
        with self.slot:
            _bits_zetten(self.beeld, pos, bits, w & ((1 << bits) - 1))
        self.wacht_cycli()
        return True

    def stop(self) -> None:
        """Uitgangen uit, nog even verversen, slaves terug naar PRE-OP (SDO blijft dan werken)."""
        import pysoem
        try:
            if self._draad is not None:
                with self.slot:
                    self.beeld[:] = bytes(len(self.beeld))
                self.wacht_cycli(20)
                self._stop.set()
                self._draad.join(1.0)
                self._draad = None
                if self.langste > 0.05 or self.wkc_fout:
                    self.log(f"Procesdata: {self.cycli} cycli, langste {self.langste * 1000:.0f} ms, {self.wkc_fout} met afwijkende wkc.")
            self.master.state = pysoem.PREOP_STATE
            self.master.write_state()
            self.master.state_check(pysoem.PREOP_STATE, 500_000)
        except Exception as e:  # noqa: BLE001
            self.log(f"Procesdata stoppen: {e}")
        finally:
            if self._winmm is not None:
                try:
                    self._winmm.timeEndPeriod(1)
                except Exception:  # noqa: BLE001
                    pass
                self._winmm = None


class _PdoDrive:
    """_Drive waarbij lees/schrijf op PDO-objecten via de procesdata gaan en de rest via SDO."""

    def __init__(self, d: _Drive, pd: _Procesdata):
        self._d, self._pd = d, pd

    def __getattr__(self, naam):
        return getattr(self._d, naam)

    def lees(self, index: int, sub: int, typ: str):
        w = self._pd.lees(index, sub, typ)
        return w if w is not None else self._d.lees(index, sub, typ)

    def schrijf(self, index: int, sub: int, typ: str, waarde) -> None:
        if not self._pd.schrijf(index, sub, typ, waarde):
            self._d.schrijf(index, sub, typ, waarde)


def _werker_test(d: _Drive, o: dict, log) -> dict:
    stappen = o.get("stappen") or []
    uit = []
    geslaagd = _voer_stappen(d, stappen, log, uit)
    mis = [u for u in uit if not u["geslaagd"]]
    samenvatting = f"{len(uit)} stappen gedaan" + (f", {len(mis)} mislukt: " + "; ".join(m["tekst"] for m in mis[:5]) if mis else ", alles goed")
    if _STOP.is_set():
        samenvatting = f"gestopt door de gebruiker na {len(uit)} stappen"
    return {"geslaagd": geslaagd and not _STOP.is_set(), "samenvatting": samenvatting, "stappen": uit,
            "gestopt": _STOP.is_set(), **_identiteit(d)}


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
    while time.time() < einde and not _STOP.is_set():
        w = _monitor_meet(d, altijd=True)
        if w is not None:
            laatste = w
            n += 1
        time.sleep(interval)
    log(f"Klaar: {n} metingen.")
    return {"metingen": n, "waarden": laatste, "gestopt": _STOP.is_set(), **_identiteit(d)}


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


SII_SERIENUMMER = 0x000E    # EEPROM-woordadres van het serienummer (2 woorden, little-endian; niet in de CRC)


def lees_sii_serienummer(s) -> int | None:
    try:
        return int.from_bytes(bytes(s.eeprom_read(SII_SERIENUMMER))[:4], "little")
    except Exception:  # noqa: BLE001
        return None


def _werker_serienummer(d: _Drive, o: dict, log) -> dict:
    import pysoem
    s = d.slave
    nieuw = int(o["serienummer"])
    if not 0 < nieuw <= 0xFFFFFFFF:
        raise RuntimeError(f"Serienummer {nieuw} past niet in 32 bits of is 0.")
    oud = lees_sii_serienummer(s)
    if oud is None:
        raise RuntimeError("De EEPROM van de slave is niet te lezen.")
    log(f"EEPROM (SII 0x0E) bevat nu serienummer {oud}.")
    if oud == nieuw:
        log("Staat er al; niets geschreven.")
    else:
        if oud not in (0, 0xFFFFFFFF) and not o.get("overschrijven"):
            raise RuntimeError(f"In de EEPROM staat al serienummer {oud}. Alleen overschrijven als dat zeker de bedoeling is.")
        # in INIT schrijven: dan gebruikt de firmware de EEPROM niet tegelijk via de PDI
        _vraag_toestand(s, log, pysoem.INIT_STATE, 2.0)
        data = nieuw.to_bytes(4, "little")
        s.eeprom_write(SII_SERIENUMMER, data[:2])
        time.sleep(0.05)
        s.eeprom_write(SII_SERIENUMMER + 1, data[2:])
        time.sleep(0.05)
        terug = lees_sii_serienummer(s)
        if terug != nieuw:
            raise RuntimeError(f"EEPROM leest {terug} terug in plaats van {nieuw}.")
        log(f"Serienummer {nieuw} in de EEPROM gezet en teruggelezen.")
    coe = None
    try:
        _vraag_toestand(s, log, pysoem.PREOP_STATE, 3.0)
        coe = struct.unpack("<I", s.sdo_read(0x1018, 4)[:4])[0]
    except Exception:  # noqa: BLE001
        pass
    if coe is not None and coe != nieuw:
        log(f"Let op: 0x1018:4 meldt {coe}. De firmware moet het serienummer uit de EEPROM (SII 0x0E) in 0x1018:4 zetten; "
            "dat gebeurt meestal pas na een herstart, en anders niet.")
    return {"serienummer": nieuw, "was": oud, "coe": coe, "positie": d.positie}


def _lees_eeprom(s, aantal_bytes: int) -> bytes:
    """De eerste `aantal_bytes` van de EEPROM (eeprom_read geeft 4 bytes = 2 woorden per keer)."""
    uit = b""
    for w in range(0, (aantal_bytes + 3) // 2, 2):
        uit += bytes(s.eeprom_read(w))[:4]
    return uit[:aantal_bytes]


def _gebieden(oud: bytes, nieuw: bytes) -> list[str]:
    """Welke delen van de EEPROM verschillen, in woorden ('kop 0x00-0x07: …')."""
    namen = [(0x00, 0x08, "kop (PDI, alias, checksum)"), (0x08, 0x10, "identiteit"), (0x14, 0x18, "bootstrap-mailbox"),
             (0x18, 0x1D, "standaardmailbox/protocollen"), (0x3E, 0x40, "grootte/versie"), (0x40, 0x10000, "categorieën")]
    uit = []
    for van, tot, naam in namen:
        a, b = oud[van * 2:tot * 2], nieuw[van * 2:tot * 2]
        if a != b:
            uit.append(f"{naam}: nu {a[:16].hex(' ')}{' …' if len(a) > 16 else ''} → {b[:16].hex(' ')}{' …' if len(b) > 16 else ''}")
    return uit


def _werker_eeprom(d: _Drive, o: dict, log) -> dict:
    """EEPROM (SII) vullen uit het ESI; met alleen_vergelijken wordt er niets geschreven."""
    import pysoem
    from . import sii
    s = d.slave
    nieuw, info = sii.maak(o["esi"], int(o.get("serienummer") or 0))
    log(f"Uit het ESI: {info['naam']} {info['vendor']}/{info['productcode']} rev {info['revisie']}, sn {info['serienummer'] or '-'}, "
        f"PDI {info['pdi']}, mailbox {info['mailbox']}, {info['protocollen']}; {info['bytes']} van {info['eeprom']} bytes.")
    oud = _lees_eeprom(s, len(nieuw))
    v, p, r, sn = sii.lees_identiteit(oud)
    leeg = all(b == 0xFF for b in oud[0x10:0x20]) or all(b == 0 for b in oud[0x10:0x20])
    log("EEPROM nu: " + ("leeg" if leeg else f"0x{v:08X}/0x{p:08X} rev 0x{r:08X}, sn {sn}"))
    # het node-adres (alias, woord 4) van de print laten staan; dat zet 'Node-adressen…'
    alias = struct.unpack_from("<H", oud, 8)[0]
    if not leeg and alias not in (0, 0xFFFF):
        nieuw, info = sii.maak(o["esi"], int(o.get("serienummer") or 0), alias=alias)
        log(f"Node-adres (alias) {alias} blijft staan.")
    verschil = _gebieden(oud, nieuw)
    for regel in verschil:
        log("  verschilt: " + regel)
    if not verschil:
        log("De EEPROM is al precies zo; niets te schrijven.")
        return {"geschreven": 0, "gelijk": True, **info}
    if o.get("alleen_vergelijken"):
        return {"geschreven": 0, "gelijk": False, "verschillen": verschil, **info}

    nieuw_sn = int(info["serienummer"] or 0)
    ander = not leeg and ((v, p, r) != (int(info["vendor"], 16), int(info["productcode"], 16), int(info["revisie"], 16))
                          or (sn not in (0, 0xFFFFFFFF) and nieuw_sn and sn != nieuw_sn))
    if ander and not o.get("overschrijven"):
        raise RuntimeError(f"De EEPROM bevat al een andere identiteit (0x{v:08X}/0x{p:08X} rev 0x{r:08X}, sn {sn}). "
                           "Alleen overschrijven als dat zeker de bedoeling is.")
    _vraag_toestand(s, log, pysoem.INIT_STATE, 2.0)
    woorden = [w for w in range(len(nieuw) // 2) if oud[w * 2:w * 2 + 2] != nieuw[w * 2:w * 2 + 2]]
    log(f"{len(woorden)} woorden schrijven …")
    for i, w in enumerate(woorden, start=1):
        s.eeprom_write(w, nieuw[w * 2:w * 2 + 2])
        if i % 50 == 0:
            log(f"  … {i}/{len(woorden)}")
    terug = _lees_eeprom(s, len(nieuw))
    if terug != nieuw:
        fout = [w for w in range(len(nieuw) // 2) if terug[w * 2:w * 2 + 2] != nieuw[w * 2:w * 2 + 2]]
        raise RuntimeError(f"Teruglezen klopt niet op {len(fout)} woorden (eerste: 0x{fout[0]:04X}). Probeer opnieuw.")
    log("Geschreven en teruggelezen. Geef de print spanning uit/aan: de ESC leest de EEPROM alleen bij het opstarten.")
    return {"geschreven": len(woorden), "gelijk": False, "herstartNodig": True, **info}


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
                    gemeld = kale_versie(d2.tekst(0x100A))
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
        # identiteit en eeprom ook op een slave met een lege of verkeerde EEPROM (die meldt nog geen MDE-vendor)
        if o.get("eis_mde", True) and d.slave.man != MDE_VENDOR and o.get("wat") not in ("identiteit", "eeprom"):
            raise RuntimeError(f"Slave op positie {positie} is geen MDE-slave (vendor 0x{d.slave.man:08X}).")
        soort = o.get("wat")
        if soort in ("test", "monitor"):
            threading.Thread(target=_lees_stdin, daemon=True).start()
        if soort in ("identiteit", "test", "monitor"):
            # Eerst echt in PRE-OP: pas dan zet de slave zijn mailbox (SM0/SM1) open. Zonder deze stap vroegen we te
            # vroeg en gaf elke SDO 'WkcError, wkc 0' (Swivel, 14-10-2026); config_init wacht daar niet op.
            stand = _vraag_toestand(d.slave, log, pysoem.PREOP_STATE, 5.0)
            if stand != pysoem.PREOP_STATE:
                log(f"Let op: slave staat in {_toestand(stand)} in plaats van PRE-OP; SDO's kunnen mislukken.")
        if soort == "identiteit":
            uit = _identiteit(d)
        elif soort == "test":
            pd = None
            if o.get("procesdata", True):
                pd = _Procesdata(master, d.slave, log)
                try:
                    if not pd.start():
                        pd = None
                except Exception as e:  # noqa: BLE001
                    log(f"Procesdata starten mislukt ({fouttekst(e)}); het recept loopt in PRE-OP via SDO.")
                    pd.stop()
                    pd = None
            try:
                uit = _werker_test(_PdoDrive(d, pd) if pd else d, o, log)
                uit["procesdata"] = pd is not None
            finally:
                if pd is not None:
                    pd.stop()
        elif soort == "monitor":
            uit = _werker_monitor(d, o, log)
        elif soort == "serienummer":
            uit = _werker_serienummer(d, o, log)
        elif soort == "eeprom":
            uit = _werker_eeprom(d, o, log)
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


def _duur(stappen: list) -> float:
    """Bovengrens van de looptijd van een recept (s), voor de time-out van de werker."""
    totaal = 0.0
    for s in stappen:
        soort = (s.get("stap") or "").lower()
        if soort == "herhaal":
            totaal += max(1, int(s.get("keer") or 1)) * _duur(s.get("stappen") or [])
        elif soort == "bediening":
            totaal += min(4 * 3600.0, float(s.get("seconden") or 3600))
        else:
            totaal += float(s.get("timeout") or 5) + float(s.get("seconden") or 0)
    return totaal


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
             "eis_mde": parameters.get("eis_mde", True), "procesdata": parameters.get("procesdata", True)}
        job.log(f"Recept '{parameters.get('naam') or ''}': {len(stappen)} stappen op slave {o['positie']}.")
        job.voortgang(5)
        timeout = _duur(stappen) + 30
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


class Serienummer:
    NAAM = "mde.serienummer"
    OMSCHRIJVING = "MDE-slave: serienummer in de EEPROM (SII) schrijven"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        o = {"wat": "serienummer", "positie": int(parameters.get("positie") or 1),
             "serienummer": int(str(parameters.get("serienummer") or "0").strip()),
             "overschrijven": bool(parameters.get("overschrijven", False))}
        job.log(f"Serienummer {o['serienummer']} naar slave {o['positie']} …")
        job.klaar(_draai(inst, job, o, 60, werker_naam="mde"))


class Eeprom:
    NAAM = "mde.eeprom"
    OMSCHRIJVING = "MDE-slave: EEPROM (SII) vullen uit het ESI, of alleen vergelijken"
    beschikbaar = staticmethod(beschikbaar)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        _hulp.ruim_werkmap_op()
        esi = _hulp.haal_firmware(parameters.get("esi") or "", job, "esi.xml")
        o = {"wat": "eeprom", "positie": int(parameters.get("positie") or 1), "esi": esi,
             "serienummer": int(str(parameters.get("serienummer") or "0").strip() or 0),
             "overschrijven": bool(parameters.get("overschrijven", False)),
             "alleen_vergelijken": bool(parameters.get("alleen_vergelijken", False))}
        job.log(("EEPROM vergelijken met" if o["alleen_vergelijken"] else "EEPROM vullen uit") + f" het ESI, slave {o['positie']} …")
        job.klaar(_draai(inst, job, o, 180, werker_naam="mde"))


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
