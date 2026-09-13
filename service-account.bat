@echo off
REM ============================================================
REM  De MDE-service onder een gebruikersaccount laten draaien, zodat
REM  hij bij \\172.16.1.4\data kan (foto's, filestore, laser-PDF).
REM
REM  Het echte werk staat in service-account.ps1 ernaast; dit bestand
REM  regelt alleen de administratorrechten.
REM ============================================================

net session >nul 2>&1
if not errorlevel 1 goto :verhoogd

echo.
echo Dit script heeft administratorrechten nodig. Windows vraagt er nu om.
echo.
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs" >nul 2>&1
if errorlevel 1 (
    echo.
    echo Verhogen is niet gelukt -- dit account is geen administrator op deze server.
    pause
)
exit /b

:verhoogd
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0service-account.ps1"
