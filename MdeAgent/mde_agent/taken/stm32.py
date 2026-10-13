"""
Taak stm32.flashen: een STM32 programmeren via ST-LINK (SWD).

Twee mogelijke motoren, in deze volgorde:
  1. STM32_Programmer_CLI.exe (STM32CubeProgrammer van ST) — als die op de pc
     staat of het pad in de instellingen is ingevuld.
  2. pyocd (pip, zit in de agent ingebakken) — werkt met ST-LINK v2/v3 en heeft
     het "target"-type nodig (bv. stm32g474retx); zonder target probeert hij
     automatisch te herkennen, wat bij ST-LINK meestal lukt.

Parameters:
    firmware   URL of pad van .hex/.bin/.elf
    adres      startadres voor .bin (standaard "0x08000000"; .hex/.elf hebben het al)
    target     pyocd-targetnaam (optioneel)
    wissen     true = eerst hele chip wissen
    uid_adres  adres van het 96-bits unieke ID (optioneel; anders geschat op familie)

Resultaat: {"uid": "0123456789abcdef01234567", "device": "...", "motor": "cubeprog|pyocd"}
"""

import glob
import os
import re

from . import _hulp

NAAM = "stm32.flashen"
OMSCHRIJVING = "STM32 flashen via ST-LINK"

# Unieke-ID-adressen per familie (referentiehandleidingen ST).
UID_ADRES = {
    "STM32F0": 0x1FFFF7AC, "STM32F1": 0x1FFFF7E8, "STM32F2": 0x1FFF7A10, "STM32F3": 0x1FFFF7AC,
    "STM32F4": 0x1FFF7A10, "STM32F7": 0x1FF0F420, "STM32G0": 0x1FFF7590, "STM32G4": 0x1FFF7590,
    "STM32H7": 0x1FF1E800, "STM32L0": 0x1FF80050, "STM32L1": 0x1FF80050, "STM32L4": 0x1FFF7590,
    "STM32L5": 0x0BFA0590, "STM32U5": 0x0BFA0700, "STM32WB": 0x1FFF7590, "STM32C0": 0x1FFF7550,
}

ZOEKPADEN_CUBEPROG = [
    r"C:\Program Files\STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe",
    r"C:\Program Files (x86)\STMicroelectronics\STM32Cube\STM32CubeProgrammer\bin\STM32_Programmer_CLI.exe",
    r"C:\ST\STM32CubeIDE*\STM32CubeIDE\plugins\com.st.stm32cube.ide.mcu.externaltools.cubeprogrammer.*\tools\bin\STM32_Programmer_CLI.exe",
]


def zoek_cubeprog(inst: dict) -> str:
    if inst.get("stm32_programmer_cli") and os.path.isfile(inst["stm32_programmer_cli"]):
        return inst["stm32_programmer_cli"]
    for patroon in ZOEKPADEN_CUBEPROG:
        for pad in glob.glob(patroon):
            if os.path.isfile(pad):
                return pad
    return ""


def beschikbaar(inst: dict):
    if zoek_cubeprog(inst):
        return True, ""
    try:
        import pyocd  # noqa: F401
        return True, ""
    except Exception as e:  # noqa: BLE001
        return False, f"Geen STM32CubeProgrammer gevonden en pyocd ontbreekt: {e}"


class _Log:
    """Vangt de regels van een proces op én geeft ze door aan de job."""
    def __init__(self, job): self.job, self.regels = job, []
    def log(self, t): self.regels.append(t); self.job.log(t)
    def voortgang(self, p): self.job.voortgang(p)
    @property
    def tekst(self): return "\n".join(self.regels)


def _uid_adres(device: str, parameters: dict) -> int | None:
    if parameters.get("uid_adres"):
        return int(str(parameters["uid_adres"]), 0)
    d = device.upper().replace(" ", "")
    for fam, adres in UID_ADRES.items():
        if fam in d:
            return adres
    return None


def _hex_uid(woorden: list[int]) -> str:
    return "".join(f"{w:08x}" for w in woorden)


# ---------------------------------------------------------------------------
#  Motor 1: STM32CubeProgrammer CLI
# ---------------------------------------------------------------------------

def _cubeprog(job, inst, exe: str, parameters: dict, pad: str) -> dict:
    basis = [exe, "-c", "port=SWD", "mode=UR", "reset=HWrst"]

    job.log("Verbinden via ST-LINK …")
    v = _Log(job)
    if _hulp.volg_proces(basis, v, 60) != 0:
        raise RuntimeError("Geen verbinding met de STM32 via ST-LINK.")
    device = ""
    m = re.search(r"Device name\s*:\s*(.+)", v.tekst)
    if m:
        device = m.group(1).strip()
    job.log(f"Device: {device or 'onbekend'}")
    job.voortgang(15)

    if parameters.get("wissen"):
        job.log("Chip wissen …")
        if _hulp.volg_proces(basis + ["-e", "all"], _Log(job), 180) != 0:
            raise RuntimeError("Wissen mislukt.")
        job.voortgang(30)

    job.log("Schrijven …")
    cmd = basis + ["-w", pad]
    if pad.lower().endswith(".bin"):
        cmd.append(str(parameters.get("adres") or "0x08000000"))
    cmd += ["-v", "-rst"]
    if _hulp.volg_proces(cmd, _Log(job), 300, voortgang_regex=r"(\d+)%") != 0:
        raise RuntimeError("Programmeren/verifiëren mislukt.")
    job.voortgang(90)

    uid = ""
    adres = _uid_adres(device, parameters)
    if adres:
        v = _Log(job)
        if _hulp.volg_proces(basis + ["-r32", f"0x{adres:08X}", "12"], v, 60) == 0:
            woorden = [int(x, 16) for x in re.findall(r":\s*((?:[0-9A-Fa-f]{8}\s*){1,4})", v.tekst)
                       for x in x.split()]
            if len(woorden) >= 3:
                uid = _hex_uid(woorden[:3])
    return {"uid": uid, "device": device, "motor": "cubeprog"}


# ---------------------------------------------------------------------------
#  Motor 2: pyocd (in de agent ingebakken)
# ---------------------------------------------------------------------------

def werker(*argv: str) -> int:
    """`MdeAgent --werker pyocd <pyocd-argumenten>`."""
    from pyocd.__main__ import main
    import sys
    sys.argv = ["pyocd", *argv]
    try:
        main()
        return 0
    except SystemExit as e:
        return int(e.code or 0)


def _pyocd(job, inst, parameters: dict, pad: str) -> dict:
    target = parameters.get("target") or ""
    t = ["-t", target] if target else []

    job.log("Verbinden via ST-LINK (pyocd) …")
    v = _Log(job)
    _hulp.volg_proces(_hulp.werker_commando("pyocd", "list", "-p"), v, 30)
    if "No available debug probes" in v.tekst:
        raise RuntimeError("Geen ST-LINK gevonden. Zit hij erin en is de driver geïnstalleerd?")
    job.voortgang(15)

    cmd = _hulp.werker_commando("pyocd", "flash", *t, "--frequency", "4000000")
    if parameters.get("wissen"):
        cmd += ["--erase", "chip"]
    if pad.lower().endswith(".bin"):
        cmd += ["--base-address", str(parameters.get("adres") or "0x08000000")]
    cmd.append(pad)
    job.log("Schrijven …")
    if _hulp.volg_proces(cmd, _Log(job), 300, voortgang_regex=r"(\d+)%") != 0:
        raise RuntimeError("pyocd flash mislukt.")
    job.voortgang(90)

    uid = ""
    device = target
    adres = _uid_adres(device, parameters)
    if adres:
        v = _Log(job)
        _hulp.volg_proces(_hulp.werker_commando("pyocd", "cmd", *t, "-c", f"read32 0x{adres:08X} 12"), v, 60)
        woorden = [int(x, 16) for x in re.findall(r"\b([0-9a-fA-F]{8})\b", v.tekst)][1:4]
        if len(woorden) == 3:
            uid = _hex_uid(woorden)
    return {"uid": uid, "device": device, "motor": "pyocd"}


def voer_uit(job, inst: dict, parameters: dict) -> None:
    _hulp.ruim_werkmap_op()
    pad = _hulp.haal_firmware(parameters.get("firmware") or "", job, "firmware.hex")
    exe = zoek_cubeprog(inst)
    uit = _cubeprog(job, inst, exe, parameters, pad) if exe else _pyocd(job, inst, parameters, pad)
    job.log(f"Klaar. UID {uit['uid'] or 'niet gelezen'}")
    job.klaar(uit)
