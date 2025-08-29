param (
    [string] $PfxPath = ".\Assets\SignCode_Expires_20260709.pfx",
    [string] $PfxPass = "St@ff1234!"
)

function Write-Log {
    param ([string]$Message)
    Write-Host "[InstallCert] $Message"
}

function Update-UI {
    param (
        [string]$Text,
        [int]$Percent
    )
    Write-Host "$Percent% - $Text"
}

Update-UI -Text "Installing certificate…" -Percent 70

if (-not (Test-Path $PfxPath)) {
    throw "Cannot install certificate — PFX file not found at '$PfxPath'"
}

$securePwd = ConvertTo-SecureString -String $PfxPass -AsPlainText -Force
$rawCert   = [IO.File]::ReadAllBytes($PfxPath)
$certObj   = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
                $rawCert, $securePwd, 'MachineKeySet,Exportable,PersistKeySet')

foreach ($storeName in 'Root','TrustedPublisher') {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeName,'LocalMachine')
    $store.Open('ReadWrite')

    $existing = $store.Certificates.Find('FindByThumbprint', $certObj.Thumbprint, $false)
    if ($existing.Count -eq 0) {
        $store.Add($certObj)
        Write-Log -Message "Imported cert to $storeName"
    } else {
        Write-Log -Message "Cert already present in $storeName"
    }

    $store.Close()
}
