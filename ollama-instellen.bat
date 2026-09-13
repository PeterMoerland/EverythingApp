@echo off
REM ============================================================
REM  Ollama op deze server klaarzetten voor het uitlezen van facturen.
REM
REM  Dit script doet drie dingen:
REM    1. de modellen op een vaste plek zetten (niet in een gebruikersprofiel)
REM    2. Ollama laten starten bij het aanzetten van de server, ook zonder dat
REM       er iemand is ingelogd
REM    3. de twee modellen ophalen
REM
REM  Ollama zelf moet al geinstalleerd zijn; zie de uitleg onderaan dit bestand.
REM  Vraagt zelf om administratorrechten.
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
setlocal
set MODELMAP=C:\ProgramData\Ollama\models
set TAAK=Ollama

echo.
echo == Is Ollama er? ==
where ollama >nul 2>&1
if errorlevel 1 (
    echo.
    echo Ollama is nog niet geinstalleerd, of staat niet in het zoekpad.
    echo.
    echo Haal het op bij   https://ollama.com/download/windows
    echo en draai OllamaSetup.exe. Sluit daarna dit venster en start dit
    echo script opnieuw.
    echo.
    pause
    exit /b 1
)
ollama --version

REM ------------------------------------------------------------
REM  1. Waar de modellen komen te staan
REM     Standaard belanden ze in het profiel van de gebruiker die ze ophaalt.
REM     Op een server wil je een vaste plek: anders staan ze straks in het
REM     profiel van wie er toevallig was, of twee keer.
REM ------------------------------------------------------------
echo.
echo == Modelmap vastzetten op %MODELMAP% ==
if not exist "%MODELMAP%" mkdir "%MODELMAP%"
setx OLLAMA_MODELS "%MODELMAP%" /M >nul
set OLLAMA_MODELS=%MODELMAP%
echo    Gezet. Let op: de modellen zijn samen ongeveer 20 GB.

REM ------------------------------------------------------------
REM  2. Altijd laten draaien
REM     Een geplande taak bij het opstarten is hier eenvoudiger dan een echte
REM     Windows-service: geen extra hulpprogramma nodig, en hij draait ook
REM     als er niemand is ingelogd.
REM ------------------------------------------------------------
echo.
echo == Ollama bij het opstarten laten beginnen ==
schtasks /query /tn "%TAAK%" >nul 2>&1
if not errorlevel 1 (
    echo    De taak bestond al; ik maak hem opnieuw aan.
    schtasks /delete /tn "%TAAK%" /f >nul
)

for /f "delims=" %%i in ('where ollama') do set OLLAMAEXE=%%i

schtasks /create /tn "%TAAK%" /tr "\"%OLLAMAEXE%\" serve" /sc onstart /ru "SYSTEM" /rl HIGHEST /f
if errorlevel 1 (
    echo.
    echo De taak aanmaken is mislukt. Zie de foutmelding hierboven.
    pause
    exit /b 1
)

echo.
echo == Nu starten ==
schtasks /run /tn "%TAAK%" >nul 2>&1
timeout /t 5 /nobreak >nul

REM ------------------------------------------------------------
REM  3. De modellen ophalen
REM     Het eerste model leest de tekst van digitale PDF's -- dat is het
REM     meeste werk. Het tweede kan kijken en is alleen nodig voor scans
REM     en foto's van bonnetjes.
REM ------------------------------------------------------------
echo.
echo == Modellen ophalen (dit duurt een tijd; samen ongeveer 20 GB) ==
echo.
echo    qwen2.5:14b   -- leest de tekst van facturen
ollama pull qwen2.5:14b
echo.
echo    qwen2.5vl:7b  -- leest scans en foto's
ollama pull qwen2.5vl:7b

echo.
echo == Wat er nu staat ==
ollama list

REM ------------------------------------------------------------
REM  Controle
REM ------------------------------------------------------------
echo.
echo == Bereikbaar? ==
powershell -NoProfile -Command "try { $r = Invoke-WebRequest -Uri 'http://localhost:11434/api/tags' -UseBasicParsing -TimeoutSec 5; Write-Host '   Ollama antwoordt op poort 11434.' } catch { Write-Host '   Ollama antwoordt NIET. Herstart de server en probeer opnieuw.' }"

echo.
echo == Videokaart in gebruik? ==
where nvidia-smi >nul 2>&1
if errorlevel 1 (
    echo    Geen nvidia-smi gevonden. Zonder videokaart werkt het ook, maar
    echo    reken op een halve minuut per factuur.
) else (
    nvidia-smi --query-gpu=name,memory.total --format=csv,noheader
)

echo.
echo Klaar. Test het in de app: Financien ^> Inkomende facturen ^> Factuur
echo toevoegen ^> een PDF kiezen ^> "Laat uitlezen".
echo.
echo Werkt het niet, kijk dan of appsettings.json bij Ai het volgende heeft:
echo    "Bron": "ollama"
echo    "OllamaAdres": "http://localhost:11434"
echo.
pause
