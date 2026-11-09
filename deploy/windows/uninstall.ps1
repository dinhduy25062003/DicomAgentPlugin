#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
if (Get-Service DicomAgent -ErrorAction SilentlyContinue) {
    Stop-Service DicomAgent
    sc.exe delete DicomAgent | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Service removal failed' }
}
Get-NetFirewallRule -DisplayName 'DICOM Agent devices' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host 'Service and firewall rule removed. Customer data, settings, binaries and operator group are preserved.'
