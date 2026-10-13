"""
Feetech-servo's (STS3215 e.d.) via de USB-TTL-adapter.

Taak feetech.uitlezen
    parameters: poort (leeg = automatisch), baud (leeg = alle gangbare proberen), id (leeg = bus scannen)
    resultaat:  {"poort": …, "baud": …, "ids": [1, …], "servo": {"id": 1, "model": "STS3215",
                 "firmware": "2.54", "servoversie": "8.41", "registers": [{adres, naam, waarde, …}]}}

Taak feetech.parameters
    parameters: parameters (URL/pad van het JSON-bestand), poort, baud, id (huidig ID; leeg = de enige
                servo op de bus), alleenEeprom (standaard true)
    Schrijft alle rw-registers uit het bestand (Lock eraf, schrijven, Lock erop), leest ze terug en
    meldt afwijkingen. ID en baudrate worden als laatste geschreven; daarna wordt de servo op zijn
    nieuwe ID/baud opgezocht ter controle.
    resultaat:  {"id": nieuwId, "registers": n, "afwijkingen": m, "afwijkend": [...], "firmware": "2.54"}
"""

import time

from . import _feetech, _hulp

NAAM = "feetech.uitlezen"
OMSCHRIJVING = "Feetech-servo uitlezen (registers, versie)"

USB_UART_KENMERKEN = ("CP210", "CH340", "CH343", "CH9102", "FT232", "USB Serial", "USB-SERIAL", "Silicon Labs")
# Volgorde: fabrieksinstelling van de STS3215 (115200) eerst, dan onze werkinstelling (1M), dan de rest.
GANGBARE_BAUDS = (115_200, 1_000_000, 500_000, 250_000, 57_600, 38_400)


def beschikbaar(inst: dict):
    try:
        import serial  # noqa: F401
        return True, ""
    except Exception as e:  # noqa: BLE001
        return False, f"pyserial ontbreekt: {e}"


def _poort(inst: dict, gevraagd: str, job) -> str:
    if gevraagd:
        return gevraagd
    if inst.get("feetech_poort"):
        return inst["feetech_poort"]
    from serial.tools import list_ports
    lijst = list(list_ports.comports())
    for p in lijst:
        if any(k.lower() in (p.description + p.hwid).lower() for k in USB_UART_KENMERKEN):
            job.log(f"USB-adapter gevonden: {p.device} ({p.description})")
            return p.device
    if len(lijst) == 1:
        return lijst[0].device
    raise RuntimeError("Geen USB-seriële adapter gevonden; zet 'feetech_poort' in agent.json of geef de poort op.")


def _verbind(inst: dict, p: dict, job):
    """Bus openen; baud en ID zo nodig zoeken. Geeft (bus, baud, ids)."""
    poort = _poort(inst, p.get("poort") or "", job)
    bauds = [int(p["baud"])] if p.get("baud") else list(GANGBARE_BAUDS)
    if inst.get("feetech_baud") and not p.get("baud"):
        bauds = [int(inst["feetech_baud"])] + [b for b in bauds if b != int(inst["feetech_baud"])]
    gewenst = int(p["id"]) if p.get("id") not in (None, "") else None

    # Eerst snel: per baudrate een broadcast-ping (één servo antwoordt met zijn ID)
    # en, als dat niets oplevert, de lage ID's 1..20 met korte wachttijd. Pas als
    # dat overal faalt de langzame volledige scan op de eerst gekozen baudrate.
    for baud in bauds:
        job.log(f"Zoeken op {poort} @ {baud} baud …")
        bus = _feetech.FeetechBus(poort, baud)
        gevonden: list[int] = []
        try:
            if gewenst is not None:
                if bus.ping(gewenst) or bus.ping(gewenst):
                    job.log(f"Servo {gewenst} antwoordt op {poort} @ {baud} baud.")
                    _meld_fout(bus, job)
                    gevonden = [gewenst]
                else:
                    # Niet op het opgegeven ID: misschien is het ID net veranderd. Broadcast
                    # vertelt welk ID er wél op de bus zit (tijdens programmeren hangt er één).
                    sid = bus.broadcast_ping()
                    if sid is not None and bus.ping(sid):
                        job.log(f"Servo antwoordt niet op ID {gewenst}, maar wel als ID {sid} ({poort} @ {baud} baud); daarmee verder.")
                        _meld_fout(bus, job)
                        gevonden = [sid]
                    else:
                        ruw = bus.ruw_antwoord(gewenst)
                        job.log(f"  ping ID {gewenst}: {'geen bytes terug' if not ruw else 'terug: ' + ruw.hex(' ')}")
            else:
                sid = bus.broadcast_ping()
                if sid is not None and bus.ping(sid):
                    job.log(f"Servo gevonden via broadcast: ID {sid} op {poort} @ {baud} baud.")
                    _meld_fout(bus, job)
                    gevonden = [sid]
                else:
                    ruw = bus.ruw_antwoord()
                    job.log(f"  broadcast-ping: {'geen bytes terug' if not ruw else 'terug: ' + ruw.hex(' ')}")
                    gevonden = bus.scan(range(1, 21))
                    if gevonden:
                        job.log(f"Op {poort} @ {baud} baud gevonden: ID {', '.join(map(str, gevonden))}")
        except Exception as e:  # noqa: BLE001
            job.log(f"  {poort} @ {baud}: {e}")
        if gevonden:
            return bus, baud, gevonden      # bus blijft open voor de aanroeper
        bus.close()

    if gewenst is None:
        job.log(f"Niets op de lage ID's; volledige scan 21..253 @ {bauds[0]} baud (kan een halve minuut duren) …")
        bus = _feetech.FeetechBus(poort, bauds[0])
        ids = []
        try:
            ids = bus.scan(range(21, 254))
            if ids:
                job.log(f"Gevonden: ID {', '.join(map(str, ids))}")
                return bus, bauds[0], ids
        finally:
            if not ids:
                bus.close()
    raise RuntimeError("Geen servo gevonden. Voeding op de servo (6-12 V), kabel in de adapter, en staat de adapter op TTL?")


def _meld_fout(bus, job) -> None:
    """De foutvlaggen uit de statusbyte in de log — met uitleg, want 'spanning' is bijna altijd: geen eigen voeding."""
    if bus.laatste_fout:
        tekst = _feetech.foutbits_tekst(bus.laatste_fout)
        job.log(f"  let op: servo meldt foutvlag(gen): {tekst}"
                + (" — heeft de servo eigen voeding (6-12 V)? Uitlezen lukt, bewegen niet." if bus.laatste_fout & 0x01 else ""))


def _versie(bus, sid, tabel) -> tuple[str, str]:
    try:
        fw = f"{bus.lees_waarde(sid, 0, 1)}.{bus.lees_waarde(sid, 1, 1)}"
        sv = f"{bus.lees_waarde(sid, 3, 1)}.{bus.lees_waarde(sid, 4, 1)}"
        return fw, sv
    except Exception:  # noqa: BLE001
        return "", ""


def voer_uit(job, inst: dict, parameters: dict) -> None:
    bus, baud, ids = _verbind(inst, parameters, job)
    try:
        sid = ids[0]
        if len(ids) > 1:
            job.log(f"Let op: {len(ids)} servo's op de bus; de eerste ({sid}) wordt uitgelezen.")
        tabel = _feetech.registertabel(parameters.get("model") or "STS3215")
        fw, sv = _versie(bus, sid, tabel)
        regs = _feetech.lees_alles(bus, sid, tabel)
        gelukt = sum(1 for r in regs if r.get("waarde") is not None)
        job.log(f"Servo {sid}: firmware {fw}, servoversie {sv}, {gelukt} van {len(regs)} registers gelezen.")
        for r in [r for r in regs if r.get("fout")][:3]:
            job.log(f"  register {r['adres']} ({r['naam']}): {r['fout']}")
        job.klaar({"poort": bus.ser.port, "baud": baud, "ids": ids,
                   "servo": {"id": sid, "model": parameters.get("model") or "STS3215",
                             "firmware": fw, "servoversie": sv, "registers": regs}})
    finally:
        bus.close()


# ---------------------------------------------------------------------------
#  Programmeren: aparte taakmodule-achtige functies, geregistreerd via Parameters
# ---------------------------------------------------------------------------

class Parameters:
    NAAM = "feetech.parameters"
    OMSCHRIJVING = "Feetech-servo programmeren met een JSON-parameterset"

    @staticmethod
    def beschikbaar(inst: dict):
        return beschikbaar(inst)

    @staticmethod
    def voer_uit(job, inst: dict, parameters: dict) -> None:
        pad = _hulp.haal_firmware(parameters.get("parameters") or "", job, "parameters.json")
        setje = _feetech.lees_parameterset(pad)
        tabel = _feetech.registertabel(setje.get("model") or "STS3215")
        alleen_eeprom = parameters.get("alleenEeprom", True)
        job.log(f"Parameterset '{setje.get('omschrijving', '')}' voor {setje.get('model', '?')}: "
                f"{len(setje['registers'])} registers.")
        job.voortgang(10)

        bus, baud, ids = _verbind(inst, parameters, job)
        try:
            sid = ids[0]
            if len(ids) > 1 and parameters.get("id") in (None, ""):
                raise RuntimeError(f"Meerdere servo's op de bus ({ids}); geef op welk ID geprogrammeerd moet worden.")
            fw, sv = _versie(bus, sid, tabel)
            job.log(f"Servo {sid}: firmware {fw}, servoversie {sv}.")

            # Te schrijven registers: rw, in tabel, evt. alleen EEPROM. ID en baud als laatste.
            te_schrijven = []
            for r in setje["registers"]:
                adres = int(r["adres"])
                if adres not in tabel:
                    job.log(f"  adres {adres} onbekend in de registertabel, overgeslagen")
                    continue
                naam, nbytes, geheugen, rw, signed = tabel[adres]
                if rw != "rw" or (alleen_eeprom and geheugen != "eeprom") or adres == _feetech.ADRES_LOCK:
                    continue
                te_schrijven.append((adres, naam, nbytes, signed, int(r["waarde"])))
            te_schrijven.sort(key=lambda x: (x[0] in (_feetech.ADRES_ID, _feetech.ADRES_BAUD), x[0]))

            job.log("Lock eraf …")
            bus.schrijf_waarde(sid, _feetech.ADRES_LOCK, 1, 0)
            time.sleep(0.02)

            nieuw_id, nieuwe_baud = sid, baud
            for i, (adres, naam, nbytes, signed, waarde) in enumerate(te_schrijven):
                bus.schrijf_waarde(sid, adres, nbytes, waarde, signed)
                time.sleep(0.01)
                job.log(f"  {adres:>3} {naam}: {waarde}")
                job.voortgang(15 + int(55 * (i + 1) / max(1, len(te_schrijven))))
                if adres == _feetech.ADRES_ID:
                    nieuw_id = waarde
                    sid = nieuw_id                      # vanaf nu luistert hij op het nieuwe ID
                if adres == _feetech.ADRES_BAUD:
                    nieuwe_baud = _feetech.BAUDS.get(waarde, baud)

            job.log("Lock erop …")
            try:
                bus.schrijf_waarde(sid, _feetech.ADRES_LOCK, 1, 1)
            except Exception as e:  # noqa: BLE001
                job.log(f"  lock zetten: {e} (na een baudwissel is dat normaal; volgt hieronder)")

            if nieuwe_baud != baud:
                job.log(f"Baudrate gewijzigd naar {nieuwe_baud}; opnieuw verbinden …")
                bus.close()
                time.sleep(0.2)
                bus = _feetech.FeetechBus(bus.ser.port, nieuwe_baud)
                if not bus.ping(sid):
                    raise RuntimeError(f"Servo antwoordt niet op ID {sid} @ {nieuwe_baud} baud na het programmeren.")
                bus.schrijf_waarde(sid, _feetech.ADRES_LOCK, 1, 1)
            job.voortgang(75)

            job.log("Teruglezen ter controle …")
            afwijkend = []
            for adres, naam, nbytes, signed, waarde in te_schrijven:
                gelezen = bus.lees_waarde(sid, adres, nbytes, signed)
                if gelezen != waarde:
                    afwijkend.append({"adres": adres, "naam": naam, "verwacht": waarde, "gelezen": gelezen})
                    job.log(f"  afwijkend: {adres} {naam} verwacht {waarde}, gelezen {gelezen}")
            job.voortgang(95)
            job.log(f"Klaar: {len(te_schrijven)} registers, {len(afwijkend)} afwijkend; servo nu ID {sid} @ {nieuwe_baud} baud.")
            job.klaar({"id": sid, "baud": nieuwe_baud, "registers": len(te_schrijven), "afwijkingen": len(afwijkend),
                       "afwijkend": afwijkend, "firmware": fw, "servoversie": sv, "model": setje.get("model", "")})
        finally:
            bus.close()
