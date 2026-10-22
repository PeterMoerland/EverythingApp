"""
Tray-icoon voor de MDE Werkplek-agent: MdeAgentTray.exe (geen console), start met Windows voor elke gebruiker
(snelkoppeling in de gemeenschappelijke map Opstarten, zie installer.iss).

De agent zelf is een Windows-service (LocalSystem); dit icoon draait in de sessie van de gebruiker en praat met de
agent via http://127.0.0.1:<poort>, net als de app. Kleur: groen = klaar, blauw = bezig met een taak, grijs = service
gestopt, rood = agent antwoordt niet. Rechtsklik:
  - status (versie, pc, lopende taak)
  - Lopende taak stoppen          POST /jobs/<id>/stop met het token uit agent.json (map is voor gebruikers leesbaar)
  - Agent herstarten / stoppen / starten
                                  via de servicebeheerder met alleen start/stop-rechten; de installer geeft ingelogde
                                  gebruikers die rechten op de service MdeAgent (sc sdset, besluit gebruiker 22-10-2026)
  - Logboek openen, EverythingApp openen, tray-icoon sluiten (de agent blijft draaien)
"""

import json
import os
import threading
import time
import urllib.request
import webbrowser

from . import VERSIE, instellingen

SERVICE = "MdeAgent"
APP_URL = "https://everything.mde-automation.nl"
KLEUREN = {"klaar": "#2f9e44", "bezig": "#1f6feb", "gestopt": "#868e96", "fout": "#c92a2a"}


class _Stand:
    """Wat het icoon nu weet; bijgewerkt door de peildraad."""

    def __init__(self):
        self.stand = "fout"          # klaar | bezig | gestopt | fout
        self.versie = ""
        self.pc = ""
        self.job = None              # id van de lopende taak
        self.taak = ""               # naam van de lopende taak
        self.service = "onbekend"    # loopt | gestopt | start | stopt | niet geïnstalleerd | onbekend
        self.melding = ""


def _poort_en_token() -> tuple[int, str]:
    try:
        inst = instellingen.lees()
        return int(inst.get("poort") or 5055), str(inst.get("token") or "")
    except Exception:  # noqa: BLE001
        return 5055, ""


def _http(pad: str, methode: str = "GET", body: dict | None = None, token: str = "", timeout: float = 3.0) -> dict:
    poort, _ = _poort_en_token()
    data = json.dumps(body or {}).encode() if methode == "POST" else None
    verzoek = urllib.request.Request(f"http://127.0.0.1:{poort}{pad}", data=data, method=methode)
    verzoek.add_header("Content-Type", "application/json")
    if token:
        verzoek.add_header("X-Agent-Token", token)
    with urllib.request.urlopen(verzoek, timeout=timeout) as antwoord:
        return json.loads(antwoord.read().decode("utf-8") or "{}")


# ---- de service, met alleen de rechten die de installer gebruikers geeft (niet SERVICE_ALL_ACCESS) ----

def _service_stand() -> str:
    import win32service
    try:
        scm = win32service.OpenSCManager(None, None, win32service.SC_MANAGER_CONNECT)
        try:
            svc = win32service.OpenService(scm, SERVICE, win32service.SERVICE_QUERY_STATUS)
        except Exception:  # noqa: BLE001
            return "niet geïnstalleerd"
        try:
            toestand = win32service.QueryServiceStatus(svc)[1]
        finally:
            win32service.CloseServiceHandle(svc)
            win32service.CloseServiceHandle(scm)
    except Exception:  # noqa: BLE001
        return "onbekend"
    return {win32service.SERVICE_RUNNING: "loopt", win32service.SERVICE_STOPPED: "gestopt",
            win32service.SERVICE_START_PENDING: "start", win32service.SERVICE_STOP_PENDING: "stopt"}.get(toestand, "onbekend")


def _service(actie: str) -> None:
    """actie: start | stop | herstart. Geeft een leesbare fout als de rechten ontbreken."""
    import win32service
    rechten = win32service.SERVICE_START | win32service.SERVICE_STOP | win32service.SERVICE_QUERY_STATUS
    scm = win32service.OpenSCManager(None, None, win32service.SC_MANAGER_CONNECT)
    try:
        try:
            svc = win32service.OpenService(scm, SERVICE, rechten)
        except Exception as e:  # noqa: BLE001
            raise RuntimeError("Geen rechten om de agent te starten of te stoppen. Installeer de agent opnieuw "
                               f"(installeer.bat) zodat gebruikers dat mogen. ({e})") from e
        try:
            if actie in ("stop", "herstart") and win32service.QueryServiceStatus(svc)[1] != win32service.SERVICE_STOPPED:
                win32service.ControlService(svc, win32service.SERVICE_CONTROL_STOP)
                einde = time.time() + 30
                while time.time() < einde and win32service.QueryServiceStatus(svc)[1] != win32service.SERVICE_STOPPED:
                    time.sleep(0.5)
            if actie in ("start", "herstart"):
                win32service.StartService(svc, None)
        finally:
            win32service.CloseServiceHandle(svc)
    finally:
        win32service.CloseServiceHandle(scm)


# ---- het icoon ----

def _plaatje(kleur: str):
    from PIL import Image, ImageDraw, ImageFont
    beeld = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    teken = ImageDraw.Draw(beeld)
    teken.ellipse((2, 2, 62, 62), fill=kleur)
    try:
        letter = ImageFont.truetype("arialbd.ttf", 36)
    except Exception:  # noqa: BLE001
        letter = ImageFont.load_default()
    teken.text((32, 33), "M", fill="white", font=letter, anchor="mm")
    return beeld


def main() -> int:
    import ctypes

    import pystray

    # één icoon per gebruiker
    import win32event
    mutex = win32event.CreateMutex(None, False, "Local\\MdeAgentTray")  # noqa: F841  (moet blijven bestaan)
    if ctypes.windll.kernel32.GetLastError() == 183:      # ERROR_ALREADY_EXISTS
        return 0

    stand = _Stand()
    plaatjes = {k: _plaatje(v) for k, v in KLEUREN.items()}
    stoppen = threading.Event()

    def standtekst() -> str:
        return {"klaar": "klaar", "bezig": f"bezig: {stand.taak or 'taak'}", "gestopt": "service gestopt",
                "fout": "antwoordt niet"}[stand.stand]

    def peil() -> None:
        while not stoppen.is_set():
            stand.service = _service_stand()
            try:
                h = _http("/health", timeout=2)
                stand.versie, stand.pc = h.get("versie") or "", h.get("pc") or ""
                stand.job = h.get("bezig")
                if stand.job:
                    _, token = _poort_en_token()
                    try:
                        stand.taak = _http(f"/jobs/{stand.job}", token=token, timeout=2).get("taak") or ""
                    except Exception:  # noqa: BLE001
                        stand.taak = ""
                stand.stand = "bezig" if stand.job else "klaar"
            except Exception:  # noqa: BLE001
                stand.job, stand.taak = None, ""
                stand.stand = "gestopt" if stand.service in ("gestopt", "stopt") else "fout"
            icoon.icon = plaatjes[stand.stand]
            icoon.title = f"MDE Werkplek-agent {stand.versie or VERSIE} — {standtekst()}"[:127]
            icoon.update_menu()
            stoppen.wait(3)

    def op_achtergrond(werk, gelukt: str):
        def draai():
            try:
                werk()
                icoon.notify(gelukt, "MDE Werkplek-agent")
            except Exception as e:  # noqa: BLE001
                icoon.notify(str(e)[:250], "MDE Werkplek-agent: niet gelukt")
        threading.Thread(target=draai, daemon=True).start()

    def taak_stoppen(_icoon, _item):
        job = stand.job
        if not job:
            return
        _, token = _poort_en_token()
        op_achtergrond(lambda: _http(f"/jobs/{job}/stop", "POST", {}, token=token),
                       f"Stop gevraagd voor {stand.taak or 'de lopende taak'}; die rondt nu af.")

    def herstarten(_icoon, _item):
        op_achtergrond(lambda: _service("herstart"), "De agent is herstart.")

    def stop_service(_icoon, _item):
        op_achtergrond(lambda: _service("stop"), "De agent is gestopt.")

    def start_service(_icoon, _item):
        op_achtergrond(lambda: _service("start"), "De agent is gestart.")

    def logboek(_icoon, _item):
        bestand = os.path.join(instellingen.LOGMAP, "agent.log")
        try:
            os.startfile(bestand if os.path.exists(bestand) else instellingen.LOGMAP)  # noqa: S606
        except Exception:  # noqa: BLE001
            pass

    def app_openen(_icoon, _item):
        webbrowser.open(APP_URL)

    def afsluiten(_icoon, _item):
        stoppen.set()
        icoon.stop()

    loopt = lambda _i: stand.service in ("loopt", "start")       # noqa: E731
    menu = pystray.Menu(
        pystray.MenuItem(lambda _i: f"MDE Werkplek-agent {stand.versie or VERSIE}", None, enabled=False),
        pystray.MenuItem(lambda _i: f"Stand: {standtekst()}" + (f" · {stand.pc}" if stand.pc else ""), None, enabled=False),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem("Lopende taak stoppen", taak_stoppen, enabled=lambda _i: bool(stand.job)),
        pystray.MenuItem("Agent herstarten", herstarten, enabled=lambda _i: stand.service != "niet geïnstalleerd"),
        pystray.MenuItem("Agent stoppen", stop_service, visible=loopt),
        pystray.MenuItem("Agent starten", start_service, visible=lambda _i: not loopt(_i)),
        pystray.Menu.SEPARATOR,
        pystray.MenuItem("Logboek openen", logboek),
        pystray.MenuItem("EverythingApp openen", app_openen),
        pystray.MenuItem("Tray-icoon sluiten (agent blijft draaien)", afsluiten),
    )
    icoon = pystray.Icon("MdeAgentTray", plaatjes["fout"], f"MDE Werkplek-agent {VERSIE}", menu)
    threading.Thread(target=peil, daemon=True).start()
    icoon.run()
    return 0
