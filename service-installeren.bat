@echo off
REM ============================================================
REM  MDE App als Windows-service installeren.
REM  Dit script vraagt zelf om administratorrechten (UAC-venster).
REM ============================================================

REM --- 1. Draaien we verhoogd? 'net session' lukt alleen als administrator. ---
net session >nul 2>&1
if not errorlevel 1 goto :verhoogd

echo.
echo Dit script heeft administratorrechten nodig.
echo Windows vraagt er nu om -- kies "Ja".
echo.
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs" >nul 2>&1
if errorlevel 1 (
    echo.
    echo Verhogen is niet gelukt. Dat betekent bijna altijd dat dit account
    echo geen lid is van de groep Administrators op deze server.
    echo.
    echo Controleren kan met:   net localgroup Administrators
    echo.
    pause
)
exit /b

:verhoogd
setlocal
set SERVICE=MDEApp
set DISPLAY=MDE App
cd /d "%~dp0"

echo.
echo Uitgevoerd als: %USERDOMAIN%\%USERNAME% (verhoogd)

echo.
echo == Stap 1/4: app publiceren ==
dotnet publish -c Release -o "%~dp0publish"
if errorlevel 1 (
    echo.
    echo Publiceren mislukt. Controleer de foutmelding hierboven.
    pause
    exit /b 1
)

echo.
echo == Stap 2/4: bestaande service stoppen/verwijderen (indien aanwezig) ==
sc.exe stop %SERVICE% >nul 2>&1
sc.exe delete %SERVICE% >nul 2>&1

echo.
echo == Stap 3/4: service registreren ==
sc.exe create %SERVICE% binPath= "\"%~dp0publish\EverythingApp.exe\"" start= auto DisplayName= "%DISPLAY%"
if errorlevel 1 (
    echo.
    echo Registreren mislukt. Zie de foutmelding hierboven.
    pause
    exit /b 1
)
sc.exe description %SERVICE% "MDE bedrijfsapplicatie - Blazor Server (poort 5010)"
REM Automatisch herstarten na een onverwachte crash:
sc.exe failure %SERVICE% reset= 86400 actions= restart/5000/restart/5000/restart/60000

echo.
echo == Stap 4/4: service starten ==
sc.exe start %SERVICE%

echo.
echo Klaar. De app draait nu als service "%SERVICE%".
echo Bereikbaar op http://172.16.1.13:5010
echo Beheren kan via services.msc (starten/stoppen/opnieuw starten).
echo.
pause
