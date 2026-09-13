# ---------------------------------------------------------------------------
# Eenmalig: hernoemt het project van UreningavePoC naar EverythingApp.
# Draai dit VOOR de eerste git-commit, vanuit de projectmap, als beheerder
# (voor het stoppen/verwijderen van de service).
#
# Wat het doet: service stoppen en verwijderen, buildresten opruimen,
# UreningavePoC.csproj hernoemen, en 'UreningavePoC' vervangen door
# 'EverythingApp' in alle bronbestanden (cs, razor, csproj, bat, ps1, md).
# appsettings.json blijft bewust ongemoeid: daar staat het máppad in, en de
# map hernoemen we (nog) niet.
# ---------------------------------------------------------------------------
$ErrorActionPreference = 'Stop'
$map = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $map

Write-Host "== Service stoppen en verwijderen (komt zo terug met de nieuwe naam) =="
sc.exe stop MDEApp | Out-Null
Start-Sleep -Seconds 3
sc.exe delete MDEApp | Out-Null

Write-Host "== Buildresten opruimen (bin, obj, publish) =="
foreach ($d in 'bin', 'obj', 'publish') {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}

Write-Host "== Projectbestand hernoemen =="
if (Test-Path 'UreningavePoC.csproj') {
    Rename-Item 'UreningavePoC.csproj' 'EverythingApp.csproj'
}

Write-Host "== Naam vervangen in de bronbestanden =="
$metBom    = New-Object System.Text.UTF8Encoding($true)    # cs/razor: zoals Visual Studio
$zonderBom = New-Object System.Text.UTF8Encoding($false)   # bat: cmd struikelt over een BOM
$overslaan = '\\bin\\|\\obj\\|\\publish\\|\\TempFileStore\\|\\\.git\\|hernoem-naar-everythingapp|appsettings'

Get-ChildItem -Recurse -File -Include *.cs, *.razor, *.csproj, *.bat, *.ps1, *.md, *.sln, launchSettings.json |
    Where-Object { $_.FullName -notmatch $overslaan } |
    ForEach-Object {
        $tekst = [IO.File]::ReadAllText($_.FullName)
        if ($tekst -notmatch 'UreningavePoC') { return }
        $nieuw = $tekst -replace 'UreningavePoC', 'EverythingApp'
        $codering = if ($_.Extension -eq '.bat') { $zonderBom } else { $metBom }
        [IO.File]::WriteAllText($_.FullName, $nieuw, $codering)
        Write-Host ("  aangepast: " + $_.FullName.Substring($map.Length))
    }

Write-Host ""
Write-Host "Klaar. Nu in deze volgorde:"
Write-Host "  1. service-installeren.bat draaien (maakt de service opnieuw, nu met EverythingApp.exe)"
Write-Host "  2. je gebruikelijke publicatiescript draaien"
Write-Host "  3. daarna pas: git init en de eerste commit"
