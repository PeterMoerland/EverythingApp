"""
Langlopende taken (flashen duurt tientallen seconden) draaien als job:
starten geeft meteen een id terug, de app pollt de status en ziet de log
meegroeien. Eén job tegelijk per pc — de hardware (USB, EtherCAT) is niet
te delen.
"""

import threading
import time
import traceback
import uuid


class Job:
    def __init__(self, taak: str, parameters: dict):
        self.id = uuid.uuid4().hex[:12]
        self.taak = taak
        self.parameters = parameters
        self.stand = "wacht"          # wacht | bezig | klaar | mislukt
        self.procent = 0
        self.regels: list[str] = []
        self.resultaat = None
        self.fout: str | None = None
        self.begin = time.time()
        self.einde: float | None = None
        self.stop_gevraagd = False    # gezet via POST /jobs/<id>/stop; de taak kijkt er zelf naar en rondt af
        self._invoer: list[str] = []  # tekst van de gebruiker via POST /jobs/<id>/invoer (seriële monitor)
        self._slot = threading.Lock()

    def stuur(self, tekst: str) -> None:
        """Invoer van de gebruiker klaarzetten; een taak die ermee werkt haalt hem op met lees_invoer()."""
        with self._slot:
            self._invoer.append(tekst)

    def lees_invoer(self) -> list[str]:
        with self._slot:
            uit, self._invoer = self._invoer, []
        return uit

    # -- aparte uitvoer (seriële monitor): niet in het joblog, de app haalt hem op via GET /jobs/<id>/uitvoer --
    def uitvoer_toevoegen(self, regel: str) -> None:
        with self._slot:
            if not hasattr(self, "_uitvoer"):
                self._uitvoer: list[str] = []
                self._uitvoer_verwijderd = 0
            self._uitvoer.append(regel)
            if len(self._uitvoer) > 5000:
                weg = len(self._uitvoer) - 5000
                del self._uitvoer[:weg]
                self._uitvoer_verwijderd += weg

    def uitvoer_vanaf(self, vanaf: int) -> tuple[list[str], int]:
        """Regels vanaf volgnummer `vanaf` en het totaal tot nu toe (ook de al weggegooide tellen mee)."""
        with self._slot:
            lijst = getattr(self, "_uitvoer", [])
            basis = getattr(self, "_uitvoer_verwijderd", 0)
            totaal = basis + len(lijst)
            return lijst[max(0, vanaf - basis):], totaal

    # -- door de taak aan te roepen ---------------------------------------
    def log(self, tekst: str) -> None:
        with self._slot:
            self.regels.append(time.strftime("%H:%M:%S ") + tekst.rstrip())
            if len(self.regels) > 500:
                del self.regels[:-500]

    def voortgang(self, procent: int) -> None:
        self.procent = max(0, min(100, int(procent)))

    def klaar(self, resultaat=None) -> None:
        self.resultaat = resultaat
        self.stand = "klaar"
        self.procent = 100
        self.einde = time.time()

    def mislukt(self, reden: str) -> None:
        self.fout = reden
        self.stand = "mislukt"
        self.einde = time.time()
        self.log("FOUT: " + reden)

    # -- voor de API --------------------------------------------------------
    def naar_dict(self, vanaf: int = 0) -> dict:
        with self._slot:
            regels = self.regels[vanaf:]
            totaal = len(self.regels)
        return {
            "id": self.id, "taak": self.taak, "stand": self.stand, "procent": self.procent, "stop": self.stop_gevraagd,
            "regels": regels, "regelsTotaal": totaal,
            "resultaat": self.resultaat, "fout": self.fout,
            "seconden": round((self.einde or time.time()) - self.begin, 1),
        }


class JobBeheer:
    def __init__(self):
        self._jobs: dict[str, Job] = {}
        self._slot = threading.Lock()

    def bezig(self) -> Job | None:
        with self._slot:
            return next((j for j in self._jobs.values() if j.stand in ("wacht", "bezig")), None)

    def start(self, taak_module, inst: dict, parameters: dict) -> Job:
        with self._slot:
            lopend = next((j for j in self._jobs.values() if j.stand in ("wacht", "bezig")), None)
            if lopend is not None:
                raise RuntimeError(f"Er loopt al een taak ({lopend.taak}, {lopend.id}).")
            job = Job(taak_module.NAAM, parameters)
            self._jobs[job.id] = job
            self._opruimen()

        def draai():
            job.stand = "bezig"
            job.log(f"Start {job.taak}")
            try:
                taak_module.voer_uit(job, inst, parameters)
                if job.stand == "bezig":
                    job.klaar(job.resultaat)
            except Exception as e:  # noqa: BLE001
                job.log(traceback.format_exc())
                job.mislukt(str(e))

        threading.Thread(target=draai, name=f"job-{job.id}", daemon=True).start()
        return job

    def haal(self, job_id: str) -> Job | None:
        return self._jobs.get(job_id)

    def _opruimen(self) -> None:
        oud = time.time() - 3600
        for jid in [j.id for j in self._jobs.values() if j.einde and j.einde < oud]:
            del self._jobs[jid]
