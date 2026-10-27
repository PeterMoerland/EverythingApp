"""
Gedeelde toegang tot Ingenia/Novanta-drives via ingeniamotion (de bibliotheek
onder MotionLab3). Verbindt over EtherCAT met één slave (positie 1 = eerste
drive op de bus) aan de hand van de dictionary (.xdf) die bij de firmware hoort.
"""

import contextlib
import xml.etree.ElementTree as ET

from .. import instellingen


_beschikbaar_cache: tuple[bool, str] | None = None


def beschikbaar():
    """Importeren van ingeniamotion duurt (numpy/scipy); één keer doen en onthouden."""
    global _beschikbaar_cache
    if _beschikbaar_cache is None:
        try:
            import ingeniamotion  # noqa: F401
            _beschikbaar_cache = (True, "")
        except Exception as e:  # noqa: BLE001
            _beschikbaar_cache = (False, f"ingeniamotion ontbreekt: {e}")
    return _beschikbaar_cache


def gil_config():
    """
    Laat ingenialink de GIL loslaten tijdens EtherCAT-verkeer. Zonder dit houdt een
    FoE-overdracht of config_init de hele Python-interpreter vast en kan de
    webserver van de agent ondertussen niet antwoorden (time-outs in de app).
    """
    try:
        from ingenialink.ethercat.network import GilReleaseConfig
        return GilReleaseConfig(config_init=True, sdo_read_write=True,
                                foe_read_write=True, send_receive_processdata=True)
    except Exception:  # noqa: BLE001
        return None


def adapter(inst: dict) -> str:
    """De EtherCAT-adapter: ingesteld of eerder gevonden (door drives.uitlezen)."""
    naam = inst.get("ethercat_interface") or inst.get("ethercat_interface_gevonden") or ""
    if not naam:
        raise RuntimeError("Geen EtherCAT-adapter bekend. Lees eerst een keer de drives uit (Automatisch uitlezen).")
    return naam


@contextlib.contextmanager
def verbinding(inst: dict, positie: int, dictionary_pad: str, job):
    """Context: verbonden MotionController voor de drive op 'positie' (1 = eerste slave)."""
    from ingeniamotion import MotionController

    mc = MotionController()
    ifname = adapter(inst)
    job.log(f"Verbinden met drive {positie} via {ifname} …")
    gil = gil_config()
    if gil is not None:
        mc.communication.connect_servo_ethercat(ifname, positie, dictionary_pad, gil_release_config=gil)
    else:
        mc.communication.connect_servo_ethercat(ifname, positie, dictionary_pad)
    try:
        yield mc
    finally:
        with contextlib.suppress(Exception):
            mc.communication.disconnect()
        job.log("Verbinding gesloten.")


@contextlib.contextmanager
def verbinding_meerdere(inst: dict, drives: list[tuple[str, int, str]], job):
    """Context: één MotionController verbonden met meerdere drives op dezelfde EtherCAT-adapter.
    drives = [(alias, positie, dictionary_pad), ...]; aanspreken met servo=alias."""
    from ingeniamotion import MotionController

    mc = MotionController()
    ifname = adapter(inst)
    gil = gil_config()
    verbonden = []
    try:
        for alias, positie, pad in drives:
            job.log(f"Verbinden met drive {positie} ('{alias}') via {ifname} …")
            if gil is not None:
                mc.communication.connect_servo_ethercat(ifname, positie, pad, alias=alias, gil_release_config=gil)
            else:
                mc.communication.connect_servo_ethercat(ifname, positie, pad, alias=alias)
            verbonden.append(alias)
        yield mc
    finally:
        for alias in reversed(verbonden):
            with contextlib.suppress(Exception):
                mc.communication.disconnect(servo=alias)
        job.log("Verbindingen gesloten.")


def lees_xcf(pad: str) -> list[dict]:
    """De registers uit een MotionLab-configuratie (.xcf): id, subnode, dtype, storage."""
    boom = ET.parse(pad)
    uit = []
    for reg in boom.getroot().iter("Register"):
        if reg.get("access", "rw") != "rw":
            continue
        uit.append({
            "id": reg.get("id"),
            "subnode": int(reg.get("subnode", "1")),
            "dtype": reg.get("dtype", ""),
            "storage": reg.get("storage"),
        })
    return uit


def xcf_kop(pad: str) -> dict:
    dev = ET.parse(pad).getroot().find(".//Device")
    return {} if dev is None else dict(dev.attrib)


def gelijk(dtype: str, verwacht: str | None, gelezen) -> bool:
    """Vergelijking met een beetje tolerantie voor floats."""
    if verwacht is None:
        return True
    try:
        if dtype == "float":
            a, b = float(verwacht), float(gelezen)
            return abs(a - b) <= max(1e-6, abs(a) * 1e-5)
        if dtype.startswith(("u", "s")):
            return int(float(verwacht)) == int(gelezen)
        return str(verwacht).strip() == str(gelezen).strip()
    except Exception:  # noqa: BLE001
        return str(verwacht).strip() == str(gelezen).strip()


def sto_vrij(mc, job, servo: str = "default") -> str | None:
    """STO-controle vóór een test die de motor inschakelt. None = vrijgegeven; anders de melding
    voor de operator (de app toont die als popup en breekt de test af).

    DRV_PROT_STO_STATUS (Summit): bit0 = STO1-ingang hoog (1 = vrijgegeven), bit1 = STO2-ingang hoog,
    bit2 = STO-voeding aanwezig, bit3 = abnormale fout, bit4 = report. Vrijgegeven = 0x17, STO actief = 0x04."""
    try:
        sto = int(mc.configuration.get_sto_status(servo=servo))
        sto1 = bool(sto & 0x1)
        sto2 = bool(sto & 0x2)
        voeding = bool(sto & 0x4)
        abnormaal = bool(sto & 0x8)
        job.log(f"STO-status{'' if servo == 'default' else ' ' + servo} 0x{sto:02X}: STO1 {'vrij' if sto1 else 'actief'}, STO2 {'vrij' if sto2 else 'actief'}, "
                f"voeding {'ok' if voeding else 'ontbreekt'}{', abnormale fout' if abnormaal else ''}")
        if not (sto1 and sto2 and voeding) or abnormaal:
            return ("STO niet vrijgegeven: sluit de STO-stekker/veiligheidskring aan op de testrig "
                    f"(STO1 {'vrij' if sto1 else 'actief'}, STO2 {'vrij' if sto2 else 'actief'}, "
                    f"voeding {'ok' if voeding else 'ontbreekt'}{', abnormale fout' if abnormaal else ''}; status 0x{sto:02X})")
    except Exception as e:  # noqa: BLE001
        job.log(f"  STO-status niet te lezen ({e}); test gaat door")
    return None


def getal(p: dict, naam: str, standaard: float) -> float:
    try:
        v = p.get(naam)
        return float(v) if v not in (None, "") else standaard
    except Exception:  # noqa: BLE001
        return standaard


def motor_aan(mc, job, pogingen: int = 2, servo: str = "default") -> None:
    """Fault reset (zo nodig twee keer) en dan inschakelen; bij een blijvende fout een duidelijke
    melding in plaats van een traceback (bv. 'Absolute encoder 1 disconnection')."""
    import time as _t
    laatste = None
    for poging in range(pogingen):
        try:
            mc.motion.fault_reset(servo=servo)
            _t.sleep(0.4)
        except Exception as e:  # noqa: BLE001
            job.log(f"  fault reset: {e}")
        try:
            mc.motion.motor_enable(servo=servo)
            return
        except Exception as e:  # noqa: BLE001
            laatste = e
            tekst = str(e).replace("\n", " ")
            job.log(f"  inschakelen mislukt (poging {poging + 1}): {tekst[:300]}")
            _t.sleep(0.6)
    code = laatste_fout(mc, job, servo)
    raise RuntimeError(f"Drive{'' if servo == 'default' else ' ' + servo} laat zich niet inschakelen — staat in FOUT: " + str(laatste).split("Error message:")[-1].strip()
                       + (f" [{code}]" if code else "")
                       + ". Los de oorzaak op (encoder/bedrading/voeding), eventueel spanning uit/aan, en start de test opnieuw.")


def laatste_fout(mc, job, servo: str = "default") -> str:
    """Laatste foutcode uit de drive als leesbare tekst ('0x3241 Absolute encoder 1 disconnection'), of ''."""
    try:
        uit = mc.errors.get_last_error(servo=servo)
        code = None
        if isinstance(uit, tuple):                      # ingeniamotion ≥ 0.9: (code, axis, is_warning)
            code = int(uit[0]) if uit and uit[0] is not None else None
        elif uit is not None:                           # oudere versies: Error-object
            code = int(getattr(uit, "error_code", 0) or 0)
        if not code:
            return ""
        tekst = f"0x{code:X}"
        try:
            data = mc.errors.get_error_data(code, servo=servo)
            if data and len(data) >= 4 and data[3]:
                tekst += f" {data[3]}"
        except Exception:  # noqa: BLE001
            pass
        job.log(f"  laatste drive-fout: {tekst}")
        return tekst
    except Exception:  # noqa: BLE001
        try:
            code = int(mc.communication.get_register("DRV_DIAG_ERROR_LAST", servo=servo))
            if code:
                job.log(f"  laatste drive-fout: 0x{code:X}")
                return f"0x{code:X}"
        except Exception:  # noqa: BLE001
            pass
    return ""


# Aantal SDO-herhalingen dat nodig was (teller voor de log: bewijs van bus-/EMI-problemen).
sdo_herhalingen = 0


def sdo(fn, *args, pogingen: int = 4, pauze: float = 0.08, job=None, **kwargs):
    """Een SDO-actie (lezen/schrijven/enable) met herhaling bij 'working counter unchanged' — een
    verloren of beschadigd EtherCAT-frame (EMI tijdens hoge motorstroom) is anders meteen fataal."""
    import time as _t
    from ..jobs import tel_comm
    global sdo_herhalingen
    laatste = None
    drive = str(kwargs.get("servo") or "")
    for poging in range(pogingen):
        try:
            return fn(*args, **kwargs)
        except Exception as e:  # noqa: BLE001
            tekst = str(e)
            if "working counter" not in tekst.lower() and "wkc" not in tekst.lower() and "timeout" not in tekst.lower():
                raise
            laatste = e
            if poging == pogingen - 1:
                break
            sdo_herhalingen += 1
            tel_comm("herhaald", drive, f"{getattr(fn, '__name__', 'sdo')}: {tekst}")
            if job is not None and poging == 0:
                job.log(f"  SDO herhaald ({getattr(fn, '__name__', 'sdo')}): bus antwoordde niet — EMI/verbinding?")
            _t.sleep(pauze * (poging + 1))
    tel_comm("mislukt", drive, f"{getattr(fn, '__name__', 'sdo')}: {laatste}")
    raise RuntimeError(f"Drive antwoordt niet meer op de EtherCAT-bus na {pogingen} pogingen: {laatste}")


def uid_voor(mc, idx: int, subidx: int = 0) -> tuple[str, int] | None:
    """(uid, subnode) van het register met dit CANopen-index/subindex, uit de dictionary; None als onbekend."""
    try:
        drive = mc._get_drive("default")
        for sn in (1, 0, 2, 3):
            try:
                regs = drive.dictionary.registers(sn)
            except Exception:  # noqa: BLE001
                continue
            for uid, reg in regs.items():
                if getattr(reg, "idx", None) == idx and (getattr(reg, "subidx", 0) or 0) == subidx:
                    return uid, sn
    except Exception:  # noqa: BLE001
        pass
    return None


class Stroommeter:
    """Motorstroom tijdens een beweging: |Iq| (CL_CUR_Q_VALUE, de koppelvormende stroom, in A) bij elke meting.
    Gemeten via SDO in de bewaakloop (±20-40 ms), dus een heel korte piek kan tussen twee metingen vallen.
    Geeft max, gemiddeld en RMS (RMS = wat de motor warm maakt)."""

    def __init__(self, mc, servo: str = "default"):
        self.mc, self.servo = mc, servo
        self.n, self.som, self.kwad, self.max = 0, 0.0, 0.0, 0.0
        self.werkt = True

    def meet(self) -> float | None:
        if not self.werkt:
            return None
        try:
            i = abs(float(sdo(self.mc.motion.get_actual_current_quadrature, servo=self.servo)))
        except RuntimeError:
            return None                          # bus hapert even: overslaan
        except Exception:  # noqa: BLE001
            self.werkt = False                   # register bestaat niet op deze drive: niet meer proberen
            return None
        self.n += 1
        self.som += i
        self.kwad += i * i
        self.max = max(self.max, i)
        return i

    def uitkomst(self) -> dict | None:
        if not self.n:
            return None
        return {"max": round(self.max, 3), "gem": round(self.som / self.n, 3), "rms": round((self.kwad / self.n) ** 0.5, 3), "n": self.n}


def is_encoderfout(code: int) -> bool:
    """Foutcodes van de feedbackmodule (0x73xx): absolute encoder los, foutbits, BiSS-C-warningbit, …"""
    return 0x7300 <= (code & 0xFFFF) <= 0x73FF


class Foutbewaking:
    """Fouten en waarschuwingen die de drive tijdens een test meldt. Bij de start het aantal in de foutbuffer
    onthouden; tijdens de test het statuswoord bekijken (bit 7 = warning, bit 3 = fault); aan het eind de
    nieuwe regels uit de buffer lezen, met omschrijving uit de dictionary en of het een waarschuwing is."""

    def __init__(self, mc, job, servo: str = "default"):
        self.mc, self.job, self.servo = mc, job, servo
        self.warning_gezien = self.fault_gezien = False
        try:
            self.start = int(sdo(mc.errors.get_number_total_errors, servo=servo))
        except Exception as e:  # noqa: BLE001
            self.start = None
            job.log(f"  foutbuffer niet te lezen ({e}); alleen het statuswoord wordt bewaakt.")

    def kijk(self) -> None:
        try:
            sw = int(sdo(self.mc.configuration.get_status_word, servo=self.servo))
        except Exception:  # noqa: BLE001
            return
        if sw & 0x80 and not self.warning_gezien:
            self.warning_gezien = True
            self.job.log("  LET OP: de drive meldt een waarschuwing (statuswoord bit 7).")
        if sw & 0x08 and not self.fault_gezien:
            self.fault_gezien = True
            self.job.log("  LET OP: de drive staat in fout (statuswoord bit 3).")

    def nieuwe(self) -> list[dict]:
        """Nieuwe regels in de foutbuffer sinds de start, oudste eerst: {code, waarschuwing, tekst, encoder}."""
        if self.start is None:
            return []
        try:
            totaal = int(sdo(self.mc.errors.get_number_total_errors, servo=self.servo))
        except Exception:  # noqa: BLE001
            return []
        aantal = min(max(0, totaal - self.start), 32)
        uit = []
        for i in range(aantal - 1, -1, -1):              # index 0 = nieuwste
            try:
                code, _as, warning = sdo(self.mc.errors.get_buffer_error_by_index, i, servo=self.servo)
            except Exception:  # noqa: BLE001
                continue
            if not code:
                continue
            code = int(code) & 0xFFFF
            tekst = f"0x{code:04X}"
            try:
                data = self.mc.errors.get_error_data(code, servo=self.servo)
                if data and len(data) >= 4 and data[3]:
                    tekst += f" {data[3]}"
            except Exception:  # noqa: BLE001
                pass
            uit.append({"code": code, "waarschuwing": bool(warning), "tekst": tekst, "encoder": is_encoderfout(code)})
        for f in uit:
            self.job.log(f"  drive meldde {'waarschuwing' if f['waarschuwing'] else 'FOUT'}: {f['tekst']}")
        return uit
