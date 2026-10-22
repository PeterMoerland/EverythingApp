"""Startpunt voor MdeAgentTray.exe (PyInstaller, zonder console): het tray-icoon van de agent (mde_agent/tray.py)."""
import sys

from mde_agent.tray import main

if __name__ == "__main__":
    sys.exit(main())
