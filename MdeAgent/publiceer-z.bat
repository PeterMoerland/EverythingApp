@echo off
rem ============================================================================
rem  De MDE Werkplek-agent beschikbaar maken op de Data-share (Z:), voor iedereen:
rem      \\172.16.1.4\data\software\MDE Apps\MDE Agent
rem  Zet daar installeer.bat, LEESMIJ.txt en de nieuwste installer (uitvoer\).
rem  Oudere installers daar gaan naar uitvoer\oud (niet weggegooid).
rem  Eerst bouwen met bouw.bat; dit script waarschuwt als de nieuwste installer
rem  ouder is dan de versie in de broncode.
rem  Het token gaat NIET mee: de map is voor iedereen leesbaar.
rem ============================================================================
setlocal EnableDelayedExpansion

set "DOEL=\\172.16.1.4\data\software\MDE Apps\MDE Agent"
rem Alleen om te testen: een andere doelmap via de omgevingsvariabele MDE_DOEL.
if defined MDE_DOEL set "DOEL=%MDE_DOEL%"
set "BRON=%~dp0"
if "%BRON:~-1%"=="\" set "BRON=%BRON:~0,-1%"

rem --- nieuwste installer in uitvoer\ ---
set "NIEUWSTE="
for /f "delims=" %%f in ('dir /b /o-d "%BRON%\uitvoer\MdeAgent-setup-*.exe" 2^>nul') do (
    if not defined NIEUWSTE set "NIEUWSTE=%%f"
)
if not defined NIEUWSTE ( echo Geen installer in %BRON%\uitvoer\ - eerst bouw.bat draaien. & pause & exit /b 1 )

rem --- versie in de broncode tegenover die van de installer ---
set "BRONVERSIE="
for /f "tokens=2 delims==" %%v in ('findstr /b /c:"VERSIE" "%BRON%\mde_agent\__init__.py"') do set "BRONVERSIE=%%v"
set "BRONVERSIE=!BRONVERSIE: =!"
set BRONVERSIE=!BRONVERSIE:"=!
set "SETUPVERSIE=%NIEUWSTE:MdeAgent-setup-=%"
set "SETUPVERSIE=%SETUPVERSIE:.exe=%"
echo Nieuwste installer: %NIEUWSTE%   (broncode: %BRONVERSIE%)
if not "%SETUPVERSIE%"=="%BRONVERSIE%" (
    echo.
    echo LET OP: de broncode staat op %BRONVERSIE%, de nieuwste installer is %SETUPVERSIE%.
    echo Draai eerst bouw.bat als je de nieuwe versie wilt uitrollen.
    choice /c JN /m "Toch %SETUPVERSIE% publiceren"
    if errorlevel 2 exit /b 1
)

rem --- doelmap ---
if not exist "%DOEL%\" (
    mkdir "%DOEL%" 2>nul || ( echo Kan "%DOEL%" niet maken of bereiken. & pause & exit /b 1 )
)
if not exist "%DOEL%\uitvoer\" mkdir "%DOEL%\uitvoer"

rem --- oudere installers daar opzij zetten (installeer.bat pakt de nieuwste op datum) ---
for %%f in ("%DOEL%\uitvoer\MdeAgent-setup-*.exe") do (
    if /i not "%%~nxf"=="%NIEUWSTE%" (
        if not exist "%DOEL%\uitvoer\oud\" mkdir "%DOEL%\uitvoer\oud"
        move /Y "%%f" "%DOEL%\uitvoer\oud\" >nul && echo Opzij gezet: %%~nxf
    )
)

echo === Kopieren naar %DOEL% ===
copy /Y "%BRON%\uitvoer\%NIEUWSTE%" "%DOEL%\uitvoer\" >nul || ( echo Kopieren van de installer mislukt. & pause & exit /b 1 )
copy /Y "%BRON%\installeer.bat" "%DOEL%\" >nul || ( echo Kopieren van installeer.bat mislukt. & pause & exit /b 1 )

rem Het basiscertificaat van de server (eigen CA van Caddy) mee, uit de vertrouwde certificaten van deze pc:
rem installeer.bat zet het bij de computer, zodat de agent-service firmware via https kan ophalen.
powershell -NoProfile -Command "$c = Get-ChildItem Cert:\LocalMachine\Root, Cert:\CurrentUser\Root | Where-Object { $_.Subject -like 'CN=Caddy Local Authority*Root' } | Sort-Object NotAfter -Descending | Select-Object -First 1; if (-not $c) { exit 1 }; [IO.File]::WriteAllBytes('%DOEL%\mde-root.crt', $c.RawData)"
if errorlevel 1 (
    echo LET OP: het Caddy-basiscertificaat staat niet op deze pc; mde-root.crt niet meegezet.
    echo Haal het van http://everything.mde-automation.nl/mde-root.crt en zet het zelf in "%DOEL%".
) else (
    echo mde-root.crt meegezet.
)

> "%DOEL%\LEESMIJ.txt" (
    echo MDE Werkplek-agent  -  versie %SETUPVERSIE%
    echo.
    echo Installeren of bijwerken op een pc:
    echo   dubbelklik op installeer.bat en bevestig de vraag om administratorrechten.
    echo.
    echo Nieuwe pc: het script vraagt om het token van de app ^(Productie:AgentToken^).
    echo Dat staat bewust niet in deze map; vraag het aan de beheerder.
    echo Token later ^(opnieuw^) instellen: open hier een venster en typ
    echo   in PowerShell:   .\installeer.bat /token
    echo   in cmd:          installeer.bat /token
    echo.
    echo installeer.bat zet ook mde-root.crt ^(het basiscertificaat van de server^) bij de computer;
    echo zonder dat kan de agent geen firmware van de app ophalen.
    echo.
    echo Controle na afloop: http://127.0.0.1:5055/health in de browser van die pc.
    echo Gepubliceerd op %DATE% %TIME:~0,5%.
)

echo.
echo Klaar. In "%DOEL%":
dir /b "%DOEL%"
dir /b "%DOEL%\uitvoer\*.exe"
pause
