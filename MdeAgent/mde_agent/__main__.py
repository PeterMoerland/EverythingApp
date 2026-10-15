"""
Ingang van de agent. Eén exe, drie rollen:

  MdeAgent.exe                      in de voorgrond draaien (console; Ctrl+C stopt)
  MdeAgent.exe --service ...        als Windows-service (install | remove | start | stop | debug)
  MdeAgent.exe --werker <taak> ...  hulpproces voor een taak (drives | esptool | pyocd)
  MdeAgent.exe --toon-token         het token laten zien (voor in appsettings van de app)
  MdeAgent.exe --zet-token <t>      het token instellen (daarna service herstarten)
"""

import json
import logging
import logging.handlers
import os
import sys

from . import VERSIE, instellingen


def _logging(naar_console: bool) -> None:
    os.makedirs(instellingen.LOGMAP, exist_ok=True)
    root = logging.getLogger()
    root.setLevel(logging.INFO)
    fmt = logging.Formatter("%(asctime)s %(levelname)s %(message)s")
    bestand = logging.handlers.RotatingFileHandler(
        os.path.join(instellingen.LOGMAP, "agent.log"), maxBytes=2_000_000, backupCount=5, encoding="utf-8")
    bestand.setFormatter(fmt)
    root.addHandler(bestand)
    if naar_console:
        console = logging.StreamHandler()
        console.setFormatter(fmt)
        root.addHandler(console)


def _werker(argv: list[str]) -> int:
    if not argv:
        print(json.dumps({"errors": ["geen taak"]}))
        return 2
    taak, rest = argv[0], argv[1:]
    if taak == "drives":
        from .taken import drives
        print(json.dumps(drives.werker(rest[0] if rest else ""), ensure_ascii=False))
        return 0
    if taak == "io":
        from .taken import drives
        masker = int(rest[2]) if len(rest) > 2 and rest[2] != "" else None
        print(json.dumps(drives.werker_io(rest[0], int(rest[1]), masker), ensure_ascii=False))
        return 0
    if taak == "coe":
        from .taken import coe
        print(json.dumps(coe.werker(rest[0], rest[1]), ensure_ascii=False))
        return 0
    if taak == "mde":
        from .taken import mde
        print(json.dumps(mde.werker(rest[0], rest[1]), ensure_ascii=False))
        return 0
    if taak == "ecatio":
        from .taken import ecat_io
        print(json.dumps(ecat_io.werker(rest[0], rest[1]), ensure_ascii=False))
        return 0
    if taak == "alias":
        from .taken import ecat_alias
        print(json.dumps(ecat_alias.werker(rest[0], rest[1]), ensure_ascii=False))
        return 0
    if taak == "esptool":
        from .taken import esp32
        return esp32.werker(*rest)
    if taak == "pyocd":
        from .taken import stm32
        return stm32.werker(*rest)
    print(json.dumps({"errors": [f"onbekende werker '{taak}'"]}))
    return 2


def main(argv: list[str] | None = None) -> int:
    argv = sys.argv[1:] if argv is None else argv

    if argv[:1] == ["--werker"]:
        return _werker(argv[1:])

    if argv[:1] == ["--toon-token"]:
        inst = instellingen.lees()
        print(inst.get("token") or "(geen token ingesteld)")
        return 0

    if argv[:1] == ["--zet-token"] and len(argv) >= 2:
        inst = instellingen.lees()
        inst["token"] = argv[1].strip()
        instellingen.bewaar(inst)
        print("Token opgeslagen; herstart de service (MdeAgent.exe --service restart).")
        return 0

    if argv[:1] == ["--versie"]:
        print(VERSIE)
        return 0

    if argv[:1] == ["--service"]:
        from . import service
        return service.main(argv[1:])

    _logging(naar_console=True)
    from . import server
    inst = instellingen.lees()
    print(f"MDE Werkplek-agent {VERSIE} — http://127.0.0.1:{inst.get('poort', 5055)}  (Ctrl+C stopt)")
    print(f"Instellingen: {instellingen.BESTAND}")
    server.draai()
    return 0


if __name__ == "__main__":
    sys.exit(main())
