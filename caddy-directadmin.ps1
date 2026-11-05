# ============================================================================
# Caddy in C:\Caddy uitbreiden met de DNS-plugin voor DirectAdmin
# (module dns.providers.directadmin, github.com/caddy-dns/directadmin).
# Daarmee kan Caddy later een echt Let's Encrypt-certificaat halen via de
# DNS-uitdaging, ook al is de server niet vanaf internet bereikbaar.
#
# Start via caddy-directadmin.bat (vraagt beheerderrechten), op de server.
#
# Doet: reservekopie van caddy.exe, Caddy even stoppen (Windows laat een
# draaiende caddy.exe niet vervangen: "rename ... Access is denied"),
# 'caddy add-package' (haalt van caddyserver.com een nieuwe caddy.exe met
# dezelfde modules plus de plugin; dat is meteen de nieuwste Caddy-versie),
# controle van de module en van de Caddyfile, en Caddy weer starten zoals hij
# draaide: als service (ook via NSSM), of anders met dezelfde opdrachtregel.
# Gaat iets mis, dan komt de oude caddy.exe terug. De Caddyfile blijft zoals hij is.
# Onderbreking: een paar seconden tot de download klaar is.
# ============================================================================

# Continue: Caddy logt naar stderr, en in Windows PowerShell 5.1 wordt dat met 'Stop' een fout.
$ErrorActionPreference = 'Continue'
$map    = 'C:\Caddy'
$exe    = Join-Path $map 'caddy.exe'
$conf   = Join-Path $map 'Caddyfile'
$pakket = 'github.com/caddy-dns/directadmin'
$module = 'dns.providers.directadmin'
$kopie  = Join-Path $map 'caddy.exe.voor-directadmin'

function Heeft-Module { (& $exe list-modules) -contains $module }
function Caddy-Proces { Get-CimInstance Win32_Process -Filter "Name='caddy.exe'" | Where-Object { $_.ExecutablePath -eq $exe } }

if (-not (Test-Path $exe)) { Write-Host "FOUT: $exe niet gevonden. Draai dit script op de server."; exit 1 }
if (Heeft-Module) { Write-Host "De module $module zit al in $exe; er hoeft niets te gebeuren."; & $exe version; exit 0 }
Write-Host "Nu: $(& $exe version)"

# ---- hoe draait Caddy? ----------------------------------------------------------
$service = $null
$argumenten = $null
$p = Caddy-Proces | Select-Object -First 1
if ($p) {
    $ouder = Get-CimInstance Win32_Process -Filter "ProcessId=$($p.ParentProcessId)" -ErrorAction SilentlyContinue
    Write-Host "Caddy-proces $($p.ProcessId): $($p.CommandLine)"
    Write-Host "  gestart door: $(if ($ouder) { "$($ouder.Name) ($($ouder.ProcessId))" } else { 'onbekend (proces bestaat niet meer)' })"
    # de service van het proces zelf, of van zijn ouder (NSSM, WinSW e.d.)
    $service = Get-CimInstance Win32_Service |
        Where-Object { $_.ProcessId -ne 0 -and ($_.ProcessId -eq $p.ProcessId -or $_.ProcessId -eq $p.ParentProcessId) } |
        Select-Object -First 1
    if (-not $service) {
        $c = $p.CommandLine.Trim()
        if ($c.StartsWith('"')) { $argumenten = $c.Substring($c.IndexOf('"', 1) + 1).Trim() }
        else { $i = $c.IndexOf(' '); $argumenten = if ($i -gt 0) { $c.Substring($i + 1).Trim() } else { '' } }
    }
} else {
    Write-Host 'Caddy draait nu niet; het script start hem na afloop niet.'
}
if ($service)        { Write-Host "Caddy draait als service: $($service.Name) ($($service.PathName))" }
elseif ($p)          { Write-Host "Geen service; Caddy komt terug met: caddy.exe $argumenten" }

function Stop-Caddy {
    if ($service) { Stop-Service -Name $service.Name -Force -ErrorAction Stop }
    elseif ($p) {
        & $exe stop 2>$null | Out-Null           # netjes via de admin-API
        Start-Sleep -Seconds 2
        Caddy-Proces | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
    Start-Sleep -Seconds 1
    if (Caddy-Proces) { throw 'Caddy is niet gestopt.' }
}

function Start-Caddy {
    if ($service) { Start-Service -Name $service.Name -ErrorAction Stop }
    elseif ($p) { Start-Process -FilePath $exe -ArgumentList $argumenten -WorkingDirectory $map -WindowStyle Hidden }
    else { return }
    Start-Sleep -Seconds 3
    if (-not (Caddy-Proces)) { throw 'Caddy start niet.' }
}

# ---- eigen reservekopie (add-package maakt er ook een, maar ruimt die op) ---------
Copy-Item $exe $kopie -Force -ErrorAction Stop
Write-Host "Reservekopie: $kopie"

try {
    Write-Host ''
    Write-Host 'Caddy stoppen...'
    Stop-Caddy
    Write-Host "caddy add-package $pakket ..."
    & $exe add-package $pakket
    if ($LASTEXITCODE -ne 0) { throw 'add-package mislukt.' }
    if (-not (Heeft-Module)) { throw "de nieuwe caddy.exe kent $module niet." }
    & $exe validate --config $conf
    if ($LASTEXITCODE -ne 0) { throw 'de Caddyfile is niet geldig met de nieuwe caddy.exe.' }
    Write-Host 'Caddy starten...'
    Start-Caddy
}
catch {
    Write-Host ''
    Write-Host "FOUT: $($_.Exception.Message)"
    Write-Host 'Oude caddy.exe terugzetten...'
    try { Stop-Caddy } catch { }
    try { Copy-Item $kopie $exe -Force -ErrorAction Stop }
    catch { Write-Host "Terugzetten lukt niet ($($_.Exception.Message)); de oude staat in $kopie." }
    try { Start-Caddy } catch { Write-Host "LET OP: Caddy start ook met de oude caddy.exe niet: $($_.Exception.Message)" }
    Write-Host "Oude caddy.exe staat er weer: $(& $exe version)"
    exit 1
}

Write-Host ''
Write-Host "Klaar. Nieuwe versie: $(& $exe version)"
Write-Host "Module $module zit erin. De reservekopie blijft staan: $kopie"
Write-Host "De Caddyfile is niet veranderd; het certificaat blijft voorlopig 'tls internal'."
exit 0
