"""
Taak drives.uitlezen: de EtherCAT-drives (Everest/Capitan) op de print
uitlezen: naam, vendor, productcode, revisie en serienummer.

Het eigenlijke lezen gebeurt in een apart proces (werker), zodat de
EtherCAT-master kort leeft en een vastloper in pysoem de agent niet
meeneemt. Staat er een read_drive_serials.py (uit de eerdere sessie), dan
gebruiken we diens scan_identity(); anders de ingebouwde versie hieronder.

Adapterkeuze: ingesteld > eerder gevonden > alle adapters afgaan tot er
drives antwoorden; de winnaar wordt onthouden.
"""

import json
import os
import subprocess
import sys

from .. import instellingen

NAAM = "drives.uitlezen"
OMSCHRIJVING = "EtherCAT-drives uitlezen (serienummers)"


NPCAP_UITLEG = ("Npcap ontbreekt op deze pc: installeer het van npcap.com met het vinkje "
                "'Install Npcap in WinPcap API-compatible Mode' en herstart daarna de agent-service.")


def npcap_aanwezig() -> bool:
    """Npcap (of het oude WinPcap): zonder kan pysoem geen netwerkkaart openen. Zelfde controle als installer.iss."""
    sys32 = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"), "System32")
    return os.path.isfile(os.path.join(sys32, "Npcap", "wpcap.dll")) or os.path.isfile(os.path.join(sys32, "wpcap.dll"))


def beschikbaar(inst: dict):
    if not npcap_aanwezig():
        return False, NPCAP_UITLEG
    try:
        import pysoem  # noqa: F401
    except Exception as e:  # noqa: BLE001
        return False, f"pysoem ontbreekt: {e}"
    return True, ""


def _tekst(x) -> str:
    """pysoem geeft naam/omschrijving soms als bytes; JSON wil str."""
    if isinstance(x, bytes):
        return x.decode("utf-8", "replace")
    return str(x) if x is not None else ""


def adapters() -> list[dict]:
    try:
        import pysoem
        return [{"name": _tekst(a.name), "desc": _tekst(a.desc)} for a in pysoem.find_adapters()]
    except Exception as e:  # noqa: BLE001
        return [{"name": "", "desc": f"pysoem niet beschikbaar: {e}"}]


# ---------------------------------------------------------------------------
#  Werker (apart proces)
# ---------------------------------------------------------------------------

def _ingebouwd_scan(ifname: str) -> list[dict]:
    """Zelfde gedrag als read_drive_serials.scan_identity, zonder dat bestand."""
    import pysoem
    master = pysoem.Master()
    master.open(ifname)
    try:
        if master.config_init() <= 0:
            return []
        uit = []
        for pos, slave in enumerate(master.slaves):
            rec = {"slave_position": pos, "name": slave.name,
                   "vendor_id": slave.man, "product_code": slave.id,
                   "revision_number": slave.rev, "serial_number": "", "errors": [],
                   "device_name": "", "hardware_version": "", "software_version": ""}
            # Tekstobjecten: eerst de Ingenia/Novanta Summit-registers (0x26E4 = firmware
            # van de motorkern, 0x5EE4 = firmware van de communicatiekern), dan de
            # standaard CiA-objecten als terugval. Niet elke drive vult ze allemaal.
            def tekst(index, sub=0):
                try:
                    return slave.sdo_read(index, sub).split(b"\0")[0].decode("ascii", "ignore").strip()
                except Exception:  # noqa: BLE001
                    return ""
            # Eigen MDE-slaves (vendor 0x10E3) kennen alleen de standaardobjecten; de Ingenia-registers
            # (0x26E4, 0x5EE4, 0x26E1, 0x26E6) zouden daar alleen SDO-aborts en foutmeldingen geven.
            eigen = slave.man == 0x000010E3
            if eigen:
                # De MDE-slave opent zijn mailbox pas in PRE-OP en is daar trager in dan een Ingenia-drive: wachten,
                # anders geeft elke SDO 'wkc 0' en blijven naam/firmware leeg. Komt hij er niet zelf, nog eens vragen.
                if slave.state_check(pysoem.PREOP_STATE, 3_000_000) != pysoem.PREOP_STATE:
                    slave.state = pysoem.PREOP_STATE
                    slave.write_state()
                    slave.state_check(pysoem.PREOP_STATE, 3_000_000)
            rec["device_name"] = tekst(0x1008)
            rec["hardware_version"] = tekst(0x1009)
            rec["software_version"] = tekst(0x100A) if eigen else (tekst(0x26E4) or tekst(0x100A))
            rec["coco_software_version"] = "" if eigen else tekst(0x5EE4)
            rec["ingenia_product_code"] = None
            if not eigen:
                try:  # productcode volgens de drive zelf (kan afwijken van de EEPROM)
                    rec["ingenia_product_code"] = int.from_bytes(slave.sdo_read(0x26E1, 0)[:4], "little")
                except Exception:  # noqa: BLE001
                    pass
            for index, sub, naam in (((0x1018, 4, "0x1018:4"),) if eigen else ((0x1018, 4, "0x1018:4"), (0x26E6, 0, "0x26E6"))):
                try:
                    ruw = slave.sdo_read(index, sub)
                    if len(ruw) >= 4 and index == 0x1018:
                        rec["serial_number"] = str(int.from_bytes(ruw[:4], "little"))
                    else:
                        tekst = ruw.split(b"\0")[0].decode("ascii", "ignore").strip()
                        rec["serial_number"] = tekst or str(int.from_bytes(ruw[:4], "little"))
                    if rec["serial_number"] not in ("", "0"):
                        break
                except Exception as e:  # noqa: BLE001
                    rec["errors"].append(f"{naam}: {e}")
            if eigen and rec["serial_number"] in ("", "0"):
                # MDE-slave: het serienummer staat in de EEPROM (SII 0x0E, door mde.serienummer gezet), ook als
                # de firmware het (nog) niet in 0x1018:4 zet
                try:
                    sii = int.from_bytes(bytes(slave.eeprom_read(0x000E))[:4], "little")
                    if sii not in (0, 0xFFFFFFFF):
                        rec["serial_number"] = str(sii)
                except Exception:  # noqa: BLE001
                    pass
            if rec["serial_number"] not in ("", "0"):
                rec["errors"] = []   # de fallback lukte, eerdere poging is niet relevant
            uit.append(rec)
        return uit
    finally:
        master.close()


def _io_scan(ifname: str, positie: int, masker: int | None) -> dict:
    """Digitale uitgangen van één drive zetten (0x2602) en/of teruglezen (0x2601)."""
    import struct
    import time as _t
    import pysoem
    master = pysoem.Master()
    master.open(ifname)
    try:
        n = master.config_init()
        if n < positie:
            return {"errors": [f"{n} drive(s) op de bus, positie {positie} bestaat niet."]}
        slave = master.slaves[positie - 1]
        if masker is not None:
            slave.sdo_write(0x2602, 0, struct.pack("<H", masker & 0xFFFF))
            _t.sleep(0.05)
        gelezen = struct.unpack("<H", slave.sdo_read(0x2601, 0)[:2])[0]
        # digitale ingangen (0x2600): op de testrig zijn uitgang n en ingang n doorverbonden
        ingangen = None
        try:
            ruw = slave.sdo_read(0x2600, 0)
            ingangen = int.from_bytes(ruw[:4], "little") if len(ruw) >= 4 else struct.unpack("<H", ruw[:2])[0]
        except Exception:  # noqa: BLE001
            pass
        return {"positie": positie, "gezet": masker, "gelezen": gelezen, "ingangen": ingangen, "errors": []}
    finally:
        master.close()


def werker_io(ifname: str, positie: int, masker: int | None) -> dict:
    try:
        return _io_scan(ifname, positie, masker)
    except Exception as e:  # noqa: BLE001
        return {"errors": [f"IO mislukt: {e}"]}


def zet_uitgangen(inst: dict, positie: int, masker: int | None, log=print) -> dict:
    """Vanuit de agent: uitgangen zetten/teruglezen in een apart proces (zoals lees)."""
    ifname = inst.get("ethercat_interface_gevonden") or inst.get("ethercat_interface") or ""
    if not ifname:
        return {"errors": ["Geen EtherCAT-adapter bekend; lees eerst de drives uit."]}
    cmd = [sys.executable] + ([] if getattr(sys, "frozen", False) else ["-m", "mde_agent"]) \
          + ["--werker", "io", ifname, str(positie), "" if masker is None else str(masker)]
    try:
        uit = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", timeout=30,
                             creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        return json.loads(uit.stdout.strip().splitlines()[-1])
    except subprocess.TimeoutExpired:
        return {"errors": ["IO-werker: time-out."]}
    except Exception as e:  # noqa: BLE001
        return {"errors": [f"IO-werker gaf geen JSON: {e}"]}


def werker(ifname: str) -> dict:
    """Wordt aangeroepen met `MdeAgent --werker drives <ifname>`; drukt JSON af."""
    inst = instellingen.lees()
    try:
        extern = inst.get("drive_serials_script") or ""
        if extern and os.path.isdir(extern):
            sys.path.insert(0, extern)
            from read_drive_serials import scan_identity  # type: ignore
            from dataclasses import asdict, is_dataclass
            records = [asdict(r) if is_dataclass(r) else dict(vars(r)) for r in scan_identity(ifname)]
        else:
            records = _ingebouwd_scan(ifname)
    except Exception as e:  # noqa: BLE001
        return {"drives": [], "errors": [f"Uitlezen mislukt: {e}"], "interface": ifname}

    for d in records:
        for veld in ("vendor_id", "product_code", "revision_number", "ingenia_product_code"):
            if isinstance(d.get(veld), int):
                d[veld] = f"0x{d[veld]:08X}"
        if d.get("serial_number") is not None and not isinstance(d["serial_number"], str):
            d["serial_number"] = _tekst(d["serial_number"])
        d["name"] = _tekst(d.get("name"))
        for veld in ("device_name", "hardware_version", "software_version", "coco_software_version"):
            d[veld] = _tekst(d.get(veld))
        d["errors"] = [_tekst(e) for e in (d.get("errors") or [])]
    return {"drives": records, "errors": [], "interface": ifname}


# ---------------------------------------------------------------------------
#  Aansturing vanuit de agent
# ---------------------------------------------------------------------------

def _scan_proces(inst: dict, ifname: str, log) -> dict:
    cmd = [sys.executable] + ([] if getattr(sys, "frozen", False) else ["-m", "mde_agent"]) \
          + ["--werker", "drives", ifname]
    try:
        uit = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8",
                             timeout=int(inst.get("timeout_seconden", 90)),
                             creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        try:
            return json.loads(uit.stdout.strip().splitlines()[-1])
        except Exception:  # noqa: BLE001
            return {"drives": [], "interface": ifname,
                    "errors": ["Werker gaf geen JSON terug: " + (uit.stderr or uit.stdout)[-800:]]}
    except subprocess.TimeoutExpired:
        return {"drives": [], "interface": ifname, "errors": ["Uitlezen duurde te lang (time-out)."]}


def lees(inst: dict, ifname_gevraagd: str = "", log=print) -> dict:
    """Synchroon uitlezen; geeft het antwoord-dict terug (ook voor GET /drives)."""
    ifname = ifname_gevraagd or inst.get("ethercat_interface_gevonden") or inst.get("ethercat_interface") or ""
    if ifname:
        inhoud = _scan_proces(inst, ifname, log)
        if inhoud["drives"] or ifname_gevraagd:
            inhoud["adapter"] = next((a["desc"] for a in adapters() if a["name"] == ifname), ifname)
            return inhoud
        log(f"Geen drives op {ifname}; alle adapters proberen.")

    if not npcap_aanwezig():
        return {"drives": [], "interface": "", "errors": [NPCAP_UITLEG]}

    geprobeerd = []
    for a in adapters():
        if not a["name"] or a["name"] == ifname:
            continue
        log(f"Zoeken op {a['desc']} …")
        inhoud = _scan_proces(inst, a["name"], log)
        geprobeerd.append({"interface": a["name"], "desc": a["desc"],
                           "drives": len(inhoud["drives"]), "errors": inhoud["errors"]})
        if inhoud["drives"]:
            inhoud["gezocht"] = geprobeerd
            inhoud["adapter"] = a["desc"]
            if inst.get("ethercat_interface_gevonden") != a["name"]:
                inst["ethercat_interface_gevonden"] = a["name"]
                instellingen.bewaar(inst)
                log(f"Adapter onthouden: {a['desc']}")
            return inhoud
    if not geprobeerd:
        return {"drives": [], "interface": "", "gezocht": geprobeerd,
                "errors": ["Geen netwerkadapters gevonden (Npcap ziet er geen). Herstart de agent-service; helpt dat "
                           "niet, installeer Npcap opnieuw met 'WinPcap API-compatible Mode'."]}
    return {"drives": [], "interface": "", "gezocht": geprobeerd,
            "errors": ["Op geen enkele netwerkadapter antwoorden EtherCAT-drives "
                       f"(geprobeerd: {', '.join(g['desc'] or g['interface'] for g in geprobeerd)}). "
                       "Zit de kabel in de goede netwerkpoort, staat de print onder spanning en is MotionLab3 dicht?"]}


def voer_uit(job, inst: dict, parameters: dict) -> None:
    uit = lees(inst, parameters.get("ifname", ""), job.log)
    for e in uit.get("errors", []):
        job.log(e)
    job.log(f"{len(uit['drives'])} drive(s) gevonden.")
    job.klaar(uit)
