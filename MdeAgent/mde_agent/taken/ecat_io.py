"""
Taak ecat.io: digitale EtherCAT-IO (bijv. Omron GX-MD1622: 8 in / 8 uit, 24 V PNP) live bedienen.

De agent is hier de EtherCAT-master met een procesdata-cyclus (ongeveer elke 2 ms): alle slaves op de bus
gaan naar OP; de ingangen komen live naar de app, de uitgangen zet de app met opdrachten. Uitgangen werken
alleen zolang deze taak loopt: stopt hij (of valt de cyclus weg), dan zet de watchdog van de slave ze uit.
Drives op dezelfde bus gaan ook naar OP, met nullen in hun procesdata (controlword 0 = niet ingeschakeld).

Welke bits digitale IO zijn, leest de werker per slave uit de PDO-toewijzing (0x1C12/0x1C13 en de mapping):
objecten 0x3020 / 0x6000 / 0x6100 (ingangen) en 0x3220 / 0x6200 / 0x6300 (uitgangen), zoals Omron GX en
andere CiA-401-IO. Zonder CoE-mailbox valt hij terug op het hele in-/uitgangsbeeld als bits.

Parameters:
    seconden   hoe lang (standaard 1800, max 7200); Stop = eerder klaar
    cyclus_ms  standaard 2

Opdrachten tijdens het lopen (POST /jobs/<id>/invoer, {"tekst": "<json>"}):
    {"uit": {"positie": 1, "bit": 0, "aan": true}}     één uitgang
    {"uit": {"positie": 1, "masker": 5}}               alle uitgangen van die slave
    {"filter": {"positie": 1, "code": 0}}              Omron GX: 0x3000 Input Time Constant (0 = 0 ms … 7 = 32 ms)
    {"stop": true}

Tussenstand (job.resultaat, live): {"slaves": [{positie, naam, vendor, product, toestand, in: [0/1…],
    uit: [0/1…], fout}], "cyclus_ms": gemiddeld, "max_ms": langste, "wkc": n, "wkc_verwacht": n, "seconden": t}
"""

import json
import queue
import struct
import subprocess
import sys
import threading
import time

from . import coe

NAAM = "ecat.io"
OMSCHRIJVING = "EtherCAT-IO (Omron GX e.d.): ingangen live, uitgangen zetten (procesdata-cyclus)"

IN_OBJ = (0x3020, 0x6000, 0x6100, 0x6020)
UIT_OBJ = (0x3220, 0x6200, 0x6300, 0x6220)


def beschikbaar(inst: dict):
    return coe.beschikbaar(inst)


# ---------------------------------------------------------------------------
#  Werker (apart proces): MdeAgent --werker ecatio <ifname> @opdracht.json
#  Leest opdrachten als JSON-regels van stdin; schrijft {"log"}/{"live"} en als laatste het resultaat.
# ---------------------------------------------------------------------------

def _mapping(s, sm_index: int, log) -> list[tuple[int, int, int]] | None:
    """PDO-toewijzing van SM2 (0x1C12) of SM3 (0x1C13) lezen: lijst van (index, sub, bits) in volgorde."""
    try:
        n = s.sdo_read(sm_index, 0)[0]
        uit = []
        for i in range(1, n + 1):
            pdo = struct.unpack("<H", s.sdo_read(sm_index, i)[:2])[0]
            m = s.sdo_read(pdo, 0)[0]
            for j in range(1, m + 1):
                e = struct.unpack("<I", s.sdo_read(pdo, j)[:4])[0]
                uit.append((e >> 16, (e >> 8) & 0xFF, e & 0xFF))
        return uit
    except Exception as e:  # noqa: BLE001
        log(f"  PDO-toewijzing 0x{sm_index:04X} niet te lezen ({e}); hele beeld als bits.")
        return None


def _digitale_bits(mapping, objecten) -> list[int] | None:
    """Bitposities (in het in- of uitgangsbeeld van de slave) van de digitale kanalen, in kanaalvolgorde."""
    if mapping is None:
        return None
    bits, pos = [], 0
    for index, _sub, n in mapping:
        if index in objecten:
            bits.extend(range(pos, pos + n))          # 1 bit per kanaal, of een byte/woord = 8/16 kanalen
        pos += n
    return bits


def _fout_byte(mapping) -> int | None:
    """Byte-positie van Omron 'Sysmac Error Status' (0x2002:01) in het ingangsbeeld, als die gemapt is."""
    if mapping is None:
        return None
    pos = 0
    for index, sub, n in mapping:
        if index == 0x2002 and sub == 1 and pos % 8 == 0:
            return pos // 8
        pos += n
    return None


def _bit(data: bytes, pos: int) -> int:
    return (data[pos // 8] >> (pos % 8)) & 1 if pos // 8 < len(data) else 0


def _toestand(code: int) -> str:
    return {1: "INIT", 2: "PRE-OP", 3: "BOOT", 4: "SAFE-OP", 8: "OP"}.get(code & 0x0F, f"0x{code:02X}") + \
        (" (fout)" if code & 0x10 else "")


def werker(ifname: str, opdracht_json: str) -> dict:
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

    seconden = max(5, min(7200, float(o.get("seconden") or 1800)))
    cyclus = max(1.0, min(20.0, float(o.get("cyclus_ms") or 2))) / 1000.0

    # opdrachten van stdin in een eigen draad, zodat de cyclus nooit op invoer wacht
    opdrachten: queue.Queue = queue.Queue()

    def lees_stdin():
        for regel in sys.stdin:
            regel = regel.strip()
            if regel:
                opdrachten.put(regel)
        opdrachten.put('{"stop": true}')          # agent weg = stoppen
    threading.Thread(target=lees_stdin, daemon=True).start()

    # Windows: de timer op 1 ms zetten, anders slaapt time.sleep 15 ms
    winmm = None
    try:
        import ctypes
        winmm = ctypes.windll.winmm
        winmm.timeBeginPeriod(1)
    except Exception:  # noqa: BLE001
        winmm = None

    master = pysoem.Master()
    try:
        master.open(ifname)
    except Exception as e:  # noqa: BLE001
        return {"error": f"EtherCAT-adapter '{ifname}' openen mislukt: {e}"}
    try:
        n = master.config_init()
        if n <= 0:
            raise RuntimeError("Geen slaves gevonden op de EtherCAT-bus.")
        log(f"{n} slave(s) op de bus.")
        info = []
        for i, s in enumerate(master.slaves):
            kaart_in = kaart_uit = None
            if getattr(s, "mbx_proto", 0x04) & 0x04:      # CoE-mailbox: PDO-toewijzing via SDO lezen
                kaart_in = _mapping(s, 0x1C13, log)
                kaart_uit = _mapping(s, 0x1C12, log)
            info.append({"s": s, "kaart_in": kaart_in, "kaart_uit": kaart_uit})
            log(f"  {i + 1}: {s.name} (vendor 0x{s.man:08X}, product 0x{s.id:08X}, rev 0x{s.rev:08X})")

        master.config_map()
        if master.state_check(pysoem.SAFEOP_STATE, 200_000) != pysoem.SAFEOP_STATE:
            master.read_state()
            log("Niet alle slaves in SAFE-OP: " + ", ".join(f"{i + 1}={_toestand(s.state)}" for i, s in enumerate(master.slaves)))

        for x in info:
            s = x["s"]
            nin, nuit = len(s.input or b""), len(s.output or b"")
            x["in_bits"] = _digitale_bits(x["kaart_in"], IN_OBJ)
            x["uit_bits"] = _digitale_bits(x["kaart_uit"], UIT_OBJ)
            if x["in_bits"] is None:
                x["in_bits"] = list(range(nin * 8))
            if x["uit_bits"] is None:
                x["uit_bits"] = list(range(nuit * 8))
            x["fout_byte"] = _fout_byte(x["kaart_in"])
            x["beeld"] = bytearray(nuit)            # wat wij naar de uitgangen sturen
            log(f"  {s.name}: {nin} byte in ({len(x['in_bits'])} digitale ingangen), "
                f"{nuit} byte uit ({len(x['uit_bits'])} digitale uitgangen).")

        def zet_uit():
            for x in info:
                if len(x["beeld"]):
                    x["s"].output = bytes(x["beeld"])

        # naar OP, met de cyclus erbij (zonder procesdata blijven slaves in SAFE-OP hangen)
        zet_uit()
        master.send_processdata()
        master.receive_processdata(2000)
        master.state = pysoem.OP_STATE
        master.write_state()
        einde_op = time.time() + 5
        while time.time() < einde_op:
            master.send_processdata()
            master.receive_processdata(2000)
            if master.state_check(pysoem.OP_STATE, 2000) == pysoem.OP_STATE:
                break
            time.sleep(cyclus)
        master.read_state()
        log("Toestand: " + ", ".join(f"{i + 1}={_toestand(s.state)}" for i, s in enumerate(master.slaves)))
        wkc_verwacht = master.expected_wkc if hasattr(master, "expected_wkc") else None

        begin = time.time()
        volgende = time.perf_counter()
        tijden = []
        langste = 0.0
        laatst_live = 0.0
        vorige_beeld = None
        stoppen = False
        wkc = 0
        laatst_state = 0.0
        while not stoppen and time.time() - begin < seconden:
            t0 = time.perf_counter()
            master.send_processdata()
            wkc = master.receive_processdata(1000)
            # opdrachten verwerken
            while True:
                try:
                    regel = opdrachten.get_nowait()
                except queue.Empty:
                    break
                try:
                    c = json.loads(regel)
                except Exception:  # noqa: BLE001
                    log(f"Onbekende opdracht: {regel[:80]}")
                    continue
                if c.get("stop"):
                    stoppen = True
                elif isinstance(c.get("uit"), dict):
                    u = c["uit"]
                    p = int(u.get("positie") or 0)
                    if not 1 <= p <= len(info):
                        log(f"Geen slave op positie {p}.")
                        continue
                    x = info[p - 1]
                    if "masker" in u:
                        m = int(u["masker"])
                        for k, pos in enumerate(x["uit_bits"]):
                            b = (m >> k) & 1
                            x["beeld"][pos // 8] = (x["beeld"][pos // 8] & ~(1 << (pos % 8))) | (b << (pos % 8))
                    else:
                        k = int(u.get("bit") or 0)
                        if 0 <= k < len(x["uit_bits"]):
                            pos = x["uit_bits"][k]
                            if u.get("aan"):
                                x["beeld"][pos // 8] |= 1 << (pos % 8)
                            else:
                                x["beeld"][pos // 8] &= ~(1 << (pos % 8)) & 0xFF
                    zet_uit()
                elif isinstance(c.get("filter"), dict):
                    f = c["filter"]
                    p = int(f.get("positie") or 0)
                    try:
                        info[p - 1]["s"].sdo_write(0x3000, 0, bytes([int(f.get("code") or 0) & 0x07]))
                        log(f"Slave {p}: ingangsfilter (0x3000) = code {int(f.get('code') or 0)}.")
                    except Exception as e:  # noqa: BLE001
                        log(f"Slave {p}: ingangsfilter zetten mislukt: {e}")
            # live naar de app: bij een verandering, en anders twee keer per seconde
            nu = time.time()
            if nu - laatst_state > 1.0:
                master.read_state()
                laatst_state = nu
            beeld = tuple(bytes(x["s"].input or b"") for x in info)
            if beeld != vorige_beeld and nu - laatst_live > 0.05 or nu - laatst_live > 0.5:
                gem = sum(tijden) / len(tijden) * 1000 if tijden else 0
                live({
                    "slaves": [{
                        "positie": i + 1, "naam": x["s"].name, "vendor": f"0x{x['s'].man:08X}", "product": f"0x{x['s'].id:08X}",
                        "toestand": _toestand(x["s"].state),
                        "in": [_bit(beeld[i], pos) for pos in x["in_bits"]],
                        "uit": [_bit(bytes(x["beeld"]), pos) for pos in x["uit_bits"]],
                        "fout": beeld[i][x["fout_byte"]] if x["fout_byte"] is not None and x["fout_byte"] < len(beeld[i]) else None,
                    } for i, x in enumerate(info)],
                    "cyclus_ms": round(gem, 2), "max_ms": round(langste * 1000, 1),
                    "wkc": wkc, "wkc_verwacht": wkc_verwacht, "seconden": round(nu - begin, 1),
                })
                vorige_beeld = beeld
                laatst_live = nu
                tijden.clear()
                langste = 0.0
            # wachten tot de volgende cyclus
            volgende += cyclus
            wacht = volgende - time.perf_counter()
            if wacht > 0:
                time.sleep(wacht)
            else:
                volgende = time.perf_counter()     # achter geraakt: niet inhalen
            duur = time.perf_counter() - t0
            tijden.append(duur)
            langste = max(langste, duur)

        # netjes stoppen: uitgangen uit, nog even verversen, dan INIT
        for x in info:
            x["beeld"][:] = bytes(len(x["beeld"]))
        zet_uit()
        for _ in range(20):
            master.send_processdata()
            master.receive_processdata(1000)
            time.sleep(cyclus)
        master.state = pysoem.INIT_STATE
        master.write_state()
        log(f"Gestopt na {time.time() - begin:.0f} s; uitgangen uit, bus naar INIT.")
        return {"slaves": [{"positie": i + 1, "naam": x["s"].name} for i, x in enumerate(info)],
                "seconden": round(time.time() - begin, 1), "gestopt": stoppen}
    except Exception as e:  # noqa: BLE001
        return {"error": str(e)}
    finally:
        try:
            master.close()
        except Exception:  # noqa: BLE001
            pass
        if winmm is not None:
            try:
                winmm.timeEndPeriod(1)
            except Exception:  # noqa: BLE001
                pass


# ---------------------------------------------------------------------------
#  Taak (vanuit de agent): werker starten, invoer doorgeven, live meelezen
# ---------------------------------------------------------------------------

def voer_uit(job, inst: dict, parameters: dict) -> None:
    import os
    import tempfile
    ifname = coe._ifname(inst)
    o = {"seconden": parameters.get("seconden") or 1800, "cyclus_ms": parameters.get("cyclus_ms") or 2}
    fd, pad = tempfile.mkstemp(prefix="mde-ecatio-", suffix=".json")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(o, f)
    cmd = [sys.executable] + ([] if getattr(sys, "frozen", False) else ["-m", "mde_agent"]) + ["--werker", "ecatio", ifname, "@" + pad]
    job.log(f"EtherCAT-IO op {ifname}, cyclus {o['cyclus_ms']} ms (Stop = uitgangen uit en bus naar INIT).")
    laatste = None
    stop_gestuurd = False
    begin = time.time()
    timeout = float(o["seconden"]) + 60
    proces = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              text=True, encoding="utf-8", errors="replace", bufsize=1,
                              creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    assert proces.stdin is not None and proces.stdout is not None

    def stuur(regel: str) -> None:
        try:
            proces.stdin.write(regel + "\n")
            proces.stdin.flush()
        except Exception:  # noqa: BLE001
            pass

    try:
        # de werker stuurt minstens twee keer per seconde een tussenstand, dus hier komt elke 0,5 s een regel langs
        for regel in proces.stdout:
            regel = regel.strip()
            if regel:
                try:
                    obj = json.loads(regel)
                except Exception:  # noqa: BLE001
                    job.log(regel)
                    obj = None
                if isinstance(obj, dict) and set(obj.keys()) == {"log"}:
                    job.log(obj["log"])
                elif isinstance(obj, dict) and set(obj.keys()) == {"live"} and isinstance(obj["live"], dict):
                    job.resultaat = {**obj["live"], "live": True}
                elif obj is not None:
                    laatste = obj
            for tekst in job.lees_invoer():
                stuur(tekst)
            if getattr(job, "stop_gevraagd", False) and not stop_gestuurd:
                stuur('{"stop": true}')
                stop_gestuurd = True
            if time.time() - begin > timeout:
                proces.kill()
                raise RuntimeError("EtherCAT-IO reageerde niet meer.")
        proces.wait(timeout=10)
    finally:
        if proces.poll() is None:
            proces.kill()
        try:
            os.remove(pad)
        except OSError:
            pass
    if not isinstance(laatste, dict):
        raise RuntimeError("De werker gaf geen resultaat terug.")
    for r in laatste.pop("log", []) or []:
        job.log(r)
    if laatste.get("error"):
        raise RuntimeError(laatste["error"])
    job.klaar(laatste)
