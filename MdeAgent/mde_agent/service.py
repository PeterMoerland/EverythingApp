"""
Windows-service om de agent. Draait onder LocalSystem (nodig voor Npcap/EtherCAT
en USB-programmers), start automatisch mee met Windows.

  MdeAgent.exe --service install     registreren (automatisch starten)
  MdeAgent.exe --service start|stop|restart|remove
  MdeAgent.exe --service debug       als service-code, maar in de console
"""

import logging
import sys
import threading

import servicemanager
import win32event
import win32service
import win32serviceutil

from . import instellingen

NAAM = "MdeAgent"
WEERGAVE = "MDE Werkplek-agent"
OMSCHRIJVING = ("Lokale hulpdienst voor de EverythingApp: EtherCAT-drives uitlezen, "
                "ESP32 en STM32 flashen. Luistert alleen op 127.0.0.1.")


class Dienst(win32serviceutil.ServiceFramework):
    _svc_name_ = NAAM
    _svc_display_name_ = WEERGAVE
    _svc_description_ = OMSCHRIJVING

    def __init__(self, args):
        super().__init__(args)
        self.stop_event = threading.Event()
        self.wacht = win32event.CreateEvent(None, 0, 0, None)

    def SvcStop(self):  # noqa: N802
        self.ReportServiceStatus(win32service.SERVICE_STOP_PENDING)
        self.stop_event.set()
        win32event.SetEvent(self.wacht)

    def SvcDoRun(self):  # noqa: N802
        from .__main__ import _logging
        _logging(naar_console=False)
        # Als service is er geen console: stdout/stderr zijn None en elke print()
        # of traceback zou dan zelf een fout geven. Naar nul leiden.
        import os
        for naam in ("stdout", "stderr"):
            if getattr(sys, naam) is None:
                setattr(sys, naam, open(os.devnull, "w", encoding="utf-8"))
        servicemanager.LogMsg(servicemanager.EVENTLOG_INFORMATION_TYPE,
                              servicemanager.PYS_SERVICE_STARTED, (self._svc_name_, ""))
        try:
            from . import server
            server.draai(self.stop_event)
        except Exception:  # noqa: BLE001
            logging.getLogger("mde_agent").exception("Service gestopt door fout")
            raise


def main(argv: list[str]) -> int:
    # PyInstaller: geen pythonservice.exe, dus zelf de service-executable opgeven.
    if getattr(sys, "frozen", False):
        Dienst._exe_name_ = sys.executable
        Dienst._exe_args_ = "--service"

    if not argv:
        # Door de Service Control Manager gestart.
        servicemanager.Initialize()
        servicemanager.PrepareToHostSingle(Dienst)
        servicemanager.StartServiceCtrlDispatcher()
        return 0

    if argv[0] == "install":
        instellingen.lees()   # agent.json + token aanmaken vóór de service draait
        win32serviceutil.InstallService(
            None if getattr(sys, "frozen", False) else win32serviceutil.GetServiceClassString(Dienst),
            NAAM, WEERGAVE, startType=win32service.SERVICE_AUTO_START,
            exeName=Dienst._exe_name_ if getattr(sys, "frozen", False) else None,
            exeArgs=Dienst._exe_args_ if getattr(sys, "frozen", False) else None,
            description=OMSCHRIJVING)
        print(f"Service '{WEERGAVE}' geïnstalleerd.")
        return 0

    win32serviceutil.HandleCommandLine(Dienst, argv=["MdeAgent", *argv])
    return 0
