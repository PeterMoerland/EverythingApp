"""
Taak drives.parameters: een MotionLab-parameterset (.xcf) in een drive laden,
opslaan in het niet-vluchtige geheugen en ter controle alle registers teruglezen.

Parameters:
    positie         1 = eerste drive op de EtherCAT-bus
    parameters      URL of pad van het .xcf-bestand
    dictionary      URL of pad van de .xdf-dictionary die bij de firmware hoort
    firmwareVersie  ter info in de log
    vergelijk       true = alleen vergelijken (niets schrijven): elk register uit de .xcf lezen en
                    de afwijkingen teruggeven ('parameter compare')

Resultaat: {"registers": n, "afwijkingen": m, "afwijkend": [{"id":..,"verwacht":..,"gelezen":..}, …]}
"""

from . import _hulp, _ingenia

NAAM = "drives.parameters"
OMSCHRIJVING = "Parameterset (.xcf) in een drive laden (ingeniamotion)"


def beschikbaar(inst: dict):
    return _ingenia.beschikbaar()


def voer_uit(job, inst: dict, parameters: dict) -> None:
    positie = int(parameters.get("positie") or 1)
    xcf = _hulp.haal_firmware(parameters.get("parameters") or "", job, "parameters.xcf")
    xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")

    kop = _ingenia.xcf_kop(xcf)
    registers = _ingenia.lees_xcf(xcf)
    job.log(f"Parameterset voor {kop.get('PartNumber', '?')} (firmware {kop.get('firmwareVersion', '?')}), "
            f"{len(registers)} registers; drive meldt firmware {parameters.get('firmwareVersie', '?')}.")
    job.voortgang(10)

    if str(parameters.get("vergelijk", "")).lower() in ("1", "true", "ja"):
        with _ingenia.verbinding(inst, positie, xdf, job) as mc:
            job.log("Alleen vergelijken: registers lezen …")
            afwijkend, alle, gelijk = [], [], 0
            for i, r in enumerate(registers):
                try:
                    gelezen = _ingenia.sdo(mc.communication.get_register, r["id"], axis=r["subnode"])
                except Exception as e:  # noqa: BLE001
                    rij = {"id": r["id"], "verwacht": r["storage"], "gelezen": f"fout: {e}", "dtype": r["dtype"], "gelijk": False}
                    afwijkend.append(rij); alle.append(rij)
                    continue
                is_gelijk = _ingenia.gelijk(r["dtype"], r["storage"], gelezen)
                rij = {"id": r["id"], "verwacht": r["storage"], "gelezen": str(gelezen), "dtype": r["dtype"], "gelijk": is_gelijk}
                alle.append(rij)
                if is_gelijk:
                    gelijk += 1
                else:
                    afwijkend.append(rij)
                if i % 25 == 0:
                    job.voortgang(10 + int(85 * i / max(1, len(registers))))
        for a in afwijkend[:40]:
            job.log(f"  afwijkend: {a['id']} parameterset {a['verwacht']}, drive {a['gelezen']}")
        job.log(f"Klaar: {len(registers)} registers vergeleken, {gelijk} gelijk, {len(afwijkend)} afwijkend.")
        job.klaar({"vergelijk": True, "registers": len(registers), "gelijk": gelijk, "afwijkingen": len(afwijkend),
                   "afwijkend": afwijkend, "alle": alle, "partnumber": kop.get("PartNumber", ""), "xcfFirmware": kop.get("firmwareVersion", "")})
        return

    with _ingenia.verbinding(inst, positie, xdf, job) as mc:
        job.log("Parameters schrijven …")
        mc.configuration.load_configuration(xcf)
        job.voortgang(55)

        job.log("Opslaan in de drive (store) …")
        mc.configuration.store_configuration()
        job.voortgang(70)

        job.log("Teruglezen ter controle …")
        afwijkend = []
        for i, r in enumerate(registers):
            try:
                gelezen = _ingenia.sdo(mc.communication.get_register, r["id"], axis=r["subnode"])
            except Exception as e:  # noqa: BLE001
                afwijkend.append({"id": r["id"], "verwacht": r["storage"], "gelezen": f"fout: {e}"})
                continue
            if not _ingenia.gelijk(r["dtype"], r["storage"], gelezen):
                afwijkend.append({"id": r["id"], "verwacht": r["storage"], "gelezen": str(gelezen)})
            if i % 25 == 0:
                job.voortgang(70 + int(25 * i / max(1, len(registers))))

    for a in afwijkend[:30]:
        job.log(f"  afwijkend: {a['id']} verwacht {a['verwacht']}, gelezen {a['gelezen']}")
    job.log(f"Klaar: {len(registers)} registers, {len(afwijkend)} afwijkend.")
    job.klaar({"registers": len(registers), "afwijkingen": len(afwijkend), "afwijkend": afwijkend[:100],
               "partnumber": kop.get("PartNumber", ""), "xcfFirmware": kop.get("firmwareVersion", "")})
