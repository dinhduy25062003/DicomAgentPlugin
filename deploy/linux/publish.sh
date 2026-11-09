#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet publish src/DicomAgent.Host -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/linux/host
dotnet publish src/DicomAgent.Desktop -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false -o publish/linux/desktop
