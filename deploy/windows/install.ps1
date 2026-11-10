#Requires -RunAsAdministrator
param(
    [string]$Source = (Join-Path $PSScriptRoot '../../publish/windows'),
    [string]$OperatorUser = "$env:USERDOMAIN\$env:USERNAME",
    [string[]]$AllowedRemoteAddresses = @(),
    [ValidateRange(1,65535)][int]$Port = 11112
)
$ErrorActionPreference = 'Stop'
$Target = Join-Path $env:ProgramFiles 'DicomAgent'
$Data = Join-Path $env:ProgramData 'DicomAgent'
$Simulator = Join-Path $env:SystemDrive 'DicomSimulator'
if (!(Test-Path "$Source/host/DicomAgent.Host.exe") -or !(Test-Path "$Source/desktop/DicomAgent.Desktop.exe") -or !(Test-Path "$Source/scu/DicomAgent.Scu.exe")) { throw 'Publish Host, Desktop and SCU first' }
foreach ($address in $AllowedRemoteAddresses) { [void][System.Net.IPAddress]::Parse($address) }
if (!(Get-LocalGroup -Name DicomAgentOperators -ErrorAction SilentlyContinue)) { New-LocalGroup -Name DicomAgentOperators -Description 'Local DICOM Agent operators' | Out-Null }
if ($OperatorUser) {
    $OperatorSid = ([System.Security.Principal.NTAccount]::new($OperatorUser)).Translate([System.Security.Principal.SecurityIdentifier]).Value
    $MemberSids = @(Get-LocalGroupMember -Group DicomAgentOperators | ForEach-Object { $_.SID.Value })
    if ($MemberSids -notcontains $OperatorSid) { Add-LocalGroupMember -Group DicomAgentOperators -Member $OperatorUser }
}
if (Get-Service DicomAgent -ErrorAction SilentlyContinue) { Stop-Service DicomAgent; }
New-Item -ItemType Directory -Force -Path "$Target/host", "$Target/desktop", "$Data/Storage" | Out-Null
New-Item -ItemType Directory -Force -Path $Simulator | Out-Null
Copy-Item "$Source/host/*" "$Target/host" -Recurse -Force
Copy-Item "$Source/desktop/*" "$Target/desktop" -Recurse -Force
Copy-Item "$Source/scu/*" $Simulator -Recurse -Force
icacls $Simulator /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' 'DicomAgentOperators:(OI)(CI)RX' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot configure simulator ACL' }
if (!(Get-Service DicomAgent -ErrorAction SilentlyContinue)) {
    New-Service -Name DicomAgent -DisplayName 'DICOM Desktop Agent' -BinaryPathName "`"$Target/host/DicomAgent.Host.exe`"" -StartupType Automatic | Out-Null
}
sc.exe config DicomAgent obj= 'NT SERVICE\DicomAgent' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot set service identity' }
icacls $Data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' 'NT SERVICE\DicomAgent:(OI)(CI)M' 'DicomAgentOperators:(OI)(CI)RX' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot configure data ACL' }
# Keep existing customer settings on upgrade. Firewall is only opened for explicit devices.
if (!(Test-Path "$Data/settings.json")) {
    @{ Dicom = @{ Port = $Port; AllowedRemoteAddresses = @($AllowedRemoteAddresses) } } | ConvertTo-Json -Depth 5 | Set-Content "$Data/settings.json" -Encoding UTF8
}
Get-NetFirewallRule -DisplayName 'DICOM Agent devices' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if ($AllowedRemoteAddresses.Count -gt 0) {
    New-NetFirewallRule -DisplayName 'DICOM Agent devices' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -RemoteAddress $AllowedRemoteAddresses -Profile Domain,Private -Program "$Target/host/DicomAgent.Host.exe" | Out-Null
}
Set-Service DicomAgent -StartupType Automatic
Start-Service DicomAgent
Write-Host "Installed. Sign out/in to refresh operator membership. Desktop: $Target/desktop/DicomAgent.Desktop.exe"
