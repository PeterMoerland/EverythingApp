"""
Testrig-agent: het bruggetje tussen de EverythingApp (in de browser) en de
EtherCAT-master op deze pc.

De app draait op de server en kan niet bij de netwerkadapter van de testrig.
Dit agentje draait wél op de testrig-pc, luistert alleen op localhost, en
leest op verzoek de drives uit met scan_identity() uit read_drive_serials.py.
De browser op deze pc haalt het antwoord op (fetch naar http://localhost:5055)
en geeft het aan de app, die het bij de print in de database zet.

Endpoints (alle GET, antwoord JSON):
  /health            -> {"ok": true, "interface": "..."}
  /interfaces        -> [{"name": "...", "desc": "..."}]   (pysoem.find_adapters)
  /drives[?ifname=]  -> {"drives": [...], "errors": [...], "interface": "...", "adapter": "..."}

Welke adapter? Zonder instelling gaat de agent alle netwerkadapters af en
onthoudt de eerste waarop drives antwoorden (interface_gevonden in agent.json).
Antwoordt die later niet meer, dan zoekt hij opnieuw. Handmatig vastzetten kan
nog steeds via "interface".

Het uitlezen gebeurt in een apart proces (deze zelfde file met --scan), zodat
de EtherCAT-master kort leeft en de adapter meteen weer vrijgeeft — en zodat
een vastloper in pysoem nooit de agent zelf meeneemt.

Vereisten: Npcap (WinPcap-modus), admin-rechten, MotionLab3 dicht.
Instellingen: agent.json naast dit bestand, zie start_agent.bat.
"""

import json
import os
import subprocess
import sys
import threading
from dataclasses import asdict, is_dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

HIER = os.path.dirname(os.path.abspath(__file__))
INSTELLINGEN = os.path.join(HIER, "agent.json")

STANDAARD = {
    "poort": 5055,
    "interface": "",                                   # pysoem-adapternaam, leeg = eerste
    "toegestaan": ["https://everything.mde-automation.nl", "http://172.16.1.13:5010"],
    "timeout_seconden": 60,
}


def lees_instellingen() -> dict:
    inst = dict(STANDAARD)
    if os.path.exists(INSTELLINGEN):
        with open(INSTELLINGEN, encoding="utf-8-sig") as f:
            tekst = f.read()
        try:
            inst.update(json.loads(tekst))
        except json.JSONDecodeError:
            # Adapternamen als \Device\NPF_{...} worden vaak met enkele
            # backslashes ingetypt; in JSON moeten dat dubbele zijn. Vergeven.
            import re
            tekst = re.sub(r'\\(?!["\\/bfnrtu])', r'\\\\', tekst)
            inst.update(json.loads(tekst))
    return inst


# ---------------------------------------------------------------------------
#  Werker: draait in een eigen proces en drukt JSON af
# ---------------------------------------------------------------------------

def _naar_dict(record):
    if is_dataclass(record):
        return asdict(record)
    return {k: getattr(record, k, None) for k in
            ("slave_position", "name", "vendor_id", "product_code",
             "revision_number", "serial_number", "errors")}


def scan(ifname: str) -> dict:
    """Leest de drives op de adapter uit via read_drive_serials.scan_identity."""
    try:
        sys.path.insert(0, HIER)
        from read_drive_serials import scan_identity  # noqa: WPS433
    except Exception as e:  # noqa: BLE001
        return {"drives": [], "errors": [f"read_drive_serials.py niet te laden: {e}"],
                "interface": ifname}

    try:
        records = scan_identity(ifname)
        drives = [_naar_dict(r) for r in records]
        # getallen leesbaar maken (vendor/product code als hex-tekst)
        for d in drives:
            for veld in ("vendor_id", "product_code", "revision_number"):
                if isinstance(d.get(veld), int):
                    d[veld] = f"0x{d[veld]:08X}"
            if d.get("serial_number") is not None and not isinstance(d["serial_number"], str):
                d["serial_number"] = str(d["serial_number"])
        return {"drives": drives, "errors": [], "interface": ifname}
    except Exception as e:  # noqa: BLE001
        return {"drives": [], "errors": [f"Uitlezen mislukt: {e}"], "interface": ifname}


def interfaces() -> list:
    try:
        import pysoem  # noqa: WPS433
        return [{"name": a.name, "desc": a.desc} for a in pysoem.find_adapters()]
    except Exception as e:  # noqa: BLE001
        return [{"name": "", "desc": f"pysoem niet beschikbaar: {e}"}]


# ---------------------------------------------------------------------------
#  Server
# ---------------------------------------------------------------------------

_slot = threading.Lock()   # nooit twee scans tegelijk op één adapter


class Handler(BaseHTTPRequestHandler):
    inst: dict = {}

    def _stuur(self, code: int, inhoud, herkomst: str | None) -> None:
        tekst = json.dumps(inhoud, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(tekst)))
        self._cors(herkomst)
        self.end_headers()
        self.wfile.write(tekst)

    def _cors(self, herkomst: str | None) -> None:
        toegestaan = self.inst.get("toegestaan") or []
        if herkomst and (herkomst in toegestaan or "*" in toegestaan):
            self.send_header("Access-Control-Allow-Origin", herkomst)
            self.send_header("Vary", "Origin")

    def do_OPTIONS(self) -> None:  # noqa: N802
        self.send_response(204)
        self._cors(self.headers.get("Origin"))
        self.send_header("Access-Control-Allow-Methods", "GET, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        herkomst = self.headers.get("Origin")
        url = urlparse(self.path)
        vraag = parse_qs(url.query)

        if url.path == "/health":
            self._stuur(200, {"ok": True, "interface": self.inst.get("interface", "")}, herkomst)
            return

        if url.path == "/interfaces":
            self._stuur(200, interfaces(), herkomst)
            return

        if url.path == "/drives":
            # Volgorde: ?ifname=, anders de eerder gevonden adapter, anders de
            # ingestelde, anders alle adapters afgaan tot er drives antwoorden.
            gevraagd = (vraag.get("ifname") or [""])[0]
            ifname = gevraagd or self.inst.get("interface_gevonden", "") or self.inst.get("interface", "")

            if not _slot.acquire(blocking=False):
                self._stuur(409, {"drives": [], "errors": ["Er loopt al een uitlezing."],
                                  "interface": ifname}, herkomst)
                return
            try:
                if ifname:
                    inhoud = self._scan_proces(ifname)
                    # Adapter kwijt (kabel verhangen, andere pc)? Dan toch alles proberen.
                    if not inhoud["drives"] and not gevraagd:
                        inhoud = self._zoek_alle(inhoud)
                else:
                    inhoud = self._zoek_alle(None)
                self._stuur(200, inhoud, herkomst)
            finally:
                _slot.release()
            return

        self._stuur(404, {"errors": ["onbekend pad"]}, herkomst)

    # -- uitlezen -----------------------------------------------------------

    def _scan_proces(self, ifname: str) -> dict:
        """Eén adapter uitlezen in een apart proces."""
        try:
            uitkomst = subprocess.run(
                [sys.executable, os.path.abspath(__file__), "--scan", ifname],
                capture_output=True, text=True, encoding="utf-8",
                timeout=int(self.inst.get("timeout_seconden", 60)))
            try:
                return json.loads(uitkomst.stdout.strip().splitlines()[-1])
            except Exception:  # noqa: BLE001
                return {"drives": [], "interface": ifname,
                        "errors": ["Werker gaf geen JSON terug: " +
                                   (uitkomst.stderr or uitkomst.stdout)[-800:]]}
        except subprocess.TimeoutExpired:
            return {"drives": [], "interface": ifname,
                    "errors": ["Uitlezen duurde te lang (time-out)."]}

    def _zoek_alle(self, eerder: dict | None) -> dict:
        """Alle adapters afgaan; de eerste met drives wint en wordt onthouden."""
        geprobeerd = []
        for a in interfaces():
            if not a["name"]:
                continue
            if eerder and a["name"] == eerder.get("interface"):
                continue  # die hadden we al
            print(f"  zoeken op {a['desc']} ...")
            inhoud = self._scan_proces(a["name"])
            geprobeerd.append({"interface": a["name"], "desc": a["desc"],
                               "drives": len(inhoud["drives"]), "errors": inhoud["errors"]})
            if inhoud["drives"]:
                inhoud["gezocht"] = geprobeerd
                inhoud["adapter"] = a["desc"]
                self._onthoud(a["name"])
                return inhoud
        return {"drives": [], "interface": "",
                "errors": ["Op geen enkele netwerkadapter antwoorden EtherCAT-drives. "
                           "Zit de kabel erin, staat de print onder spanning en is MotionLab3 dicht?"],
                "gezocht": geprobeerd}

    def _onthoud(self, ifname: str) -> None:
        """De adapter waarop het lukte in agent.json zetten voor de volgende keer."""
        if self.inst.get("interface_gevonden") == ifname:
            return
        self.inst["interface_gevonden"] = ifname
        try:
            with open(INSTELLINGEN, "w", encoding="utf-8") as f:
                json.dump(self.inst, f, indent=2, ensure_ascii=False)
            print(f"  adapter onthouden: {ifname}")
        except OSError as e:
            print(f"  kon agent.json niet bijwerken: {e}")

    def log_message(self, fmt, *args):  # noqa: D102
        print("%s - %s" % (self.address_string(), fmt % args))


def main() -> None:
    if len(sys.argv) >= 3 and sys.argv[1] == "--scan":
        print(json.dumps(scan(sys.argv[2]), ensure_ascii=False))
        return

    inst = lees_instellingen()
    Handler.inst = inst
    server = ThreadingHTTPServer(("127.0.0.1", int(inst["poort"])), Handler)
    print(f"Testrig-agent luistert op http://127.0.0.1:{inst['poort']}  "
          f"(adapter: {inst['interface'] or 'eerste gevonden'}) — Ctrl+C om te stoppen")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
