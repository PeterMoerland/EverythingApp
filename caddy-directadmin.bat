@echo off
REM ============================================================================
REM Caddy (C:\Caddy) uitbreiden met de DirectAdmin-DNS-plugin. Het werk zit in
REM caddy-directadmin.ps1 (zelfde map); dit start het als beheerder.
REM Draai dit OP DE SERVER (de share werkt ook).
REM ============================================================================

net session >nul 2>&1
if errorlevel 1 (
    echo Opnieuw starten als beheerder...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0caddy-directadmin.ps1"
pause
