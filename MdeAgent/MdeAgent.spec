# -*- mode: python ; coding: utf-8 -*-
# PyInstaller-recept: één map dist\MdeAgent\ met MdeAgent.exe en alles wat erbij hoort.
# Bouwen: build.ps1 (of: pyinstaller MdeAgent.spec)

import os
from PyInstaller.utils.hooks import collect_all, collect_submodules

ICOON = "mde.ico" if os.path.exists("mde.ico") else None

datas, binaries, hiddenimports = [], [], []
for pakket in ("esptool", "pyocd", "cmsis_pack_manager", "pysoem", "serial", "ingeniamotion", "ingenialink"):
    try:
        d, b, h = collect_all(pakket)
        datas += d; binaries += b; hiddenimports += h
    except Exception:
        pass

# Deze moeten er echt in; ontbreken ze in de venv, dan hoort de build te stoppen
# in plaats van stil een agent zonder drive-taken op te leveren.
import importlib
for verplicht in ("pysoem", "esptool", "ingeniamotion", "ingenialink"):
    importlib.import_module(verplicht)

hiddenimports += [
    "ingeniamotion", "ingenialink",
    "win32timezone", "servicemanager", "win32event", "win32service", "win32serviceutil",
    "mde_agent.taken.drives", "mde_agent.taken.esp32", "mde_agent.taken.stm32",
] + collect_submodules("mde_agent")

a = Analysis(
    ["start.py"],
    pathex=["."],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    runtime_hooks=[],
    excludes=["tkinter", "matplotlib", "numpy.tests", "scipy.tests"],   # numpy.f2py NIET uitsluiten: ingeniamotion/scipy hebben het bij het laden nodig
    noarchive=False,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz, a.scripts, [],
    exclude_binaries=True,
    name="MdeAgent",
    icon=ICOON,
    console=True,
    uac_admin=False,      # de service draait als LocalSystem; de installer regelt de rechten
)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="MdeAgent")
