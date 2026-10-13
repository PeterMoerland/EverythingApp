@echo off
rem ============================================================================
rem  MDE Werkplek-agent installeren of bijwerken (op de werkplek-pc).
rem  Dubbelklikken vanaf de share is genoeg: pakt de nieuwste installer uit
rem  de map uitvoer\, vraagt om administratorrechten en installeert stil.
rem  Een bestaand token blijft staan. Eerste keer: geef het token mee:
rem      installeer.bat MijnToken
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

set "EXE=%ProgramFiles%\MDE\Agent\MdeAgent.exe"
if not "%TOKEN%"=="" (
    echo === Token instellen ===
    "%EXE%" --zet-token "%TOKEN%"
    "%EXE%" --service restart >nul
)

echo.
echo === Controle ===
"%EXE%" --versie
echo Token (hoort in appsettings Productie:AgentToken):
"%EXE%" --toon-token
echo.
timeout /t 3 /nobreak >nul
rem De eerste /health kan even duren (agent laadt zijn bibliotheken); ruim wachten.
powershell -NoProfile -Command "try { (Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5055/health -TimeoutSec 40).Content } catch { 'Agent antwoordt nog niet: ' + $_.Exception.Message + ' -- probeer http://127.0.0.1:5055/health in de browser.' }"
echo.
pause
