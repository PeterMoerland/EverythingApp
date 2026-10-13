# ============================================================
#  Meldt deze computer aan bij de tabel Computers van de MDE-app.
#
#  Wordt aangeroepen door MDE-Starten.bat (op de achtergrond, onzichtbaar)
#  vlak voordat de browser opent. Verzamelt: computernaam, Windows-gebruiker
#  (aanmeldnaam + volledige naam), service tag (= BIOS-serienummer, bij Dell
#  de echte service tag), merk/type, processor, geheugen, schijven en
#  grafische kaarten.
#
#  De sleutel hieronder moet gelijk zijn aan Laptops:Sleutel in
#  appsettings.json van de app. Twee instellingen, verder niets.
# ============================================================

$app     = "https://everything.mde-automation.nl/mde"
$sleutel = "JOUW-SLEUTEL"

try {
    $bios = Get-CimInstance Win32_BIOS
    $sys  = Get-CimInstance Win32_ComputerSystem
    $cpu  = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name

    # Volledige naam van de aangemelde gebruiker (lokaal of domein); lukt dat niet, dan de aanmeldnaam.
    $volledigeNaam = ""
    try {
        $acc = Get-CimInstance Win32_UserAccount -Filter "Name='$env:USERNAME' AND Domain='$env:USERDOMAIN'" -ErrorAction Stop
        $volledigeNaam = ("" + $acc.FullName).Trim()
    } catch { }
    if (-not $volledigeNaam) {
        try { $volledigeNaam = ("" + ([adsi]"WinNT://$env:USERDOMAIN/$env:USERNAME,user").FullName).Trim() } catch { }
    }

    # Schijven: model + grootte per fysieke schijf, en het totaal in GB.
    $schijven   = @()
    $schijvenGb = 0
    foreach ($d in Get-CimInstance Win32_DiskDrive) {
        $gb = [int][math]::Round($d.Size / 1GB)
        $schijvenGb += $gb
        $schijven += ("{0} ({1} GB)" -f ("" + $d.Model).Trim(), $gb)
    }

    # Grafische kaarten (Microsoft Basic Display Adapter en dergelijke overslaan).
    $gpus = Get-CimInstance Win32_VideoController |
            Where-Object { $_.Name -notmatch 'Basic Display|Remote Display|Virtual' } |
            ForEach-Object { ("" + $_.Name).Trim() }

    $body = @{
        computernaam     = $env:COMPUTERNAME
        gebruiker        = $env:USERNAME
        gebruikerNaam    = $volledigeNaam
        serviceTag       = ("" + $bios.SerialNumber).Trim()
        merkType         = (("" + $sys.Manufacturer).Trim() + " " + ("" + $sys.Model).Trim()).Trim()
        processor        = ("" + $cpu).Trim()
        geheugenGb       = [int][math]::Round($sys.TotalPhysicalMemory / 1GB)
        schijven         = ($schijven -join "; ")
        schijvenGb       = $schijvenGb
        grafischeKaarten = (($gpus | Select-Object -Unique) -join "; ")
    } | ConvertTo-Json

    Invoke-RestMethod -Method Post -Uri ($app + "/api/computer") `
        -Headers @{ "X-Sleutel" = $sleutel } `
        -ContentType "application/json; charset=utf-8" `
        -Body $body -TimeoutSec 5 | Out-Null
}
catch {
    # Bewust stil: het aanmelden mag het starten van de app nooit ophouden.
    # Mislukt het (server even weg), dan lukt het de volgende start gewoon weer.
}
