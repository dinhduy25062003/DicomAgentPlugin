$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '../..')
dotnet publish src/DicomAgent.Host -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/windows/host
if ($LASTEXITCODE -ne 0) { throw 'Host publish failed' }
dotnet publish src/DicomAgent.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish/windows/desktop
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }
