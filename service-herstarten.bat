@echo off
REM ============================================================
REM  Nieuwe versie uitrollen: stoppen, publiceren, starten.
REM  Dit is het enige script dat je na een wijziging nodig hebt.
REM
REM  Verwijderen en opnieuw installeren hoeft niet meer: dit script
REM  wacht echt tot de service gestopt is (in plaats van een vast
REM  aantal seconden), en maakt de service aan als hij nog niet
REM  bestaat. Vraagt zelf om administratorrechten.
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
    echo Controleren kan met:   net localgroup Administrators
    pause
)
exit /b

:verhoogd
setlocal EnableDelayedExpansion
set SERVICE=MDEApp
set DISPLAY=MDE App
set EXE=%~dp0publish\EverythingApp.exe
cd /d "%~dp0"

echo.
echo Uitgevoerd als: %USERDOMAIN%\%USERNAME% (verhoogd)

REM ------------------------------------------------------------
REM  Bestaat de service al?
REM ------------------------------------------------------------
set NIEUW=0
sc.exe query %SERVICE% >nul 2>&1
if not errorlevel 1 goto :stoppen

set NIEUW=1
echo.
echo De service bestaat nog niet; die maak ik straks aan.
goto :publiceren

REM ------------------------------------------------------------
REM  0. Gebruikers waarschuwen: iedereen die de app open heeft krijgt
REM     bovenin een melding met een aftelklok (POST naar de app zelf,
REM     alleen vanaf deze server toegestaan). Draait de app niet, dan
REM     valt er niemand te waarschuwen.
REM ------------------------------------------------------------
:stoppen
sc.exe query %SERVICE% | find "RUNNING" >nul
if errorlevel 1 goto :stoppennu

echo.
echo == Gebruikers waarschuwen ==
echo Iedereen die de app open heeft, krijgt bovenin een melding met een aftelklok.
set WACHT=3
set /p WACHT=Over hoeveel minuten stopt de app? [Enter = 3, 0 = niet waarschuwen]:
set /a WACHT=WACHT
if !WACHT! LEQ 0 goto :stoppennu
powershell -NoProfile -Command "try { Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:5010/onderhoud/aankondigen?minuten=!WACHT!' -TimeoutSec 10 } catch { 'Waarschuwen lukte niet (' + $_.Exception.Message + '); ik ga gewoon door.' }"
set /a WACHTSEC=WACHT*60
echo.
echo Wachten tot de tijd om is. Een toets = nu doorgaan.
timeout /t !WACHTSEC!

REM ------------------------------------------------------------
REM  1. Stoppen en wachten tot hij echt stil is.
REM     Zonder dit wachten is EverythingApp.exe nog in gebruik en
REM     mislukt het publiceren -- de reden om anders maar te
REM     verwijderen en opnieuw te installeren.
REM ------------------------------------------------------------
:stoppennu
echo.
echo == Service stoppen ==
sc.exe stop %SERVICE% >nul 2>&1
set /a POGING=0

:wacht
sc.exe query %SERVICE% | find "STOPPED" >nul
if not errorlevel 1 goto :gestopt

set /a POGING+=1
if !POGING! GEQ 30 goto :afbreken

echo    wachten... (!POGING!)
timeout /t 1 /nobreak >nul
goto :wacht

:afbreken
echo    Hij gaat niet uit zichzelf uit; ik beeindig het proces.
taskkill /f /im EverythingApp.exe >nul 2>&1
timeout /t 2 /nobreak >nul

:gestopt
echo    Gestopt.
REM Windows laat het bestand soms nog even los na 'STOPPED'.
timeout /t 2 /nobreak >nul

REM ------------------------------------------------------------
REM  2. Publiceren
REM ------------------------------------------------------------
:publiceren
echo.
echo == Vorige publicatie opruimen ==
REM Weggooien in plaats van overschrijven. Blijven er oude bestanden staan, dan
REM draait de app straks deels op de vorige versie. Instellingen en sleutels
REM staan niet hier maar in C:\ProgramData\MDE, dus die gaan niet verloren.
REM Eerst robocopy een lege map eroverheen spiegelen. Een gewone rmdir struikelt
REM over paden langer dan 260 tekens, en juist die zijn er ontstaan toen publish
REM zichzelf steeds dieper kopieerde.
if exist "%~dp0publish" (
    if not exist "%TEMP%\mde-leeg" mkdir "%TEMP%\mde-leeg"
    robocopy "%TEMP%\mde-leeg" "%~dp0publish" /MIR /NFL /NDL /NJH /NJS /NC /NS >nul
    rmdir /s /q "%~dp0publish" 2>nul
    rmdir /s /q "%TEMP%\mde-leeg" 2>nul
)
if exist "%~dp0publish" (
    echo.
    echo De map publish kon niet worden verwijderd. Meestal houdt een programma
    echo nog een bestand vast. Sluit verkenner-vensters in die map en probeer
    echo het opnieuw.
    echo.
    pause
    exit /b 1
)

echo.
echo == Publiceren ==
dotnet publish -c Release -o "%~dp0publish"
if not errorlevel 1 goto :registreren

echo.
echo Publiceren mislukt. De oude versie staat er nog.
if "%NIEUW%"=="0" echo Ik start de service weer, zodat iedereen door kan werken.
if "%NIEUW%"=="0" sc.exe start %SERVICE% >nul 2>&1
echo.
pause
exit /b 1

REM ------------------------------------------------------------
REM  3. Registreren, alleen als de service nog niet bestond
REM ------------------------------------------------------------
:registreren
if "%NIEUW%"=="0" goto :starten

echo.
echo == Service registreren ==
sc.exe create %SERVICE% binPath= "\"%EXE%\"" start= auto DisplayName= "%DISPLAY%"
if errorlevel 1 goto :registrerenmislukt

sc.exe description %SERVICE% "MDE bedrijfsapplicatie - Blazor Server (poort 5010)"
REM Automatisch herstarten na een onverwachte crash:
sc.exe failure %SERVICE% reset= 86400 actions= restart/5000/restart/5000/restart/60000

echo.
echo LET OP: een nieuw aangemaakte service draait onder LocalSystem en kan dan
echo niet bij netwerkmappen zoals \\172.16.1.4\data (foto's, filestore, laser).
echo Draai daarna service-account.bat om hem onder een gebruikersaccount te zetten.
goto :starten

:registrerenmislukt
echo.
echo Registreren mislukt. Zie de foutmelding hierboven.
echo.
pause
exit /b 1

REM ------------------------------------------------------------
REM  4. Starten en controleren of hij het ook echt doet
REM ------------------------------------------------------------
:starten
echo.
echo == Service starten ==
REM Mocht de service ooit op 'Uitgeschakeld' zijn gezet (foutcode 1058), dan
REM zet dit hem terug op automatisch starten. Kost niets als het al goed staat.
sc.exe config %SERVICE% start= auto >nul

REM De uitvoer niet wegmoffelen: als het misgaat staat hier de foutcode.
sc.exe start %SERVICE%
set /a POGING=0

:wachtstart
sc.exe query %SERVICE% | find "RUNNING" >nul
if not errorlevel 1 goto :draait

set /a POGING+=1
if !POGING! GEQ 20 goto :startmislukt

timeout /t 1 /nobreak >nul
goto :wachtstart

:startmislukt
echo.
echo De service is niet gestart.
echo.
echo Kijk eerst hier:  C:\ProgramData\MDE\logboek\fouten.txt
echo.
echo Staat daar niets, start de app dan met de hand -- dan komt de fout gewoon
echo in beeld in plaats van dat hij in de service verdwijnt:
echo.
echo    "%~dp0publish\EverythingApp.exe"
echo.
echo Foutmelding 1053 komt meestal door een fout in appsettings.json; let op de
echo backslashes, die moeten dubbel ("\\server\\map"). Foutmelding 5 betekent dat
echo het serviceaccount niet bij de programmamap mag, 1069 dat het wachtwoord van
echo dat account niet klopt.
echo.
pause
exit /b 1

:draait
echo.
echo Klaar. De app draait. http://172.16.1.13:5010
echo.
pause
