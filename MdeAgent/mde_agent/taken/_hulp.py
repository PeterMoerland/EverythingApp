"""Gedeelde hulpjes voor de flash-taken: firmware ophalen, werker-proces volgen."""

import os
import re
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

from .. import instellingen


def haal_firmware(url_of_pad: str, job, naam_hint: str = "firmware.bin") -> str:
    """Firmware van de app downloaden (of een lokaal pad accepteren); geeft lokaal pad."""
    if not url_of_pad:
        raise ValueError("Geen firmware opgegeven.")
    if os.path.isfile(url_of_pad):
        return url_of_pad
    if not re.match(r"^https?://", url_of_pad):
        raise ValueError(f"Firmware niet gevonden: {url_of_pad}")

    job.log(f"Bestand ophalen: {url_of_pad}")
    # De app haalt het bestand van een netwerkshare; die is af en toe een paar
    # seconden niet bereikbaar (404/5xx). Een paar keer proberen scheelt handmatig herstarten.
    r = None
    for poging, wacht in enumerate((0, 2, 4, 8), start=1):
        if wacht:
            job.log(f"  nog niet beschikbaar, opnieuw proberen over {wacht} s (poging {poging}) …")
            time.sleep(wacht)
        try:
            r = urllib.request.urlopen(url_of_pad, timeout=60)
            break
        except urllib.error.HTTPError as e:
            if e.code not in (404, 500, 502, 503, 504) or poging == 4:
                raise
        except urllib.error.URLError as e:
            # Certificaat niet vertrouwd: opnieuw proberen helpt niet. De app draait met de eigen CA van Caddy
            # (mde-root.crt); de agent is een service en kijkt alleen in de certificaten van de computer.
            if isinstance(e.reason, ssl.SSLCertVerificationError):
                raise RuntimeError(
                    "Het certificaat van de server wordt op deze pc niet vertrouwd (voor de agent-service). "
                    "Installeer het basiscertificaat voor de computer: draai installeer.bat opnieuw (zet het "
                    "vanaf de share erbij), of als beheerder: certutil -addstore Root mde-root.crt "
                    "(http://everything.mde-automation.nl/mde-root.crt). Daarna opnieuw proberen.") from e
            if poging == 4:
                raise
    assert r is not None
    with r:
        # De echte bestandsnaam staat in Content-Disposition (de URL eindigt op
        # /download). De extensie is belangrijk: ingeniamotion kijkt naar .lfu/.sfu.
        cd = r.headers.get("Content-Disposition", "")
        m = re.search(r"filename\*=UTF-8''([^;]+)", cd) or re.search(r'filename="?([^";]+)"?', cd)
        naam = urllib.parse.unquote(m.group(1)) if m else (os.path.basename(url_of_pad.split("?")[0]) or naam_hint)
        naam = re.sub(r"[^\w.\-]", "_", naam.strip())
        if "." not in naam:
            naam += os.path.splitext(naam_hint)[1] or ".bin"
        doel = os.path.join(instellingen.WERKMAP, f"{int(time.time())}_{naam}")
        with open(doel, "wb") as f:
            f.write(r.read())
    job.log(f"Bestand: {naam}, {os.path.getsize(doel):,} bytes.".replace(",", "."))
    return doel


def ruim_werkmap_op(max_leeftijd_uur: int = 24) -> None:
    grens = time.time() - max_leeftijd_uur * 3600
    try:
        for n in os.listdir(instellingen.WERKMAP):
            p = os.path.join(instellingen.WERKMAP, n)
            if os.path.isfile(p) and os.path.getmtime(p) < grens:
                os.remove(p)
    except OSError:
        pass


def werker_commando(*args: str) -> list[str]:
    """Commandoregel om deze agent als werker te starten (exe of `python -m`)."""
    basis = [sys.executable] if getattr(sys, "frozen", False) else [sys.executable, "-m", "mde_agent"]
    return basis + ["--werker", *args]


def volg_proces(cmd: list[str], job, timeout: int, voortgang_regex: str | None = None) -> int:
    """Een (werker)proces draaien en zijn uitvoer regel voor regel in de job-log zetten."""
    job.log("» " + " ".join(cmd[-6:]))
    proces = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              text=True, encoding="utf-8", errors="replace", bufsize=1,
                              creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    begin = time.time()
    patroon = re.compile(voortgang_regex) if voortgang_regex else None
    try:
        assert proces.stdout is not None
        for regel in proces.stdout:
            regel = regel.rstrip("\r\n")
            if not regel.strip():
                continue
            # esptool herschrijft dezelfde regel met \r; het laatste stuk telt
            regel = regel.split("\r")[-1]
            job.log(regel)
            if patroon:
                m = patroon.search(regel)
                if m:
                    try:
                        job.voortgang(int(float(m.group(1))))
                    except ValueError:
                        pass
            if time.time() - begin > timeout:
                proces.kill()
                raise TimeoutError(f"Proces duurde langer dan {timeout} s.")
        return proces.wait(timeout=10)
    finally:
        if proces.poll() is None:
            proces.kill()
