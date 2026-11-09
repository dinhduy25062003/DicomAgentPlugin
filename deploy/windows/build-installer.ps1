param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0',
    [string]$MakeNsis = "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$SkipPublish) { & (Join-Path $PSScriptRoot 'publish.ps1') }
if (!(Test-Path $MakeNsis)) { throw 'Install NSIS, or pass -MakeNsis with the compiler path.' }
New-Item -ItemType Directory -Force (Join-Path $root 'publish/installers') | Out-Null
& $MakeNsis "/DROOT=$root" "/DVERSION=$Version" (Join-Path $PSScriptRoot 'installer/setup.nsi')
if ($LASTEXITCODE -ne 0) { throw 'Setup.exe compilation failed.' }
