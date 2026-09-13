# ============================================================
#  Meldt deze laptop aan bij het MDE-laptopregister.
#
#  Wordt aangeroepen door MDE-Starten.bat (op de achtergrond, onzichtbaar)
#  vlak voordat de browser opent. Verzamelt: computernaam, Windows-gebruiker,
#  service tag (= BIOS-serienummer, bij Dell dus de echte service tag),
#  merk, model, processor en geheugen.
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

    $body = @{
        computernaam   = $env:COMPUTERNAME
        gebruikersnaam = $env:USERNAME
        serviceTag     = ("" + $bios.SerialNumber).Trim()
        merk           = ("" + $sys.Manufacturer).Trim()
        model          = ("" + $sys.Model).Trim()
        processor      = ("" + $cpu).Trim()
        geheugenGB     = [int][math]::Round($sys.TotalPhysicalMemory / 1GB)
    } | ConvertTo-Json

    $url = $app + "/api/laptop"
    Invoke-RestMethod -Method Post -Uri $url `
        -Headers @{ "X-Sleutel" = $sleutel } `
        -ContentType "application/json; charset=utf-8" `
        -Body $body -TimeoutSec 5 | Out-Null
}
catch {
    # Bewust stil: het aanmelden mag het starten van de app nooit ophouden.
    # Mislukt het (server even weg), dan lukt het de volgende start gewoon weer.
}
