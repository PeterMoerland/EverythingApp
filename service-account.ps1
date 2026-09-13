# ============================================================
#  De MDE-service onder een gebruikersaccount laten draaien.
#
#  Waarom: als service draait de app standaard onder LocalSystem. Op deze
#  server heet dat naar buiten toe WORKGROUP\SQL-SERVER$ — een account dat
#  alleen op deze machine bestaat. Een netwerkmap als \\172.16.1.4\data
#  weigert dat, dus foto's, filestore en de laser-PDF blijven onbereikbaar.
#
#  Met een gewoon gebruikersaccount lukt het wel, mits datzelfde account
#  met hetzelfde wachtwoord ook op 172.16.1.4 bestaat. In een werkgroep
#  (geen domein) logt Windows dan met naam en wachtwoord in op de share.
#
#  Dit script doet drie dingen:
#    1. het account het recht "Aanmelden als service" geven
#    2. de service op dat account zetten
#    3. de service opnieuw starten en controleren
# ============================================================

# Het account staat hier als standaardwaarde: bij het uitvoeren hoef je alleen
# nog op Enter te drukken. Het wachtwoord komt hier bewust NIET te staan — dit
# bestand komt in de publish-map terecht en is voor iedereen leesbaar.
param(
    [string]$Account = '.\mdeapp'
)

$ErrorActionPreference = 'Stop'
$service = 'MDEApp'

function Stap($tekst) {
    Write-Host ''
    Write-Host "== $tekst ==" -ForegroundColor Cyan
}

Write-Host ''
Write-Host 'De MDE-service onder een gebruikersaccount zetten' -ForegroundColor White
Write-Host ''
Write-Host 'Let op: dit account moet ook op 172.16.1.4 bestaan, met exact'
Write-Host 'hetzelfde wachtwoord. Anders blijft de netwerkmap dicht.'
Write-Host ''
Write-Host 'Schrijf een lokaal account als  .\naam  (bijvoorbeeld .\mdeapp).'
Write-Host ''

# De tekst vóór de dubbele punt is de vráág; wat je intypt is het antwoord.
# Enter zonder iets te typen neemt de standaardwaarde hierboven over.
$naam = Read-Host "Accountnaam [$Account]"
if ([string]::IsNullOrWhiteSpace($naam)) { $naam = $Account }

Write-Host ''
Write-Host 'Het wachtwoord wordt niet getoond en nergens bewaard; het gaat'
Write-Host 'rechtstreeks naar Windows.'
$geheim = Read-Host 'Wachtwoord' -AsSecureString
if ($geheim.Length -eq 0) { Write-Host 'Geen wachtwoord ingevuld; gestopt.'; exit 1 }

$wachtwoord = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($geheim))

# ------------------------------------------------------------
#  1. Recht "Aanmelden als service"
#     Dat recht staat in het lokale beveiligingsbeleid en is alleen te
#     wijzigen via secedit. Zet je alleen het account met sc.exe, dan
#     weigert de service te starten met foutmelding 1069.
# ------------------------------------------------------------
Stap 'Recht "Aanmelden als service" toekennen'

# De naam omzetten naar een SID; het beleid werkt met SID's.
$zoeknaam = if ($naam.StartsWith('.\')) { "$env:COMPUTERNAME\" + $naam.Substring(2) } else { $naam }
try {
    $sid = (New-Object System.Security.Principal.NTAccount($zoeknaam)).Translate(
                [System.Security.Principal.SecurityIdentifier]).Value
} catch {
    Write-Host ''
    Write-Host "Account '$zoeknaam' bestaat niet op deze machine." -ForegroundColor Red
    Write-Host 'Maak het eerst aan, bijvoorbeeld met:'
    Write-Host "   net user mdeapp * /add"
    Write-Host ''
    exit 1
}
Write-Host "  $zoeknaam  ->  $sid"

$inf = Join-Path $env:TEMP 'mde-rechten.inf'
$db  = Join-Path $env:TEMP 'mde-rechten.sdb'
secedit /export /cfg $inf /areas USER_RIGHTS | Out-Null

$regels = Get-Content $inf
$huidig = ($regels | Where-Object { $_ -match '^SeServiceLogonRight' })

if ($huidig -and $huidig -match [regex]::Escape($sid)) {
    Write-Host '  Het recht stond er al.'
} else {
    $nieuw = if ($huidig) { "$huidig,*$sid" } else { "SeServiceLogonRight = *$sid" }

    $uit = if ($huidig) {
        $regels | ForEach-Object { if ($_ -match '^SeServiceLogonRight') { $nieuw } else { $_ } }
    } else {
        $regels | ForEach-Object { $_; if ($_ -match '^\[Privilege Rights\]') { $nieuw } }
    }

    Set-Content -Path $inf -Value $uit -Encoding Unicode
    secedit /configure /db $db /cfg $inf /areas USER_RIGHTS | Out-Null
    Write-Host '  Toegekend.'
}

Remove-Item $inf, $db -ErrorAction SilentlyContinue

# ------------------------------------------------------------
#  2. De service op dit account zetten
# ------------------------------------------------------------
Stap 'Service op dit account zetten'

# De spatie na 'obj=' en 'password=' hoort zo: sc.exe wil dat zo hebben.
$uitvoer = & sc.exe config $service obj= $naam password= $wachtwoord 2>&1
Write-Host "  $uitvoer"
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'Instellen mislukt. Bestaat de service wel? Draai anders eerst' -ForegroundColor Red
    Write-Host 'service-herstarten.bat.'
    exit 1
}

# ------------------------------------------------------------
#  3. Opnieuw starten
# ------------------------------------------------------------
Stap 'Service opnieuw starten'
& sc.exe stop $service | Out-Null

$pogingen = 0
while ((& sc.exe query $service) -notmatch 'STOPPED' -and $pogingen -lt 30) {
    Start-Sleep -Seconds 1
    $pogingen++
}
Start-Sleep -Seconds 2
& sc.exe start $service | Out-Null

$pogingen = 0
while ((& sc.exe query $service) -notmatch 'RUNNING' -and $pogingen -lt 20) {
    Start-Sleep -Seconds 1
    $pogingen++
}

Write-Host ''
if ((& sc.exe query $service) -match 'RUNNING') {
    Write-Host 'Klaar. De service draait nu als ' -NoNewline
    Write-Host $naam -ForegroundColor Green
    Write-Host ''
    Write-Host 'Controleer het resultaat in de app: Artikelen > een artikel met foto >'
    Write-Host 'knop "Waar zoekt hij?". Daar moet nu dit account staan en moet de'
    Write-Host 'hoofdmap bestaan.'
} else {
    Write-Host 'De service is niet gestart.' -ForegroundColor Red
    Write-Host ''
    Write-Host 'Foutmelding 1069 = het wachtwoord klopt niet, of het recht'
    Write-Host '"Aanmelden als service" is niet aangekomen.'
    Write-Host 'Kijk in Logboeken > Windows-logboeken > Systeem.'
}

Write-Host ''
Read-Host 'Druk op Enter om te sluiten'
