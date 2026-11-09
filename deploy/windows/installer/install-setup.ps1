param([Parameter(Mandatory)][string]$Payload, [Parameter(Mandatory)][string]$OperatorFile)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $env:TEMP 'DicomAgent-setup.log') -Force | Out-Null
try {
    $operatorUser = (Get-Content -LiteralPath $OperatorFile -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($operatorUser)) { throw 'An operator Windows account is required.' }
    & (Join-Path $Payload 'install.ps1') -Source $Payload -OperatorUser $operatorUser
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
} finally { Stop-Transcript | Out-Null }
