<#
  Bouwt de MDE Werkplek-agent: dist\MdeAgent\MdeAgent.exe en (als Inno Setup
  aanwezig is) uitvoer\MdeAgent-setup-<versie>.exe.

  Eenmalig:  py -3.12 -m venv .venv ; .venv\Scripts\pip install -r requirements.txt
  Daarna:    .\build.ps1
#>
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# OpenBLAS (onder numpy) reserveert per CPU-kern een grote buffer; op pc's met veel kernen of een
# krap wisselbestand mislukt dat ("Memory allocation still failed") en sterven PyInstallers
# kindprocessen. Eén thread is voor ons ruim voldoende.
$env:OPENBLAS_NUM_THREADS = "1"
$env:OMP_NUM_THREADS = "1"
$env:MKL_NUM_THREADS = "1"

$py = if (Test-Path ".venv\Scripts\python.exe") { ".venv\Scripts\python.exe" } else { "python" }
$versie = & $py -c "import mde_agent; print(mde_agent.VERSIE)"
Write-Host "MDE Werkplek-agent $versie bouwen met $py" -ForegroundColor Cyan

if (-not (Test-Path "mde.ico")) {
    try { & $py -c "import PIL" 2>$null; & $py maak_icoon.py } catch { Write-Host "Geen Pillow: zonder icoon." }
}

if (Test-Path "build") { Remove-Item build -Recurse -Force }
if (Test-Path "dist")  { Remove-Item dist  -Recurse -Force }
& $py -m PyInstaller --noconfirm MdeAgent.spec
if ($LASTEXITCODE -ne 0) { throw "PyInstaller mislukt." }

# Snelle controle: start de exe even en vraag de versie.
$uit = & "dist\MdeAgent\MdeAgent.exe" --versie
Write-Host "Exe zegt versie: $uit"

# CanLuisteraar: 32-bit hulpprogramma (.NET, x86) voor de GCAN USBCAN via de 32-bit ECanVci.dll
# van eTuner. De 64-bit agent kan die DLL niet zelf laden. Komt naast MdeAgent.exe te staan.
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    & dotnet publish CanLuisteraar\CanLuisteraar.csproj -c Release -o uitvoer\CanLuisteraar --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "CanLuisteraar (dotnet publish) mislukt." }
    Copy-Item uitvoer\CanLuisteraar\CanLuisteraar.exe dist\MdeAgent\ -Force
    Write-Host "CanLuisteraar.exe (32-bit) toegevoegd."
} else {
    Write-Host "dotnet SDK niet gevonden: CanLuisteraar overgeslagen (CAN meeluisteren werkt dan niet)." -ForegroundColor Yellow
}

# Installer
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    New-Item -ItemType Directory -Force uitvoer | Out-Null
    & $iscc "/DVersie=$versie" installer.iss
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup mislukt." }
    Write-Host "Installer: uitvoer\MdeAgent-setup-$versie.exe" -ForegroundColor Green
} else {
    Write-Host "Inno Setup 6 niet gevonden (https://jrsoftware.org/isdl.php); alleen dist\MdeAgent\ gebouwd." -ForegroundColor Yellow
}
