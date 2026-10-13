"""
Taak drives.registers: álle registers uit de dictionary (.xdf) van een Ingenia-drive lezen —
ook de alleen-lezen statusregisters — en teruggeven met index, type, toegang, eenheid en waarde.

Parameters: positie, dictionary, subnode (leeg = alle subnodes), filter (deel van de naam, optioneel)

Resultaat: {"registers": [{"id","subnode","idx","sub","dtype","toegang","eenheid","categorie","omschrijving","waarde","fout"}...],
            "aantal": n, "gelezen": m, "fouten": k}
"""

from . import _hulp, _ingenia

NAAM = "drives.registers"
OMSCHRIJVING = "Alle registers van een drive lezen (dictionary dump)"


def beschikbaar(inst: dict):
    return _ingenia.beschikbaar()


def _attr(reg, *namen, standaard=""):
    for n in namen:
        try:
            v = getattr(reg, n)
        except Exception:  # noqa: BLE001
            continue
        if v is None:
            continue
        return getattr(v, "name", v)
    return standaard


def voer_uit(job, inst: dict, parameters: dict) -> None:
    positie = int(parameters.get("positie") or 1)
    xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")
    gevraagd_subnode = parameters.get("subnode")
    filter_ = (parameters.get("filter") or "").strip().lower()

    with _ingenia.verbinding(inst, positie, xdf, job) as mc:
        drive = mc._get_drive("default")
        try:
            subnodes = sorted(int(k) for k in mc.info.get_subnodes().keys())
        except Exception:  # noqa: BLE001
            subnodes = [0, 1]
        if gevraagd_subnode not in (None, ""):
            subnodes = [int(gevraagd_subnode)]
        uit = []
        totaal = 0
        for sn in subnodes:
            try:
                regs = drive.dictionary.registers(sn)
            except Exception as e:  # noqa: BLE001
                job.log(f"  subnode {sn}: dictionary niet leesbaar ({e})")
                continue
            items = [(uid, r) for uid, r in regs.items() if not filter_ or filter_ in uid.lower()]
            totaal += len(items)
            job.log(f"Subnode {sn}: {len(items)} registers lezen …")
            for i, (uid, reg) in enumerate(items):
                rij = {"id": uid, "subnode": sn,
                       "idx": f"0x{getattr(reg, 'idx', 0):04X}" if getattr(reg, "idx", None) is not None else "",
                       "sub": getattr(reg, "subidx", ""),
                       "dtype": str(_attr(reg, "dtype")), "toegang": str(_attr(reg, "access")),
                       "eenheid": str(_attr(reg, "units")), "categorie": str(_attr(reg, "cat_id")),
                       "omschrijving": str(_attr(reg, "description", "labels")), "waarde": "", "fout": ""}
                toegang = rij["toegang"].upper()
                if "WO" in toegang:
                    rij["fout"] = "alleen schrijven"
                else:
                    try:
                        w = _ingenia.sdo(mc.communication.get_register, uid, axis=sn, pogingen=2)
                        rij["waarde"] = str(w)
                    except Exception as e:  # noqa: BLE001
                        rij["fout"] = str(e).split("\n")[0][:120]
                uit.append(rij)
                if i % 20 == 0:
                    job.voortgang(int(5 + 90 * len(uit) / max(1, totaal)))
    gelezen = sum(1 for r in uit if r["waarde"] != "")
    fouten = sum(1 for r in uit if r["fout"] and r["fout"] != "alleen schrijven")
    job.log(f"Klaar: {len(uit)} registers, {gelezen} gelezen, {fouten} fout(en).")
    job.klaar({"registers": uit, "aantal": len(uit), "gelezen": gelezen, "fouten": fouten})
