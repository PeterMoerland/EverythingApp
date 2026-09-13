@echo off
setlocal
REM ============================================================================
REM Https aanzetten op de bestaande Caddy in C:\Caddy.
REM
REM Doet: nieuwe Caddyfile op zijn plek (met reservekopie), poort 443 open,
REM configuratie herladen zonder onderbreking, en het basiscertificaat
REM klaarzetten zodat pc's en telefoons het kunnen installeren.
REM
REM Draai dit script OP DE SERVER, vanaf deze map (de share werkt ook).
REM ============================================================================

REM ---- als beheerder draaien -------------------------------------------------
net session >nul 2>&1
if errorlevel 1 (
    echo Opnieuw starten als beheerder...
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

set MAP=C:\Caddy
set EXE=%MAP%\caddy.exe
set CONF=%MAP%\Caddyfile

if not exist "%EXE%" (
    echo FOUT: %EXE% niet gevonden. Draai dit script op de server.
    pause
    exit /b 1
)

REM ---- reservekopie en nieuwe configuratie ------------------------------------
if not exist "%MAP%\publiek" mkdir "%MAP%\publiek"
if exist "%CONF%" copy /y "%CONF%" "%CONF%.oud" >nul
copy /y "%~dp0Caddyfile" "%CONF%" >nul
echo Nieuwe Caddyfile geplaatst; de oude staat in Caddyfile.oud

REM ---- controle: klopt de configuratie? ---------------------------------------
"%EXE%" validate --config "%CONF%"
if errorlevel 1 (
    echo FOUT: de nieuwe Caddyfile is niet geldig; de oude wordt teruggezet.
    copy /y "%CONF%.oud" "%CONF%" >nul
    pause
    exit /b 1
)

REM ---- firewall: 443 erbij (80 staat al open, anders werkte http nu ook niet) --
netsh advfirewall firewall show rule name="MDE Caddy https" >nul 2>&1
if errorlevel 1 (
    netsh advfirewall firewall add rule name="MDE Caddy https" dir=in action=allow protocol=TCP localport=443 >nul
    echo Firewall-regel voor 443 toegevoegd.
)

REM ---- herladen zonder onderbreking --------------------------------------------
REM 'reload' praat met de draaiende Caddy; hoe die ooit gestart is maakt
REM daarvoor niet uit, en niemand merkt er iets van.
"%EXE%" reload --config "%CONF%"
if errorlevel 1 (
    echo FOUT: herladen mislukt. De oude configuratie draait nog gewoon door.
    pause
    exit /b 1
)
echo Configuratie herladen; https staat aan.

REM ---- certificaat aanmaken en klaarzetten --------------------------------------
REM De eigen CA ontstaat bij het eerste https-gebruik; die vraag doen we zelf.
timeout /t 2 /nobreak >nul
curl -sk https://everything.mde-automation.nl/ >nul 2>&1

set ROOTCRT=%MAP%\data\pki\authorities\local\root.crt
if not exist "%ROOTCRT%" (
    echo LET OP: %ROOTCRT% nog niet gevonden.
    echo Open eenmaal https://everything.mde-automation.nl in een browser
    echo en draai dit script daarna nog een keer.
    pause
    exit /b 1
)

copy /y "%ROOTCRT%" "%MAP%\publiek\mde-root.crt" >nul
certutil -f -addstore Root "%ROOTCRT%" >nul
echo Basiscertificaat klaargezet en op deze server vertrouwd.

echo.
echo Klaar. Nu op elk apparaat eenmalig het certificaat installeren:
echo   - Windows-pc: http://everything.mde-automation.nl/mde-root.crt openen,
echo     dubbelklikken, installeren in "Vertrouwde basiscertificeringsinstanties"
echo     ^(of als beheerder: certutil -addstore Root mde-root.crt^).
echo   - Android: dezelfde link openen, of Instellingen ^> Beveiliging ^>
echo     Meer instellingen ^> CA-certificaat installeren.
echo   - iPhone: de link in Safari openen, profiel installeren via
echo     Instellingen, en daarna AANZETTEN bij Instellingen ^> Algemeen ^>
echo     Info ^> Certificaatvertrouwensinstellingen.
echo.
echo Daarna werkt https://everything.mde-automation.nl/mde/ en dus ook de
echo camera op de telefoon. Http blijft gewoon werken zolang je wilt.
pause
