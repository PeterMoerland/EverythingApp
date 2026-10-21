"""
Taak ecat.alias: de EtherCAT node-adressen (Configured Station Alias) van alle slaves op de
bus lezen en schrijven — het 'Write to slaves' van TwinCAT.

Het alias staat in de EEPROM van de ESC op woordadres 0x0004 (met een CRC-8 over woord 0..6
in woord 7) en wordt bij het opstarten in register 0x0012 geladen; dat register is voor de
master alleen leesbaar. Het nieuwe adres geldt dus na spanning uit/aan — net als bij Sysmac/TwinCAT.

Parameters:
    wat        "lees" (standaard) of "schrijf"
    adressen   bij schrijf: {"1": 1001, "2": 1002, ...}  positie (1-based) → nieuw alias (0..65535)
    ifname     EtherCAT-adapter (leeg = de onthouden adapter)

Resultaat: {"slaves": [{"positie": 1, "naam": "eRob", "product_code": "0x…", "alias": 1001,
             "alias_eeprom": 1001, "alias_register": 1001, "geschreven": true, "melding": ""}], "errors": []}
"""

import json
import os
import subprocess
import sys
import tempfile
import time

from .. import instellingen

NAAM = "ecat.alias"
OMSCHRIJVING = "EtherCAT node-adressen (station alias) lezen en schrijven"

EEPROM_ALIAS = 0x0004     # woordadres 'Configured Station Alias'
REG_ALIAS = 0x0012        # ESC-register (2 bytes)


def beschikbaar(inst: dict):
    try:
        import pysoem  # noqa: F401
    except Exception as e:  # noqa: BLE001
        return False, f"pysoem ontbreekt: {e}"
    return True, ""


def _tekst(x) -> str:
    return x.decode("utf-8", "replace") if isinstance(x, bytes) else (str(x) if x is not None else "")


# ---------------------------------------------------------------------------
#  Werker (apart proces): MdeAgent --werker alias <ifname> @opdracht.json
# ---------------------------------------------------------------------------

def _lees_eeprom(slave) -> int | None:
    try:
        ruw = slave.eeprom_read(EEPROM_ALIAS)
        return int.from_bytes(ruw[:2], "little")
    except Exception:  # noqa: BLE001
        return None


def _crc8(data: bytes) -> int:
    """SII-checksum: CRC-8, polynoom x^8+x^2+x+1 (0x07), startwaarde 0xFF, over woord 0..6 (14 bytes)."""
    crc = 0xFF
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = ((crc << 1) ^ 0x07) & 0xFF if crc & 0x80 else (crc << 1) & 0xFF
    return crc


def _lees_explicit_id(slave) -> int | None:
    """Explicit Device Identification (ETG.1020): bit 5 in AL Control zetten, dan staat het ID in
    AL Status Code 0x0134. Ingenia-drives en Omron gebruiken dit als het node-adres niet via het alias gaat."""
    try:
        ctrl = int.from_bytes(bytes(slave._fprd(0x0120, 2))[:2], "little")
        slave._fpwr(0x0120, ((ctrl & 0x0F) | 0x20).to_bytes(2, "little"))
        uit = None
        for _ in range(20):
            time.sleep(0.01)
            st = int.from_bytes(bytes(slave._fprd(0x0130, 2))[:2], "little")
            if st & 0x20:
                uit = int.from_bytes(bytes(slave._fprd(0x0134, 2))[:2], "little")
                break
        slave._fpwr(0x0120, (ctrl & 0x0F).to_bytes(2, "little"))
        return uit
    except Exception:  # noqa: BLE001
        return None


def _lees_kop(slave) -> bytes:
    """EEPROM-woorden 0..7 (16 bytes): PDI-configuratie, alias en de checksum in woord 7."""
    kop = b""
    for w in (0, 2, 4, 6):
        kop += bytes(slave.eeprom_read(w))[:4]
    return kop


def _eeprom_status(slave) -> int | None:
    """ESC-register 0x0502 (EEPROM control/status): bit 15 bezig, 13 commando-/ack-fout, 14 write-enable-fout,
    11 checksum-fout, 12 laadfout, 6 EEPROM wordt nagebootst door de processor van de slave (emulatie)."""
    try:
        return int.from_bytes(bytes(slave._fprd(0x0502, 2))[:2], "little")
    except Exception:  # noqa: BLE001
        return None


def _wacht_eeprom(slave, seconden: float = 1.0) -> None:
    """Wachten tot de EEPROM klaar is met schrijven (bit 15 'bezig' laag); een vaste pauze was te krap."""
    einde = time.time() + seconden
    while time.time() < einde:
        st = _eeprom_status(slave)
        if st is not None and not st & 0x8000:
            return
        time.sleep(0.01)


def _lees_woord(slave, adres: int, pogingen: int = 3) -> bytes | None:
    for _ in range(pogingen):
        try:
            return bytes(slave.eeprom_read(adres))[:2]
        except Exception:  # noqa: BLE001
            time.sleep(0.05)
    return None


def _schrijf_alias(slave, nieuw: int, log) -> None:
    """Woord 4 (alias) en de checksum in woord 7 schrijven — veilig:

    De checksum gaat er pas in als het alias goed teruggelezen is. Andersom (eerst checksum, dan pas controleren)
    maakte op 21-10-2026 Everest-drives onbereikbaar: alias kwam verkeerd in de EEPROM (0 of 128 i.p.v. 31/33),
    de checksum paste bij het bedoelde alias, en na spanning uit/aan negeerde de ESC de hele EEPROM-kop (PDI niet
    actief: geen mailbox, MotionLab en FoE zien de drive niet). Lukt het alias niet, dan wordt het oude teruggezet
    en blijft de checksum ongemoeid."""
    st = _eeprom_status(slave)
    if st is not None and st & 0x8000:
        _wacht_eeprom(slave)
        st = _eeprom_status(slave)
    if st is not None and st & 0x8000:
        raise RuntimeError("EEPROM blijft bezig; is er nog een ander EtherCAT-programma actief (MotionLab, TwinCAT)? Niets geschreven.")
    kop = bytearray(_lees_kop(slave))
    oud_alias = bytes(kop[8:10])
    checksum_ok = _crc8(bytes(kop[:14])) == kop[14]
    if not checksum_ok:
        log(f"  let op: de huidige EEPROM-checksum klopt niet (0x{kop[14]:02X}, verwacht 0x{_crc8(bytes(kop[:14])):02X}); wordt nu hersteld")
    if st is not None and st & 0x0040:
        log("  (EEPROM wordt door de slave zelf nagebootst)")
    kop[8:10] = nieuw.to_bytes(2, "little")
    crc = _crc8(bytes(kop[:14]))

    # 1. het alias, en controleren voordat er iets aan de checksum gebeurt
    if bytes(kop[8:10]) != oud_alias:
        slave.eeprom_write(EEPROM_ALIAS, bytes(kop[8:10]))
        _wacht_eeprom(slave)
        terug = _lees_woord(slave, EEPROM_ALIAS)
        if terug != bytes(kop[8:10]):
            gelezen = "niets" if terug is None else str(int.from_bytes(terug, "little"))
            hersteld = False
            try:
                slave.eeprom_write(EEPROM_ALIAS, oud_alias)
                _wacht_eeprom(slave)
                hersteld = _lees_woord(slave, EEPROM_ALIAS) == oud_alias
            except Exception:  # noqa: BLE001
                pass
            if hersteld:
                raise RuntimeError(f"EEPROM leest {gelezen} terug in plaats van {nieuw}; het oude nummer "
                                   f"{int.from_bytes(oud_alias, 'little')} is teruggezet en de checksum is niet aangeraakt.")
            raise RuntimeError(f"EEPROM leest {gelezen} terug in plaats van {nieuw} en het oude nummer terugzetten lukte niet. "
                               "NIET de spanning eraf halen: sluit andere EtherCAT-programma's en schrijf het node-adres opnieuw, "
                               "anders kan de drive na spanning uit/aan onbereikbaar zijn.")

    # 2. pas nu de checksum (ook als alleen die niet klopte)
    if kop[14] != crc or not checksum_ok:
        for poging in range(2):
            slave.eeprom_write(0x0007, bytes([crc, kop[15]]))
            _wacht_eeprom(slave)
            if (_lees_woord(slave, 0x0007) or b"\0")[0] == crc:
                break
        else:
            raise RuntimeError(f"checksum niet geschreven (verwacht 0x{crc:02X}). NIET de spanning eraf halen: sluit andere "
                               "EtherCAT-programma's en schrijf het node-adres opnieuw, anders kan de drive na spanning uit/aan "
                               "onbereikbaar zijn.")
    terug = bytearray(_lees_kop(slave))
    if terug[8:10] != kop[8:10] or _crc8(bytes(terug[:14])) != terug[14]:
        raise RuntimeError(f"controle na schrijven klopt niet (EEPROM woord 0..7: {terug.hex(' ')}). NIET de spanning eraf halen; "
                           "schrijf het node-adres opnieuw.")
    log(f"  EEPROM: node {nieuw}, checksum 0x{crc:02X} (gecontroleerd)")


def _lees_register(slave) -> int | None:
    try:
        ruw = slave._fprd(REG_ALIAS, 2)
        return int.from_bytes(bytes(ruw)[:2], "little")
    except Exception:  # noqa: BLE001
        return None


def werker(ifname: str, opdracht_json: str) -> dict:
    import pysoem
    if opdracht_json.startswith("@"):
        with open(opdracht_json[1:], encoding="utf-8") as f:
            o = json.load(f)
    else:
        o = json.loads(opdracht_json or "{}")
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
        return {"slaves": [], "errors": [f"EtherCAT-adapter '{ifname}' openen mislukt: {e}"]}
    try:
        n = master.config_init()
        if n <= 0:
            return {"slaves": [], "errors": ["Geen EtherCAT-slaves gevonden (kabel, spanning, andere master actief?)."]}
        wat = o.get("wat") or "lees"
        adressen = {int(k): int(v) for k, v in (o.get("adressen") or {}).items()}
        uit = []
        fouten = []
        for i, slave in enumerate(master.slaves):
            pos = i + 1
            rec = {"positie": pos, "naam": _tekst(slave.name), "product_code": f"0x{slave.id:08X}",
                   "vendor_id": f"0x{slave.man:08X}", "geschreven": False, "melding": ""}
            rec["alias_eeprom"] = _lees_eeprom(slave)
            rec["alias_register"] = _lees_register(slave)
            rec["explicit_id"] = _lees_explicit_id(slave)
            # diagnose: klopt de registerweg (0x0010 = station address, door de master gezet, dus ≠ 0)
            # en heeft de ESC de EEPROM foutloos geladen (0x0502 EEPROM-status: bit 11 checksum error,
            # bit 12 loading error, bit 13 acknowledge/command error)?
            try:
                rec["station_adres"] = int.from_bytes(bytes(slave._fprd(0x0010, 2))[:2], "little")
                st = int.from_bytes(bytes(slave._fprd(0x0502, 2))[:2], "little")
                rec["esc_status"] = f"0x{st:04X}"
                rec["eeprom_laadfout"] = bool(st & 0x1800)
            except Exception as e:  # noqa: BLE001
                rec["station_adres"] = None
                rec["esc_status"] = f"FPRD mislukt: {e}"
            try:
                kop = _lees_kop(slave)
                rec["eeprom_kop"] = " ".join(f"{int.from_bytes(kop[i:i + 2], 'little'):04X}" for i in range(0, 16, 2))   # woord 0..7
                rec["checksum_ok"] = _crc8(kop[:14]) == kop[14]
                if not rec["checksum_ok"] and wat != "schrijf":
                    rec["melding"] = "EEPROM-checksum klopt niet: het node-nummer wordt bij opstarten genegeerd — opnieuw schrijven herstelt dit"
            except Exception:  # noqa: BLE001
                rec["checksum_ok"] = None
            rec["alias"] = rec["alias_register"] if rec["alias_register"] is not None else rec["alias_eeprom"]
            if not rec["alias"] and rec["explicit_id"]:
                rec["alias"] = rec["explicit_id"]          # node-adres komt van de Explicit Device ID
                rec["melding"] = f"Explicit Device ID {rec['explicit_id']} (geen station alias; wordt in de drive zelf ingesteld)"
            if wat == "schrijf" and pos in adressen:
                nieuw = adressen[pos]
                if not 0 <= nieuw <= 0xFFFF:
                    rec["melding"] = f"alias {nieuw} buiten bereik 0..65535"
                    fouten.append(f"positie {pos}: {rec['melding']}")
                else:
                    log(f"Positie {pos} ({rec['naam']}): node {rec['alias']} → {nieuw} …")
                    try:
                        _schrijf_alias(slave, nieuw, log)
                        rec["alias_eeprom"] = nieuw
                        rec["geschreven"] = True
                        # Register 0x0012 is voor de master alleen leesbaar (de ESC laadt het uit de EEPROM
                        # bij het opstarten), dus het nieuwe nummer geldt na spanning uit/aan.
                        rec["melding"] = "in EEPROM geschreven; geldt na spanning uit/aan"
                        rec["alias"] = nieuw
                        log(f"  ok — {rec['melding']}")
                    except Exception as e:  # noqa: BLE001
                        rec["melding"] = f"schrijven mislukt: {e}"
                        fouten.append(f"positie {pos}: {rec['melding']}")
                        log(f"  FOUT: {e}")
            if rec["geschreven"]:
                try:
                    kop = _lees_kop(slave)
                    rec["eeprom_kop"] = " ".join(f"{int.from_bytes(kop[i:i + 2], 'little'):04X}" for i in range(0, 16, 2))
                    rec["checksum_ok"] = _crc8(kop[:14]) == kop[14]
                except Exception:  # noqa: BLE001
                    pass
            uit.append(rec)
        return {"slaves": uit, "errors": fouten, "interface": ifname}
    except Exception as e:  # noqa: BLE001
        return {"slaves": [], "errors": [f"Mislukt: {e}"]}
    finally:
        master.close()


# ---------------------------------------------------------------------------
#  Aansturing vanuit de agent
# ---------------------------------------------------------------------------

def _draai(inst: dict, job, opdracht: dict, ifname: str, timeout: int = 60) -> dict:
    fd, pad = tempfile.mkstemp(prefix="mde-alias-", suffix=".json")
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        json.dump(opdracht, f)
    cmd = [sys.executable] + ([] if getattr(sys, "frozen", False) else ["-m", "mde_agent"]) \
          + ["--werker", "alias", ifname, "@" + pad]
    laatste = None
    try:
        proces = subprocess.Popen(cmd, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                  text=True, encoding="utf-8", errors="replace", bufsize=1,
                                  creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        begin = time.time()
        try:
            assert proces.stdout is not None
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
                else:
                    laatste = obj
                if time.time() - begin > timeout:
                    proces.kill()
                    raise RuntimeError(f"Werker antwoordde niet binnen {timeout} s.")
            proces.wait(timeout=10)
        finally:
            if proces.poll() is None:
                proces.kill()
    finally:
        try:
            os.remove(pad)
        except OSError:
            pass
    if not isinstance(laatste, dict):
        raise RuntimeError("Werker gaf geen resultaat terug.")
    return laatste


def voer_uit(job, inst: dict, parameters: dict) -> None:
    from . import drives
    ifname = parameters.get("ifname") or inst.get("ethercat_interface_gevonden") or inst.get("ethercat_interface") or ""
    wat = parameters.get("wat") or "lees"
    if not ifname:
        # nog geen adapter bekend: via de gewone drive-scan laten zoeken (die onthoudt hem)
        job.log("Geen EtherCAT-adapter bekend; adapters afzoeken …")
        drives.lees(inst, "", job.log)
        ifname = inst.get("ethercat_interface_gevonden") or ""
        if not ifname:
            raise RuntimeError("Geen EtherCAT-adapter met slaves gevonden.")
    job.log(f"{'Node-adressen schrijven' if wat == 'schrijf' else 'Node-adressen lezen'} op {ifname} …")
    uit = _draai(inst, job, {"wat": wat, "adressen": parameters.get("adressen") or {}}, ifname)
    for e in uit.get("errors", []):
        job.log("FOUT: " + e)
    for s in uit.get("slaves", []):
        job.log(f"  positie {s['positie']}: station 0x{(s.get('station_adres') or 0):04X}, EEPROM-status {s.get('esc_status')}, "
                f"EEPROM-laadfout {s.get('eeprom_laadfout')}, checksum ok {s.get('checksum_ok')}, explicit ID {s.get('explicit_id')}, "
                f"EEPROM woord 0..7: {s.get('eeprom_kop')}")
        job.log(f"  positie {s['positie']}: {s['naam']} — node {s.get('alias')}"
                + (f" (EEPROM {s.get('alias_eeprom')}, register {s.get('alias_register')})" if s.get('alias_eeprom') != s.get('alias_register') else "")
                + (f" — {s['melding']}" if s.get("melding") else ""))
    if wat == "schrijf" and uit.get("errors"):
        job.mislukt("; ".join(uit["errors"]))
        return
    job.klaar(uit)
