@echo off
rem ============================================================================
rem  MDE Werkplek-agent bouwen (op de ontwikkel-laptop).
rem
rem  1. kopieert de broncode van de share naar een lokale bouwmap
rem     (PyInstaller werkt niet lekker op een netwerkschijf)
rem  2. maakt de venv als die er nog niet is
rem  3. bouwt exe + installer
rem  4. zet de installer terug op de share in MdeAgent\uitvoer\
rem
rem  Daarna op de werkplek-pc: installeer.bat dubbelklikken.
rem ============================================================================
setlocal
rem OpenBLAS (onder numpy) op één thread: anders "Memory allocation still failed" op pc's met veel kernen
rem of een krap wisselbestand, en sterft elke Python die numpy laadt (controle én PyInstaller).
set OPENBLAS_NUM_THREADS=1
set OMP_NUM_THREADS=1
set MKL_NUM_THREADS=1
set BRON=%~dp0
set BOUWMAP=D:\MdeAgent
set PYVERSIE=3.12

echo === Broncode kopieren naar %BOUWMAP% ===
robocopy "%BRON%." "%BOUWMAP%" /E /XD .venv build dist uitvoer __pycache__ bin obj /XF *.pyc /NFL /NDL /NJH /NJS >nul
if errorlevel 8 ( echo Kopieren mislukt. & pause & exit /b 1 )

cd /d "%BOUWMAP%"

if not exist ".venv\Scripts\python.exe" (
    echo === Python-omgeving aanmaken ===
    py -%PYVERSIE% -m venv .venv || ( echo Python %PYVERSIE% niet gevonden; installeer die van python.org. & pause & exit /b 1 )
)

echo === Pakketten bijwerken (requirements.txt) ===
rem --no-cache-dir: voorkomt de "Cache entry deserialization failed"-waarschuwingen van een oude pip-cache.
.venv\Scripts\python -m pip install --no-cache-dir -r requirements.txt pillow || ( echo pip install mislukt. & pause & exit /b 1 )

echo === Controle: zit alles in de venv? ===
.venv\Scripts\python -c "import pysoem, esptool, ingeniamotion, ingenialink; print('pysoem, esptool, ingeniamotion, ingenialink: ok')" || ( echo Een pakket ontbreekt; zie de melding hierboven. & pause & exit /b 1 )

echo === Bouwen (incl. CanLuisteraar, 32-bit hulpprogramma voor de GCAN USBCAN) ===
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
if errorlevel 1 ( echo Bouwen mislukt. & pause & exit /b 1 )

echo === Installer naar de share ===
if not exist "%BRON%uitvoer" mkdir "%BRON%uitvoer"
copy /Y "uitvoer\MdeAgent-setup-*.exe" "%BRON%uitvoer\" >nul
if errorlevel 1 (
    echo Geen installer gemaakt ^(Inno Setup ontbreekt?^); alleen dist\MdeAgent\ gebouwd.
) else (
    echo Klaar. Installer staat in %BRON%uitvoer\
    dir /b "%BRON%uitvoer\MdeAgent-setup-*.exe"
)
pause
