"""
Instellingen van de agent. Staan in %ProgramData%\\MDE\\Agent\\agent.json
(door de installer aangemaakt), zodat ze een update van het programma overleven.
Ontbreekt het bestand, dan gelden de standaardwaarden en wordt het aangemaakt.
"""

import json
import os
import re
import secrets

MAP = os.path.join(os.environ.get("ProgramData", r"C:\ProgramData"), "MDE", "Agent")
BESTAND = os.path.join(MAP, "agent.json")
LOGMAP = os.path.join(MAP, "log")
WERKMAP = os.path.join(MAP, "werk")

STANDAARD = {
    "poort": 5055,
    "token": "",                       # gedeeld geheim met de app (Productie:AgentToken); leeg = geen controle
    "toegestaan": ["https://everything.mde-automation.nl", "http://172.16.1.13:5010"],
    "ethercat_interface": "",          # leeg = zoeken en onthouden
    "ethercat_interface_gevonden": "",
    "timeout_seconden": 90,
    "esptool_poort": "",               # leeg = automatisch zoeken (eerste USB-UART)
    "esptool_baud": 460800,
    "stm32_programmer_cli": "",        # pad naar STM32_Programmer_CLI.exe; leeg = zoeken / pyocd
    "drive_serials_script": "",        # map met read_drive_serials.py; leeg = ingebouwde versie
    "feetech_poort": "",               # COM-poort van de Feetech USB-adapter; leeg = automatisch zoeken
    "feetech_baud": 0,                 # 0 = gangbare baudrates proberen (1M, 115200, …)
    "drive_boot_in_app": None,         # ingeniamotion load_firmware_ecat; None = uit de bestandsextensie bepalen (aanbevolen)
    "gedeelde_werkplek": False,        # true = pc waar meerdere mensen aan werken: de app blokkeert dan Mail en Agenda
}


def _vergeef_backslashes(tekst: str) -> str:
    return re.sub(r'\\(?!["\\/bfnrtu])', r'\\\\', tekst)


def lees() -> dict:
    inst = dict(STANDAARD)
    if os.path.exists(BESTAND):
        with open(BESTAND, encoding="utf-8-sig") as f:
            tekst = f.read()
        try:
            inst.update(json.loads(tekst))
        except json.JSONDecodeError:
            inst.update(json.loads(_vergeef_backslashes(tekst)))
    else:
        inst["token"] = secrets.token_urlsafe(24)
        bewaar(inst)
    for m in (LOGMAP, WERKMAP):
        os.makedirs(m, exist_ok=True)
    return inst


def bewaar(inst: dict) -> None:
    os.makedirs(MAP, exist_ok=True)
    with open(BESTAND, "w", encoding="utf-8") as f:
        json.dump(inst, f, indent=2, ensure_ascii=False)
