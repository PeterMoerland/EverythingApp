@echo off
REM ============================================================
REM  MDE App-service stoppen en verwijderen.
REM  (De app-bestanden blijven staan; alleen de service verdwijnt.)
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
set SERVICE=MDEApp

echo Service stoppen...
sc.exe stop %SERVICE%

echo Service verwijderen...
sc.exe delete %SERVICE%

echo.
echo Klaar. De service is verwijderd.
echo.
pause
