"""
Taak io.zet: de digitale uitgangen van één drive zetten en teruglezen.
Novanta Summit: 0x2602 = set value (bitmasker), 0x2601 = value (teruglezen).

Parameters:
    positie   1 = eerste drive op de bus
    masker    bitmasker (bit 0 = uitgang 1), óf
    uitgang   1..n en aan: true/false — dan wordt alleen die bit gewijzigd t.o.v.
              de huidige teruggelezen stand
Resultaat: {"positie": n, "gezet": masker, "gelezen": waarde}
"""

from . import drives

NAAM = "io.zet"
OMSCHRIJVING = "Digitale uitgangen van een drive zetten"


def beschikbaar(inst: dict):
    return drives.beschikbaar(inst)


def voer_uit(job, inst: dict, parameters: dict) -> None:
    positie = int(parameters.get("positie") or 1)
    masker = parameters.get("masker")
    if masker is None and parameters.get("uitgang") is not None:
        huidig = drives.zet_uitgangen(inst, positie, None, job.log)
        if huidig.get("errors"):
            raise RuntimeError("; ".join(huidig["errors"]))
        bit = 1 << (int(parameters["uitgang"]) - 1)
        masker = (huidig.get("gelezen", 0) | bit) if parameters.get("aan") else (huidig.get("gelezen", 0) & ~bit)
    uit = drives.zet_uitgangen(inst, positie, None if masker is None else int(masker), job.log)
    if uit.get("errors"):
        raise RuntimeError("; ".join(uit["errors"]))
    job.log(f"Drive {positie}: uitgangen = {uit.get('gelezen', 0):04b}")
    job.klaar(uit)
