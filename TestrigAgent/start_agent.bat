@echo off
rem Start de testrig-agent (als administrator — nodig voor Npcap/EtherCAT).
rem Zet dit bestand samen met drive_agent.py, agent.json en read_drive_serials.py
rem in één map op de testrig-pc, bijvoorbeeld C:\tools\drive-serials\.

cd /d "%~dp0"

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Dit script moet als administrator draaien. Opnieuw starten met verhoogde rechten...
    powershell -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
    exit /b
)

rem Staat er een venv naast (met pysoem erin), gebruik die; anders de systeem-Python.
set PY=python
if exist "%~dp0.venv\Scripts\python.exe" set PY=%~dp0.venv\Scripts\python.exe
if exist "%~dp0venv\Scripts\python.exe"  set PY=%~dp0venv\Scripts\python.exe

"%PY%" -c "import pysoem" 2>nul || (
    echo pysoem ontbreekt in %PY%. Installeer met:  "%PY%" -m pip install pysoem
    echo Lukt dat niet ^(geen wheel voor deze Python-versie^), maak dan een venv met Python 3.12:
    echo    py -3.12 -m venv "%~dp0.venv"  ^&^&  "%~dp0.venv\Scripts\pip" install pysoem
    pause
    exit /b 1
)

echo Sluit MotionLab3 voordat je een print uitleest.
"%PY%" drive_agent.py
pause
