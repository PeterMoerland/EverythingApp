"""
Taak drives.stroomlus: de stroomregellus van een Ingenia/Novanta-drive (Everest/Capitan) testen
zoals 'Current Direct Tuning' in MotionLab — maar dan in de drive zelf:

  - disturbance: blokgolf ±amplitude op CL_CUR_D_SET_POINT (direct-as, dus geen koppel, geen beweging);
  - monitoring: CL_CUR_D_REF_VALUE (demand) en CL_CUR_D_VALUE (werkelijk) op de regelklok;
  - modus Current, fault reset, enable, meten, disable, setpoint 0.

Beoordeling per stap (gemiddeld over alle flanken): stijgtijd 10→90 %, overshoot, fout in het
vlakke stuk (laatste 40 % van elke halve periode) en rimpel daar. Grenzen komen mee als parameters.

Parameters:
    positie       1 = eerste drive op de EtherCAT-bus
    dictionary    URL of pad van de .xdf die bij de firmware hoort
    amplitude     A (standaard 1.0)      frequentie  Hz (standaard 200)      perioden (standaard 3)
    stijgtijdMs   grens (standaard 0.5)  overshootPct (15)  foutPct (5)  rimpelPct (10)
    maxDeelMaxstroom  amplitude mag hooguit dit deel van CL_CUR_REF_MAX zijn (standaard 0.2)

Resultaat: {"geslaagd": bool, "samenvatting": str, "frequentie": Hz van de meting, "t": [...],
            "demand": [...], "waarde": [...], "metingen": {...}, "grenzen": {...}}
"""

import time

from . import _hulp, _ingenia

NAAM = "drives.stroomlus"
OMSCHRIJVING = "Stroomlus testen (Current Direct, blokgolf in de drive) — Everest/Capitan"

REG_SETPOINT = "CL_CUR_D_SET_POINT"
REG_DEMAND = "CL_CUR_D_REF_VALUE"
REG_WAARDE = "CL_CUR_D_VALUE"
REG_MAX = "CL_CUR_REF_MAX"


def beschikbaar(inst: dict):
    return _ingenia.beschikbaar()


def _f(p: dict, naam: str, standaard: float) -> float:
    try:
        v = p.get(naam)
        return float(v) if v not in (None, "") else standaard
    except Exception:  # noqa: BLE001
        return standaard


def _loop_rate(mc, job) -> float:
    for naam in ("get_position_and_velocity_loop_rate", "get_current_loop_rate"):
        try:
            f = float(getattr(mc.configuration, naam)())
            if f > 0:
                return f
        except Exception as e:  # noqa: BLE001
            job.log(f"  {naam}: {e}")
    return 1000.0


def beoordeel(t: list[float], demand: list[float], waarde: list[float], amplitude: float, grenzen: dict) -> dict:
    """Segmenten van de blokgolf bepalen (elke sprong in de demand) en per segment op ±amplitude:
    stijgtijd 10→90 %, overshoot, fout en rimpel in het vlakke stuk (laatste 40 % van dat segment).
    Het stuk vóór de eerste sprong en een eventueel eindstuk op 0 A tellen niet mee."""
    n = min(len(t), len(demand), len(waarde))
    if n < 10:
        return {"fout": "te weinig samples"}
    grenzen_idx = [i for i in range(1, n) if abs(demand[i] - demand[i - 1]) > amplitude * 0.5]
    if len(grenzen_idx) < 2:
        return {"fout": "geen blokgolf in de demand gezien (disturbance niet actief?)"}
    segmenten = []
    for k, i0 in enumerate(grenzen_idx):
        i1 = grenzen_idx[k + 1] if k + 1 < len(grenzen_idx) else n
        niveau = demand[i0]
        if i1 - i0 >= 4 and abs(abs(niveau) - amplitude) <= amplitude * 0.25:
            segmenten.append((i0, i1, niveau))
    if not segmenten:
        return {"fout": "geen segmenten op ±amplitude"}
    # Een segment dat door het einde van de meting is afgekapt (korter dan 80 % van de normale halve
    # periode) is niet te beoordelen: het 'vlakke stuk' zou dan midden in de flank liggen.
    lengtes = sorted(i1 - i0 for i0, i1, _ in segmenten)
    normaal = lengtes[len(lengtes) // 2]
    volledig = [sgm for sgm in segmenten if sgm[1] - sgm[0] >= 0.8 * normaal]
    if len(volledig) >= 2:
        segmenten = volledig
    stijg, over, fouten, rimpels = [], [], [], []
    for i0, i1, doel in segmenten:
        start = waarde[i0 - 1]
        stap = doel - start
        if abs(stap) < amplitude * 0.5:
            continue
        t10 = t90 = None
        for i in range(i0, i1):
            frac = (waarde[i] - start) / stap
            if t10 is None and frac >= 0.1:
                t10 = t[i]
            if t90 is None and frac >= 0.9:
                t90 = t[i]
                break
        if t10 is not None and t90 is not None:
            stijg.append(max(0.0, t90 - t10))
        uiterst = max(((waarde[i] - doel) / stap) for i in range(i0, i1))
        over.append(max(0.0, uiterst) * 100)
        vanaf = i0 + int((i1 - i0) * 0.6)
        vlak = waarde[vanaf:i1]
        if vlak:
            gem = sum(vlak) / len(vlak)
            fouten.append(abs(gem - doel) / amplitude * 100)
            rimpels.append((max(vlak) - min(vlak)) / amplitude * 100)
    if not stijg or not over:
        return {"fout": "geen bruikbare flanken"}
    m = {
        "flanken": len(segmenten),
        "stijgtijd_ms": round(sum(stijg) / len(stijg) * 1000, 3),
        "stijgtijd_max_ms": round(max(stijg) * 1000, 3),
        "overshoot_pct": round(max(over), 1),
        "fout_pct": round(max(fouten), 1) if fouten else None,
        "rimpel_pct": round(max(rimpels), 1) if rimpels else None,
    }
    oordeel = []
    if m["stijgtijd_max_ms"] > grenzen["stijgtijdMs"]:
        oordeel.append(f"stijgtijd {m['stijgtijd_max_ms']} ms > {grenzen['stijgtijdMs']} ms")
    if m["overshoot_pct"] > grenzen["overshootPct"]:
        oordeel.append(f"overshoot {m['overshoot_pct']} % > {grenzen['overshootPct']} %")
    if m["fout_pct"] is not None and m["fout_pct"] > grenzen["foutPct"]:
        oordeel.append(f"fout {m['fout_pct']} % > {grenzen['foutPct']} %")
    if m["rimpel_pct"] is not None and m["rimpel_pct"] > grenzen["rimpelPct"]:
        oordeel.append(f"rimpel {m['rimpel_pct']} % > {grenzen['rimpelPct']} %")
    m["afgekeurd"] = oordeel
    return m


def voer_uit(job, inst: dict, parameters: dict) -> None:
    positie = int(parameters.get("positie") or 1)
    xdf = _hulp.haal_firmware(parameters.get("dictionary") or "", job, "dictionary.xdf")
    amplitude = _f(parameters, "amplitude", 1.0)
    frequentie = _f(parameters, "frequentie", 200.0)
    perioden = max(1, int(_f(parameters, "perioden", 3)))
    grenzen = {"stijgtijdMs": _f(parameters, "stijgtijdMs", 0.5), "overshootPct": _f(parameters, "overshootPct", 15),
               "foutPct": _f(parameters, "foutPct", 5), "rimpelPct": _f(parameters, "rimpelPct", 10)}
    max_deel = _f(parameters, "maxDeelMaxstroom", 0.2)

    from ingeniamotion.enums import OperationMode

    with _ingenia.verbinding(inst, positie, xdf, job) as mc:
        # STO: zonder vrijgave kan de motor niet aan — dan geen test maar een melding (popup in de app).
        melding = _ingenia.sto_vrij(mc, job)
        if melding:
            job.klaar({"geslaagd": False, "sto_ontbreekt": True, "samenvatting": melding})
            return

        # veiligheid: amplitude t.o.v. de maximale stroom van de drive
        try:
            imax = float(mc.communication.get_register(REG_MAX))
            job.log(f"Max. stroom in de drive ({REG_MAX}): {imax:g} A")
            if amplitude > imax * max_deel:
                raise RuntimeError(f"Amplitude {amplitude:g} A is meer dan {max_deel:.0%} van de maximale stroom ({imax:g} A).")
        except RuntimeError:
            raise
        except Exception as e:  # noqa: BLE001
            job.log(f"  {REG_MAX} niet gelezen ({e}); amplitude niet gecontroleerd")

        rate = _loop_rate(mc, job)
        job.log(f"Regelklok {rate:g} Hz; blokgolf ±{amplitude:g} A, {frequentie:g} Hz, {perioden} perioden.")
        half = max(1, int(round(rate / (2 * frequentie))))
        f_werkelijk = rate / (2 * half)
        data = ([amplitude] * half + [-amplitude] * half) * perioden
        meettijd = len(data) / rate + 4 * half / rate     # een hele periode extra: de capture start niet gelijk met de blokgolf
        job.voortgang(10)

        # Kp/Ki ter info (zodat de meting te vergelijken is met MotionLab)
        for reg in ("CL_CUR_D_KP", "CL_CUR_D_KI"):
            try:
                job.log(f"  {reg} = {mc.communication.get_register(reg)}")
            except Exception:  # noqa: BLE001
                pass

        # wat we aanraken en achteraf terugzetten: de bedrijfsmodus en de setpoints
        backup = {}
        for uid in ("DRV_OP_CMD", "CL_CUR_D_SET_POINT", "CL_CUR_Q_SET_POINT"):
            try:
                backup[uid] = mc.communication.get_register(uid)
            except Exception:  # noqa: BLE001
                pass

        job.log("Monitoring en disturbance inrichten …")
        mc.capture.clean_monitoring_disturbance()
        monitoring = mc.capture.create_monitoring(
            [{"name": REG_DEMAND, "axis": 1}, {"name": REG_WAARDE, "axis": 1}],
            prescaler=1, sample_time=meettijd, trigger_delay=0)
        mc.capture.create_disturbance(REG_SETPOINT, data, freq_divider=1)
        job.voortgang(25)

        job.log("Modus Current, fault reset, motor enable …")
        mc.motion.set_operation_mode(OperationMode.CURRENT)
        try:
            mc.motion.set_current_direct(0.0)
            mc.motion.set_current_quadrature(0.0)
        except Exception:  # noqa: BLE001
            pass
        try:
            _ingenia.motor_aan(mc, job)
            job.voortgang(40)
            job.log("Meten …")
            mc.capture.enable_monitoring_disturbance()
            t0 = time.time()
            samples = monitoring.read_monitoring_data(timeout=meettijd + 10)
            job.log(f"  {len(samples[0]) if samples else 0} samples in {time.time() - t0:.2f} s")
        finally:
            for stap in (mc.capture.disable_monitoring_disturbance, lambda: mc.motion.set_current_direct(0.0),
                         mc.motion.motor_disable, mc.capture.clean_monitoring_disturbance):
                try:
                    stap()
                except Exception as e:  # noqa: BLE001
                    job.log(f"  afronden: {e}")
            fouten = 0
            for uid, w in backup.items():
                try:
                    mc.communication.set_register(uid, w)
                except Exception:  # noqa: BLE001
                    fouten += 1
            job.log(f"Motor uit, setpoint 0, bedrijfsmodus en setpoints hersteld ({len(backup) - fouten} registers).")
            _ingenia.laatste_fout(mc, job)
        job.voortgang(85)

    if not samples or len(samples) < 2 or not samples[0]:
        raise RuntimeError("Geen meetdata uit de drive gekregen.")
    demand = [float(x) for x in samples[0]]
    waarde = [float(x) for x in samples[1]]
    t = [i / rate for i in range(len(demand))]
    m = beoordeel(t, demand, waarde, amplitude, grenzen)
    if "fout" in m:
        raise RuntimeError("Beoordeling niet mogelijk: " + m["fout"])
    geslaagd = not m["afgekeurd"]
    samenvatting = (f"stijgtijd {m['stijgtijd_ms']} ms (max {m['stijgtijd_max_ms']}), overshoot {m['overshoot_pct']} %, "
                    f"fout {m['fout_pct']} %, rimpel {m['rimpel_pct']} % bij ±{amplitude:g} A / {f_werkelijk:.0f} Hz")
    if not geslaagd:
        samenvatting += " — " + "; ".join(m["afgekeurd"])
    job.log(("GESLAAGD: " if geslaagd else "AFGEKEURD: ") + samenvatting)
    # data uitdunnen voor de app (max ~2000 punten)
    stap = max(1, len(t) // 2000)
    job.klaar({"geslaagd": geslaagd, "samenvatting": samenvatting, "frequentie": rate, "amplitude": amplitude,
               "blokgolf_hz": f_werkelijk, "t": t[::stap], "demand": demand[::stap], "waarde": waarde[::stap],
               "metingen": m, "grenzen": grenzen})
