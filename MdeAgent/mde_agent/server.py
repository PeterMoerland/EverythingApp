"""
De HTTP-API van de agent, alleen op 127.0.0.1.

GET  /health                    versie, hostnaam, taken + beschikbaarheid, gedeeld (gedeelde werkplek)
GET  /adapters                  netwerkadapters (EtherCAT)
GET  /poorten                   COM-poorten (ESP32)
GET  /drives[?ifname=]          drives synchroon uitlezen (snel; voor Test PCB)
POST /taken/<naam>              job starten, body = JSON-parameters → {"job": id}
GET  /jobs/<id>[?vanaf=n]       status + logregels vanaf n
POST /jobs/<id>/stop            (voorlopig: alleen markeren; processen lopen af via time-out)
POST /jobs/<id>/invoer          {"tekst": "..."} naar een lopende taak (seriële monitor)
GET  /jobs/<id>/uitvoer[?vanaf=n]  aparte uitvoer van een taak (seriële monitor), buiten het joblog

Beveiliging: alleen localhost; CORS alleen voor de origins in "toegestaan";
een token (X-Agent-Token of ?token=) als er een is ingesteld. Reads (/health)
mogen zonder token, zodat de app kan zien óf er een agent draait.
"""

import json
import logging
import socket
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

from . import VERSIE, instellingen
from .jobs import JobBeheer
from .taken import ALLE, drives, esp32

log = logging.getLogger("mde_agent")


class Handler(BaseHTTPRequestHandler):
    inst: dict = {}
    jobs: JobBeheer = JobBeheer()
    server_version = f"MdeAgent/{VERSIE}"

    # -- helpers ------------------------------------------------------------
    def _herkomst(self) -> str | None:
        return self.headers.get("Origin")

    def _cors(self) -> None:
        h = self._herkomst()
        toegestaan = self.inst.get("toegestaan") or []
        if h and (h in toegestaan or "*" in toegestaan):
            self.send_header("Access-Control-Allow-Origin", h)
            self.send_header("Access-Control-Allow-Headers", "Content-Type, X-Agent-Token")
            self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            # Chrome: een (https-)site op internet die naar een lokaal adres praat
            # (Private/Local Network Access) moet dit expliciet toegestaan krijgen.
            self.send_header("Access-Control-Allow-Private-Network", "true")
            self.send_header("Access-Control-Max-Age", "600")
            self.send_header("Vary", "Origin")
        elif h:
            log.warning("Aanroep vanaf niet-toegestane origin %s (zet hem in 'toegestaan' in agent.json)", h)

    def _stuur(self, code: int, inhoud) -> None:
        tekst = json.dumps(inhoud, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(tekst)))
        self.send_header("Cache-Control", "no-store")
        self._cors()
        self.end_headers()
        self.wfile.write(tekst)

    def _token_ok(self, vraag: dict) -> bool:
        token = self.inst.get("token") or ""
        if not token:
            return True
        gegeven = self.headers.get("X-Agent-Token") or (vraag.get("token") or [""])[0]
        return gegeven == token

    def _body(self) -> dict:
        n = int(self.headers.get("Content-Length") or 0)
        if n == 0:
            return {}
        try:
            return json.loads(self.rfile.read(n).decode("utf-8")) or {}
        except Exception:  # noqa: BLE001
            return {}

    # -- verbs ----------------------------------------------------------------
    def do_OPTIONS(self) -> None:  # noqa: N802
        self.send_response(204)
        self._cors()
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        self._veilig(self._get)

    def do_POST(self) -> None:  # noqa: N802
        self._veilig(self._post)

    def _veilig(self, fn) -> None:
        """Een fout in de afhandeling mag nooit een lege response geven: loggen en 500."""
        try:
            fn()
        except Exception as e:  # noqa: BLE001
            log.exception("Fout bij %s %s", self.command, self.path)
            try:
                self._stuur(500, {"fout": f"{type(e).__name__}: {e}", "drives": [], "errors": [str(e)]})
            except Exception:  # noqa: BLE001
                pass

    def _get(self) -> None:
        url = urlparse(self.path)
        vraag = parse_qs(url.query)
        pad = url.path.rstrip("/")

        if pad == "/health":
            taken = {}
            for naam, mod in ALLE.items():
                ok, reden = mod.beschikbaar(self.inst)
                taken[naam] = {"omschrijving": mod.OMSCHRIJVING, "beschikbaar": ok, "reden": reden}
            lopend = self.jobs.bezig()
            self._stuur(200, {"ok": True, "versie": VERSIE, "pc": socket.gethostname(),
                              "taken": taken, "bezig": lopend.id if lopend else None,
                              "tokenVereist": bool(self.inst.get("token")),
                              "gedeeld": bool(self.inst.get("gedeelde_werkplek"))})
            return

        if not self._token_ok(vraag):
            self._stuur(401, {"fout": "Token ontbreekt of klopt niet."})
            return

        if pad == "/adapters":
            self._stuur(200, drives.adapters())
        elif pad == "/poorten":
            self._stuur(200, esp32.poorten())
        elif pad == "/drives":
            if self.jobs.bezig():
                self._stuur(409, {"drives": [], "errors": ["Er loopt al een taak."], "interface": ""})
                return
            with _drives_slot:
                self._stuur(200, drives.lees(self.inst, (vraag.get("ifname") or [""])[0], log.info))
        elif pad.startswith("/jobs/") and pad.endswith("/uitvoer"):
            # aparte uitvoer van een taak (seriële monitor), buiten het joblog
            job = self.jobs.haal(pad.split("/")[2])
            if job is None:
                self._stuur(404, {"fout": "Onbekende job."})
            else:
                vanaf = int((vraag.get("vanaf") or ["0"])[0])
                regels, totaal = job.uitvoer_vanaf(vanaf)
                res = job.resultaat if isinstance(job.resultaat, dict) else {}
                self._stuur(200, {"regels": regels, "totaal": totaal, "stand": job.stand, "fout": job.fout,
                                  "poort": res.get("poort", "")})
        elif pad.startswith("/jobs/"):
            job = self.jobs.haal(pad.split("/")[2])
            if job is None:
                self._stuur(404, {"fout": "Onbekende job."})
            else:
                vanaf = int((vraag.get("vanaf") or ["0"])[0])
                self._stuur(200, job.naar_dict(vanaf))
        else:
            self._stuur(404, {"fout": "Onbekend pad."})

    def _post(self) -> None:
        url = urlparse(self.path)
        vraag = parse_qs(url.query)
        pad = url.path.rstrip("/")
        if not self._token_ok(vraag):
            self._stuur(401, {"fout": "Token ontbreekt of klopt niet."})
            return

        if pad.startswith("/taken/"):
            naam = pad[len("/taken/"):]
            mod = ALLE.get(naam)
            if mod is None:
                self._stuur(404, {"fout": f"Onbekende taak '{naam}'."})
                return
            ok, reden = mod.beschikbaar(self.inst)
            if not ok:
                self._stuur(503, {"fout": f"Taak niet beschikbaar op deze pc: {reden}"})
                return
            try:
                job = self.jobs.start(mod, self.inst, self._body())
                log.info("Job %s gestart: %s", job.id, naam)
                self._stuur(202, {"job": job.id})
            except RuntimeError as e:
                self._stuur(409, {"fout": str(e)})
            return

        if pad.startswith("/jobs/") and pad.endswith("/stop"):
            job = self.jobs.haal(pad.split("/")[2])
            if job is None:
                self._stuur(404, {"fout": "Onbekende job."})
            else:
                job.stop_gevraagd = True
                job.log("Stop gevraagd door gebruiker — taak rondt af.")
                self._stuur(200, {"ok": True})
            return

        if pad.startswith("/jobs/") and pad.endswith("/invoer"):
            # tekst naar een lopende taak (seriële monitor): {"tekst": "..."}
            job = self.jobs.haal(pad.split("/")[2])
            if job is None:
                self._stuur(404, {"fout": "Onbekende job."})
            elif job.stand != "bezig":
                self._stuur(409, {"fout": "De taak loopt niet meer."})
            else:
                job.stuur(str(self._body().get("tekst", "")))
                self._stuur(200, {"ok": True})
            return

        self._stuur(404, {"fout": "Onbekend pad."})

    def log_message(self, fmt, *args):  # noqa: D102
        log.info("%s %s", self.address_string(), fmt % args)


_drives_slot = threading.Lock()


def maak_server(inst: dict) -> ThreadingHTTPServer:
    Handler.inst = inst
    Handler.jobs = JobBeheer()
    server = ThreadingHTTPServer(("127.0.0.1", int(inst.get("poort", 5055))), Handler)
    server.daemon_threads = True
    return server


def draai(stop_event: threading.Event | None = None) -> None:
    inst = instellingen.lees()
    server = maak_server(inst)
    log.info("MDE Werkplek-agent %s luistert op http://127.0.0.1:%s", VERSIE, inst.get("poort", 5055))

    # De beschikbaarheidscontroles alvast in de achtergrond doen (ingeniamotion
    # importeren kost seconden), zodat /health meteen snel antwoordt.
    def opwarmen():
        for naam, mod in ALLE.items():
            try:
                ok, reden = mod.beschikbaar(inst)
                log.info("Taak %s: %s%s", naam, "beschikbaar" if ok else "niet beschikbaar", f" ({reden})" if reden else "")
            except Exception as e:  # noqa: BLE001
                log.warning("Taak %s: controle mislukt: %s", naam, e)
    threading.Thread(target=opwarmen, name="opwarmen", daemon=True).start()
    if stop_event is None:
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
    else:
        t = threading.Thread(target=server.serve_forever, daemon=True)
        t.start()
        stop_event.wait()
        server.shutdown()
    log.info("Agent gestopt.")
