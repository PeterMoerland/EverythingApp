"""
Taak test.uitvoeren: een test op de print draaien. Welke test, staat in de
parameter 'test' (code uit Productie:Testen van de app). Elke test is een
functie hieronder die een dict teruggeeft: {"geslaagd": bool, "samenvatting": str,
"metingen": {...}}. Nieuwe test = functie erbij en opnemen in TESTEN.

Parameters: test, serienummer, product, posities (aantal verwachte drives)
"""

from . import drives

NAAM = "test.uitvoeren"
OMSCHRIJVING = "Test op de print uitvoeren"


def beschikbaar(inst: dict):
    return True, ""


# --------------------------------------------------------------------------
#  De testen
# --------------------------------------------------------------------------

def basistest(job, inst: dict, p: dict) -> dict:
    """Basistest: alle verwachte drives antwoorden op de bus en hebben een serienummer."""
    verwacht = int(p.get("posities") or 0)
    job.log(f"Basistest: {verwacht or '?'} drive(s) verwacht op de EtherCAT-bus.")
    uit = drives.lees(inst, "", job.log)
    gevonden = uit.get("drives", [])
    for e in uit.get("errors", []):
        job.log("  " + e)
    zonder = [d for d in gevonden if not d.get("serial_number")]
    ok = len(gevonden) > 0 and (verwacht == 0 or len(gevonden) == verwacht) and not zonder
    samenvatting = f"{len(gevonden)} drive(s) gevonden" + (f", {verwacht} verwacht" if verwacht else "") \
                   + (f", {len(zonder)} zonder serienummer" if zonder else "")
    return {"geslaagd": ok, "samenvatting": samenvatting,
            "metingen": {"drives": [{"positie": d.get("slave_position"), "serienummer": d.get("serial_number"),
                                     "firmware": d.get("software_version")} for d in gevonden]}}


def iotest(job, inst: dict, p: dict) -> dict:
    """
    IO-test: op elke drive de uitgangen één voor één aan (looplicht), teruglezen via 0x2601 én
    de ingangen lezen via 0x2600. Op de testrig zijn uitgang n en ingang n doorverbonden
    (bruggen), dus bij uitgang n aan hoort precies ingang n hoog te zijn en de andere laag;
    met alles uit horen alle ingangen laag. Daarmee is de test volledig automatisch.
    Parameter 'brug' = false → alleen teruglezen van de uitgangen, met bevestiging door de operator.
    """
    import time as _t
    posities = int(p.get("posities") or 0)
    per_drive = int(p.get("uitgangen") or 4)
    pauze = float(p.get("pauze") or 0.4)
    brug = str(p.get("brug", "1")).lower() not in ("0", "false", "nee", "")
    masker_alle = (1 << per_drive) - 1

    if posities == 0:
        uit = drives.lees(inst, "", job.log)
        posities = len(uit.get("drives", []))
    if posities == 0:
        return {"geslaagd": False, "samenvatting": "geen drives op de bus"}

    fouten = []
    ingangen_gezien = True
    stappen = posities * (per_drive + 1)
    stap = 0
    for pos in range(1, posities + 1):
        # rusttoestand: alles uit → alle ingangen laag
        r = drives.zet_uitgangen(inst, pos, 0, job.log)
        stap += 1
        if r.get("errors"):
            fouten.append(f"drive {pos}: {'; '.join(r['errors'])}")
            job.log(f"  drive {pos}: FOUT {'; '.join(r['errors'])}")
            continue
        if brug:
            if r.get("ingangen") is None:
                ingangen_gezien = False
                job.log(f"  drive {pos}: ingangen (0x2600) niet leesbaar — alleen teruglezen van de uitgangen")
            else:
                ing = r["ingangen"] & masker_alle
                job.log(f"  drive {pos} alles uit: ingangen {ing:0{per_drive}b} {'✓' if ing == 0 else '✗'}")
                if ing != 0:
                    fouten.append(f"drive {pos}: ingangen {ing:0{per_drive}b} hoog terwijl alle uitgangen uit staan")
        _t.sleep(pauze / 2)
        for bit in range(per_drive):
            masker = 1 << bit
            r = drives.zet_uitgangen(inst, pos, masker, job.log)
            stap += 1
            job.voortgang(10 + int(80 * stap / stappen))
            if r.get("errors"):
                fouten.append(f"drive {pos} uitgang {bit + 1}: {'; '.join(r['errors'])}")
                job.log(f"  drive {pos} uitgang {bit + 1}: FOUT {'; '.join(r['errors'])}")
                continue
            gelezen = r.get("gelezen", 0)
            ok_uit = (gelezen & masker) == masker
            regel = f"  drive {pos} uitgang {bit + 1}: aan → uitgang terug {gelezen:0{per_drive}b} {'✓' if ok_uit else '✗'}"
            if not ok_uit:
                fouten.append(f"drive {pos} uitgang {bit + 1} leest {gelezen:0{per_drive}b} terug")
            if brug and r.get("ingangen") is not None:
                ing = r["ingangen"] & masker_alle
                ok_in = ing == masker
                regel += f", ingangen {ing:0{per_drive}b} {'✓' if ok_in else '✗'}"
                if not ok_in:
                    if ing & masker == 0:
                        fouten.append(f"drive {pos} uitgang {bit + 1} → ingang {bit + 1} blijft laag (brug/bedrading/uitgang defect)")
                    else:
                        fouten.append(f"drive {pos} uitgang {bit + 1} → ook andere ingang(en) hoog: {ing:0{per_drive}b} (kortsluiting/verwisseld)")
            job.log(regel)
            _t.sleep(pauze)
        drives.zet_uitgangen(inst, pos, 0, job.log)

    samenvatting = f"{posities} drive(s) × {per_drive} uitgangen" + (
        f", {len(fouten)} fout(en)" if fouten else (", uitgangen en ingangen ok" if brug and ingangen_gezien else ", teruglezen ok"))
    return {"geslaagd": not fouten, "samenvatting": samenvatting, "fouten": fouten,
            "operator_bevestiging": not (brug and ingangen_gezien)}


def nog_niet(naam):
    def _t(job, inst, p):
        job.log(f"Test '{naam}' is nog niet uitgewerkt in de agent.")
        return {"geslaagd": False, "samenvatting": "nog niet geïmplementeerd"}
    return _t


TESTEN = {
    "basistest": basistest,
    "iotest": iotest,
    "test3": nog_niet("Test 3"),
}


def voer_uit(job, inst: dict, parameters: dict) -> None:
    code = (parameters.get("test") or "").strip().lower()
    fn = TESTEN.get(code)
    if fn is None:
        raise RuntimeError(f"Onbekende test '{code}'. Bekend: {', '.join(TESTEN)}")
    job.log(f"Start {code} voor {parameters.get('product', '')} {parameters.get('serienummer', '')}")
    job.voortgang(5)
    uit = fn(job, inst, parameters)
    job.log(("GESLAAGD: " if uit.get("geslaagd") else "MISLUKT: ") + uit.get("samenvatting", ""))
    job.klaar(uit)
