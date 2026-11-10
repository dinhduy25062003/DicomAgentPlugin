#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
version="${1:-0.1.1}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Version must be major.minor.patch' >&2; exit 1; }
compiler="${MAKENSIS:-makensis}"
[[ -f publish/windows/host/DicomAgent.Host.exe && -f publish/windows/desktop/DicomAgent.Desktop.exe && -f publish/windows/scu/DicomAgent.Scu.exe ]] || { echo 'Run deploy/windows/publish.ps1 to build Host, Desktop and SCU first.' >&2; exit 1; }
mkdir -p publish/installers
"$compiler" -V3 "-DROOT=$PWD" "-DVERSION=$version" deploy/windows/installer/setup.nsi
