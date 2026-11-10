@echo off
rem ============================================================================
rem  MDE Werkplek-agent installeren of bijwerken (op de werkplek-pc).
rem  Dubbelklikken vanaf de share is genoeg: pakt de nieuwste installer uit
rem  de map uitvoer\, vraagt om administratorrechten en installeert stil.
rem  Het script vraagt altijd om het token van de app (Productie:AgentToken; het wordt
rem  niet getoond). Bij bijwerken = Enter: het huidige token blijft staan. Ook als de
rem  agent eerder zelf een token verzon (dat de app niet kent), kun je het zo goedzetten.
rem  Meegeven kan ook:            installeer.bat MijnToken
rem  Opnieuw laten vragen:        installeer.bat /token
rem  Het token staat bewust NIET in deze map: die is voor iedereen leesbaar.
rem
rem  Werkt ook vanaf een \\server\share: het script kopieert zichzelf eerst
rem  naar een lokale map, want een verhoogd cmd kan niet in een UNC-map staan
rem  en kent de netwerkletters (T:) van de gebruiker niet.
rem ============================================================================
setlocal EnableDelayedExpansion

if /i "%~1"=="/bron" (
    set "BRON=%~2"
    set "TOKEN=%~3"
) else (
    set "BRON=%~dp0"
    set "TOKEN=%~1"
)
if "%BRON:~-1%"=="\" set "BRON=%BRON:~0,-1%"

rem Gestart vanaf een netwerkletter (T:)? Een verhoogd venster kent die niet; omzetten naar \\server\share.
if "%BRON:~1,1%"==":" (
    for /f "delims=" %%u in ('powershell -NoProfile -Command "(Get-PSDrive -Name '%BRON:~0,1%' -ErrorAction SilentlyContinue).DisplayRoot"') do set "UNC=%%u"
    if defined UNC set "BRON=!UNC!%BRON:~2%"
)

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Administratorrechten nodig; opnieuw starten...
    rem Kopie op een plek die ook het admin-account kan lezen; cmd /k houdt het venster open.
    rem Vaste, spatievrije plek zodat het pad zonder aanhalingstekens kan (cmd /k struikelt
    rem anders over een opdracht die met een aanhalingsteken begint).
    copy /Y "%~f0" "C:\Users\Public\mde-installeer.bat" >nul || ( echo Kan niet kopieren naar C:\Users\Public. & pause & exit /b 1 )
    powershell -NoProfile -Command "Start-Process cmd -Verb RunAs -ArgumentList @('/k', 'C:\Users\Public\mde-installeer.bat', '/bron', '\"%BRON%\"', '\"%TOKEN%\"')"
    exit /b
)

echo Bronmap: %BRON%
if /i "%TOKEN%"=="/token" set "TOKEN="
rem Nieuwe pc: de agent maakt bij de eerste start zelf een willekeurig token, dat de app niet kent. Dan mag Enter
rem het huidige token dus niet houden.
set "NIEUW="
if not exist "%ProgramData%\MDE\Agent\agent.json" set "NIEUW=1"
if not exist "%BRON%\uitvoer\" (
    echo De bronmap is vanuit dit verhoogde venster niet bereikbaar ^(netwerkshare zonder aanmelding?^).
    echo Koppel de share eerst met:  net use \\172.16.1.13\claudecoworkshare /user:GEBRUIKER
    pause & exit /b 1
)
set NIEUWSTE=
for /f "delims=" %%f in ('dir /b /o-d "%BRON%\uitvoer\MdeAgent-setup-*.exe" 2^>nul') do (
    if not defined NIEUWSTE set "NIEUWSTE=%%f"
)
if not defined NIEUWSTE ( echo Geen installer gevonden in %BRON%\uitvoer\ & pause & exit /b 1 )

echo === %NIEUWSTE% ophalen ===
copy /Y "%BRON%\uitvoer\%NIEUWSTE%" "%TEMP%\%NIEUWSTE%" >nul || ( echo Kopieren mislukt. & pause & exit /b 1 )

echo === Oude service stoppen ===
sc stop MdeAgent >nul 2>&1
timeout /t 3 /nobreak >nul
taskkill /F /IM MdeAgent.exe >nul 2>&1

echo === Installeren (stil) ===
"%TEMP%\%NIEUWSTE%" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS
if errorlevel 1 ( echo Installatie gaf een fout ^(code %errorlevel%^). & pause & exit /b 1 )
del "%TEMP%\%NIEUWSTE%" >nul 2>&1

rem De app draait met de eigen CA van Caddy. De agent is een service (systeemaccount) en vertrouwt alleen wat voor
rem de COMPUTER is geinstalleerd; een dubbelklik op mde-root.crt zet hem alleen bij de gebruiker.
echo === Basiscertificaat van de server ===
if exist "%BRON%\mde-root.crt" (
    certutil -f -addstore Root "%BRON%\mde-root.crt" >nul && echo Vertrouwd voor deze computer ^(nodig om firmware van de app op te halen^).
) else (
    echo Let op: mde-root.crt staat niet in de bronmap; firmware ophalen via https kan dan mislukken.
)

set "EXE=%ProgramFiles%\MDE\Agent\MdeAgent.exe"
rem Het token altijd vragen (10-11-2026): eerder vroeg het script alleen op een pc zonder agent.json, en een token dat de
rem agent zelf had verzonnen bleef dan stilletjes staan (de app kon de agent niet gebruiken). Enter = huidige houden.
set "HUIDIG="
if not defined NIEUW for /f "delims=" %%t in ('call "%EXE%" --toon-token') do set "HUIDIG=%%t"
if "!HUIDIG!"=="(geen token ingesteld)" set "HUIDIG="
if "%TOKEN%"=="" (
    echo.
    echo De agent heeft het token van de app nodig ^(Productie:AgentToken in appsettings.json^); vraag het zo nodig aan de beheerder.
    if defined HUIDIG (
        echo Nu ingesteld: !HUIDIG:~0,4!...   Enter = dat token houden.
        echo Zegt de app ^(Tools ^> MDE Agent^) dat het token niet klopt, vul dan hier het token van de app in.
    ) else (
        echo Leeg laten = later instellen met: installeer.bat /token
    )
    for /f "usebackq delims=" %%t in (`powershell -NoProfile -Command "$s = Read-Host 'Token (wordt niet getoond)' -AsSecureString; [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))"`) do set "TOKEN=%%t"
)
if "%TOKEN%"=="" if not defined HUIDIG (
    echo LET OP: geen token ingesteld; de app kan deze agent pas bereiken na: installeer.bat /token
)
if not "%TOKEN%"=="" (
    echo === Token instellen ===
    "%EXE%" --zet-token "%TOKEN%"
    "%EXE%" --service restart >nul
)

echo.
echo === Controle ===
"%EXE%" --versie
rem Het token alleen kort tonen: dit venster staat soms op een gedeeld scherm.
set "HUIDIG="
for /f "delims=" %%t in ('call "%EXE%" --toon-token') do set "HUIDIG=%%t"
echo Token: !HUIDIG:~0,4!... ^(volledig: "%EXE%" --toon-token^)
echo.
timeout /t 3 /nobreak >nul
rem De eerste /health kan even duren (agent laadt zijn bibliotheken); ruim wachten.
powershell -NoProfile -Command "try { (Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5055/health -TimeoutSec 40).Content } catch { 'Agent antwoordt nog niet: ' + $_.Exception.Message + ' -- probeer http://127.0.0.1:5055/health in de browser.' }"
echo.
pause
exit
