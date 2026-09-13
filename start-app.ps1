# ============================================================
#  MDE App handmatig starten (met meldingen in beeld).
#  Gebruik: rechtermuisklik -> "Uitvoeren met PowerShell"
#           of via een snelkoppeling (zie onderaan).
#  Stoppen: Ctrl+C
# ============================================================

$publish = Join-Path $PSScriptRoot 'publish'
$exe     = Join-Path $publish 'EverythingApp.exe'

if (-not (Test-Path $exe)) {
    Write-Host "EverythingApp.exe niet gevonden in $publish" -ForegroundColor Red
    Write-Host "Draai eerst service-installeren.bat (die publiceert de app)." -ForegroundColor Yellow
    Read-Host "Enter om te sluiten"
    exit 1
}

# Draait de service? Die houdt de poort bezet -> eerst stoppen.
$svc = Get-Service MDEApp -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -eq 'Running') {
    Write-Host "Service MDEApp draait en wordt gestopt (poort is anders bezet)..." -ForegroundColor Yellow
    Stop-Service MDEApp -Force
    Start-Sleep -Seconds 2
}

# Eventuele losse processen opruimen.
Get-Process EverythingApp -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "App starten uit $publish ..." -ForegroundColor Cyan
Write-Host "Stoppen met Ctrl+C" -ForegroundColor DarkGray
Write-Host ""

Set-Location $publish
& $exe
