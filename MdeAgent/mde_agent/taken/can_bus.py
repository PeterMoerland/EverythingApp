"""
Gedeelde toegang tot een GCAN USBCAN (ZLG/ECanVci-API) voor de CAN-taken.

Twee wegen naar de adapter:
  - 64-bit ECanVci64.dll: rechtstreeks via ctypes;
  - 32-bit ECanVci.dll (zoals bij eTuner): via CanLuisteraar.exe (32-bit hulpprogramma) dat
    frames als JSON-regels op stdout zet en zendopdrachten via stdin aanneemt.

Gebruik:
    with CanBus.open(inst, job, baud=1000, kanaal=0, luister_alleen=True, seconden=30) as bus:
        for frame in bus.frames():          # (t, can_id, ext, data) of None (even niets)
            ...
            bus.zend(0x643, b"\x02\x32\x00\x01")   # alleen als luister_alleen=False
            if klaar: bus.stop()

DLL: ECanVci.dll / ECanVci64.dll naast MdeAgent.exe, in C:\\ProgramData\\MDE\\Agent, of via
'can_dll' in agent.json. Apparaattype 3 = USBCAN-I, 4 = USBCAN-II; index = welke van meerdere
USBCAN's (eRob-Studio houdt er meestal één vast). Niet ingesteld = alle combinaties proberen,
de werkende wordt onthouden (agent.json: can_apparaattype, can_index).
"""

from __future__ import annotations

import ctypes
import json
import os
import subprocess
import sys
import time
from typing import Iterator

# bitrate → (Timing0, Timing1) volgens de ZLG/GCAN-tabel (16 MHz)
TIMING = {1000: (0x00, 0x14), 800: (0x00, 0x16), 500: (0x00, 0x1C), 250: (0x01, 0x1C),
          125: (0x03, 0x1C), 100: (0x04, 0x1C), 50: (0x09, 0x1C), 20: (0x18, 0x1C), 10: (0x31, 0x1C)}

Frame = tuple[float, int, int, bytes]     # (t, can_id, ext, data)


class _INIT_CONFIG(ctypes.Structure):
    _fields_ = [("AccCode", ctypes.c_uint), ("AccMask", ctypes.c_uint), ("Reserved", ctypes.c_uint),
                ("Filter", ctypes.c_ubyte), ("Timing0", ctypes.c_ubyte), ("Timing1", ctypes.c_ubyte),
                ("Mode", ctypes.c_ubyte)]


class _CAN_OBJ(ctypes.Structure):
    _fields_ = [("ID", ctypes.c_uint), ("TimeStamp", ctypes.c_uint), ("TimeFlag", ctypes.c_ubyte),
                ("SendType", ctypes.c_ubyte), ("RemoteFlag", ctypes.c_ubyte), ("ExternFlag", ctypes.c_ubyte),
                ("DataLen", ctypes.c_ubyte), ("Data", ctypes.c_ubyte * 8), ("Reserved", ctypes.c_ubyte * 3)]


def _basismap() -> str:
    return os.path.dirname(sys.executable if getattr(sys, "frozen", False) else os.path.abspath(__file__))


def _is_64bit_dll(pad: str) -> bool:
    """PE-kop lezen: machine 0x8664 = x64, 0x014C = x86."""
    try:
        with open(pad, "rb") as f:
            kop = f.read(0x200)
        pe = int.from_bytes(kop[0x3C:0x40], "little")
        return int.from_bytes(kop[pe + 4:pe + 6], "little") == 0x8664
    except Exception:  # noqa: BLE001
        return False


def zoek_dll(inst: dict) -> tuple[str, bool]:
    """(pad, is64). 64-bit DLL laden we zelf; een 32-bit (zoals die van eTuner) gaat via CanLuisteraar.exe."""
    kandidaten = [inst.get("can_dll") or ""]
    for naam in ("ECanVci64.dll", "ECANVCI64.dll", "ECanVci.dll", "ECANVCI.dll", "ControlCAN.dll"):
        kandidaten += [os.path.join(_basismap(), naam), os.path.join(r"C:\ProgramData\MDE\Agent", naam),
                       os.path.join(os.getcwd(), naam)]
    for k in kandidaten:
        if k and os.path.isfile(k):
            return k, _is_64bit_dll(k)
    return "", False


def zoek_helper() -> str:
    for k in (os.path.join(_basismap(), "CanLuisteraar.exe"),
              os.path.join(_basismap(), "..", "..", "uitvoer", "CanLuisteraar", "CanLuisteraar.exe"),
              os.path.join(r"C:\ProgramData\MDE\Agent", "CanLuisteraar.exe")):
        if os.path.isfile(k):
            return os.path.abspath(k)
    return ""


def beschikbaar(inst: dict):
    dll, is64 = zoek_dll(inst)
    if not dll:
        return False, ("ECanVci.dll / ECanVci64.dll (GCAN USBCAN-API, zit bv. bij eTuner) niet gevonden: zet hem naast "
                       "MdeAgent.exe of in C:\\ProgramData\\MDE\\Agent, of 'can_dll' in agent.json.")
    if not is64 and not zoek_helper():
        return False, f"{os.path.basename(dll)} is 32-bit en CanLuisteraar.exe (32-bit hulpprogramma) ontbreekt naast de agent."
    return True, ""


class CanBus:
    """Eén geopende CAN-adapter. Maak hem met CanBus.open(...)."""

    def __init__(self, job, seconden: int):
        self.job = job
        self.seconden = seconden
        self.gestopt = False
        self.dev_type = 0
        self.dev_index = 0
        self.kanaal = 0
        self.luister_alleen = True
        self._begin = time.time()
        # ctypes-weg
        self._dll = None
        # helper-weg
        self._proces: subprocess.Popen | None = None
        self._eerste: Frame | None = None      # frame dat bij het openen al binnenkwam

    # ------------------------------------------------------------------ openen
    @classmethod
    def open(cls, inst: dict, job, baud: int = 1000, kanaal: int = 0, luister_alleen: bool = True,
             seconden: int = 30, apparaattype=None, index=None) -> "CanBus":
        dll_pad, is64 = zoek_dll(inst)
        if not dll_pad:
            raise RuntimeError(beschikbaar(inst)[1])
        if baud not in TIMING:
            raise RuntimeError(f"Onbekende bitrate {baud}; kies uit {sorted(TIMING)}.")
        helper = "" if is64 else zoek_helper()
        if not is64 and not helper:
            raise RuntimeError(f"{os.path.basename(dll_pad)} is 32-bit en CanLuisteraar.exe ontbreekt naast de agent (opnieuw bouwen met bouw.bat).")

        gevraagd_type = apparaattype if apparaattype not in (None, "") else inst.get("can_apparaattype")
        typen = [int(gevraagd_type)] if gevraagd_type not in (None, "") else [3, 4]
        if index not in (None, ""):
            indexen = [int(index)]
        elif inst.get("can_index") not in (None, ""):
            onth = int(inst["can_index"])
            indexen = [onth] + [i for i in (0, 1, 2) if i != onth]     # onthouden eerst, rest als reserve
        else:
            indexen = [0, 1, 2]

        laatste_fout = ""
        combinaties = [(ix, ty) for ix in indexen for ty in typen]
        for i, (dev_index, dev_type) in enumerate(combinaties):
            bus = cls(job, seconden)
            bus.dev_type, bus.dev_index, bus.kanaal, bus.luister_alleen = dev_type, dev_index, kanaal, luister_alleen
            modus = "alleen luisteren" if luister_alleen else "zenden en ontvangen"
            job.log(f"CAN-adapter openen ({os.path.basename(dll_pad)}, {'64-bit' if is64 else '32-bit via CanLuisteraar.exe'}, "
                    f"type {dev_type}, index {dev_index}, CAN{kanaal + 1}, {baud} kbit, {modus}) …")
            try:
                if is64:
                    bus._open_ctypes(dll_pad, baud)
                else:
                    bus._open_helper(helper, dll_pad, baud)
            except RuntimeError as e:
                laatste_fout = str(e)
                if "OpenDevice" in laatste_fout and i + 1 < len(combinaties):
                    job.log(f"  index {dev_index}, type {dev_type}: niet te openen — volgende proberen.")
                    continue
                raise
            # gelukt: onthouden
            gewijzigd = False
            if gevraagd_type in (None, "") and inst.get("can_apparaattype") != dev_type:
                inst["can_apparaattype"] = dev_type; gewijzigd = True
            if index in (None, "") and inst.get("can_index") != dev_index:
                inst["can_index"] = dev_index; gewijzigd = True
            if gewijzigd:
                try:
                    from .. import instellingen
                    instellingen.bewaar(inst)
                    job.log(f"Onthouden in agent.json: can_apparaattype={dev_type}, can_index={dev_index}.")
                except Exception:  # noqa: BLE001
                    pass
            return bus
        raise RuntimeError("Geen USBCAN te openen op index " + ", ".join(str(i) for i in indexen) + ". " + laatste_fout)

    def _open_ctypes(self, dll_pad: str, baud: int) -> None:
        dll = ctypes.WinDLL(dll_pad)
        if dll.OpenDevice(self.dev_type, self.dev_index, 0) != 1:
            raise RuntimeError("OpenDevice mislukt: zit de USBCAN erin, is het stuurprogramma geïnstalleerd en is hij niet in gebruik door eTuner/eRob-Studio?")
        try:
            cfg = _INIT_CONFIG(AccCode=0, AccMask=0xFFFFFFFF, Reserved=0, Filter=0,
                               Timing0=TIMING[baud][0], Timing1=TIMING[baud][1],
                               Mode=1 if self.luister_alleen else 0)
            if dll.InitCAN(self.dev_type, self.dev_index, self.kanaal, ctypes.byref(cfg)) != 1:
                raise RuntimeError("InitCAN mislukt (verkeerd kanaal of bitrate?).")
            if dll.StartCAN(self.dev_type, self.dev_index, self.kanaal) != 1:
                raise RuntimeError("StartCAN mislukt.")
        except Exception:
            dll.CloseDevice(self.dev_type, self.dev_index)
            raise
        self._dll = dll
        self._begin = time.time()

    def _open_helper(self, helper: str, dll_pad: str, baud: int) -> None:
        cmd = [helper, dll_pad, str(self.dev_type), str(self.dev_index), str(self.kanaal), str(baud),
               str(self.seconden), "1" if self.luister_alleen else "0"]
        proces = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                  text=True, encoding="utf-8", errors="replace", bufsize=1,
                                  creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        self._proces = proces
        self._begin = time.time()
        # Het openen gebeurt in het kind; een fout komt als eerste regel. Even wachten op de eerste
        # regel (frame of fout); komt er binnen 3 s niets, dan is het apparaat open en de bus stil.
        assert proces.stdout is not None
        wacht_tot = time.time() + 3.0
        while time.time() < wacht_tot:
            if proces.poll() is not None:
                # al gestopt: fout uitlezen
                for regel in proces.stdout:
                    o = self._parse(regel)
                    if o and "error" in o:
                        raise RuntimeError(str(o["error"]))
                raise RuntimeError("CanLuisteraar.exe stopte meteen zonder melding.")
            # niet-blokkerend lezen kan niet netjes op Windows-pipes; daarom een korte peek via een thread
            regel = self._lees_regel_met_timeout(0.2)
            if regel is None:
                continue
            o = self._parse(regel)
            if not o:
                continue
            if "error" in o:
                raise RuntimeError(str(o["error"]))
            if "id" in o:
                self._eerste = (float(o.get("t", 0)), int(o.get("id", 0)), int(o.get("ext", 0)), bytes.fromhex(o.get("data", "")))
                return
        # geen fout en geen frame: open en stil

    # -------------------------------------------------------------- hulp helper
    _leesthread = None
    _wachtrij = None

    def _lees_regel_met_timeout(self, timeout: float):
        import queue
        import threading
        if self._wachtrij is None:
            self._wachtrij = queue.Queue()

            def lezer():
                try:
                    assert self._proces is not None and self._proces.stdout is not None
                    for regel in self._proces.stdout:
                        self._wachtrij.put(regel)
                except Exception:  # noqa: BLE001
                    pass
                self._wachtrij.put(None)      # einde
            self._leesthread = threading.Thread(target=lezer, daemon=True)
            self._leesthread.start()
        try:
            return self._wachtrij.get(timeout=timeout)
        except queue.Empty:
            return None

    def _parse(self, regel: str):
        regel = (regel or "").strip()
        if not regel:
            return None
        try:
            return json.loads(regel)
        except Exception:  # noqa: BLE001
            self.job.log("CanLuisteraar: " + regel[:200])
            return None

    # --------------------------------------------------------------- gebruiken
    @property
    def verstreken(self) -> float:
        return time.time() - self._begin

    def stop(self) -> None:
        """Eerder stoppen: frames() houdt op, sluiten gebeurt in close()."""
        self.gestopt = True
        if self._proces is not None and self._proces.stdin is not None:
            try:
                self._proces.stdin.write('{"stop":true}\n'); self._proces.stdin.flush()
            except Exception:  # noqa: BLE001
                pass

    def zend(self, can_id: int, data: bytes) -> None:
        if self.luister_alleen:
            raise RuntimeError("De adapter staat op alleen luisteren; zenden kan niet.")
        if self._dll is not None:
            obj = _CAN_OBJ(ID=can_id, SendType=0, RemoteFlag=0, ExternFlag=1 if can_id > 0x7FF else 0, DataLen=len(data))
            for i, b in enumerate(data[:8]):
                obj.Data[i] = b
            if self._dll.Transmit(self.dev_type, self.dev_index, self.kanaal, ctypes.byref(obj), 1) != 1:
                raise RuntimeError(f"Zenden van 0x{can_id:03X} mislukt (Transmit).")
            return
        assert self._proces is not None and self._proces.stdin is not None
        self._proces.stdin.write(json.dumps({"id": can_id, "data": data.hex()}) + "\n")
        self._proces.stdin.flush()

    def frames(self) -> Iterator[Frame | None]:
        """Frames tot de tijd om is of stop() is aangeroepen. None = even niets (ongeveer elke 20 ms)."""
        if self._eerste is not None:
            yield self._eerste
            self._eerste = None
        if self._dll is not None:
            buf = (_CAN_OBJ * 256)()
            while self.verstreken < self.seconden and not self.gestopt:
                n = self._dll.Receive(self.dev_type, self.dev_index, self.kanaal, buf, 256, 20)
                if n <= 0 or n == 0xFFFFFFFF:
                    yield None
                    continue
                t = round(self.verstreken, 3)
                for i in range(n):
                    f = buf[i]
                    yield (t, f.ID, f.ExternFlag, bytes(f.Data[:f.DataLen]))
            return
        # helper
        while not self.gestopt or True:
            regel = self._lees_regel_met_timeout(0.05)
            if regel is None:
                if self._proces is not None and self._proces.poll() is not None:
                    return
                yield None
                continue
            o = self._parse(regel)
            if not o:
                continue
            if "error" in o:
                raise RuntimeError("CanLuisteraar: " + str(o["error"]))
            if o.get("klaar"):
                return
            if "gezonden" in o:
                if not o.get("ok"):
                    self.job.log(f"  zenden van 0x{int(o['gezonden']):03X} mislukt ({o.get('reden', 'Transmit')}).")
                continue
            if "log" in o:
                self.job.log("CanLuisteraar: " + str(o["log"]))
                continue
            if "id" in o:
                yield (float(o.get("t", 0)), int(o.get("id", 0)), int(o.get("ext", 0)), bytes.fromhex(o.get("data", "")))

    def close(self) -> None:
        if self._dll is not None:
            try:
                self._dll.CloseDevice(self.dev_type, self.dev_index)
            except Exception:  # noqa: BLE001
                pass
            self._dll = None
        if self._proces is not None:
            try:
                if self._proces.stdin is not None:
                    try:
                        self._proces.stdin.write('{"stop":true}\n'); self._proces.stdin.flush()
                    except Exception:  # noqa: BLE001
                        pass
                    self._proces.stdin.close()
                self._proces.wait(timeout=3)
            except Exception:  # noqa: BLE001
                pass
            if self._proces.poll() is None:
                self._proces.kill()
            self._proces = None

    def __enter__(self) -> "CanBus":
        return self

    def __exit__(self, *exc) -> None:
        self.close()
