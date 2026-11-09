#!/usr/bin/env bash
set -euo pipefail
umask 022
cd "$(dirname "$0")/../.."
version="${1:-0.1.0}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo 'Version must be major.minor.patch' >&2; exit 1; }
[[ -x publish/linux/host/DicomAgent.Host && -x publish/linux/desktop/DicomAgent.Desktop ]] || { echo 'Run deploy/linux/publish.sh first.' >&2; exit 1; }
mkdir -p publish/installers
stage="$(mktemp -d)"
artifact="$(mktemp publish/installers/.dicom-agent.XXXXXX.deb)"
trap 'rm -rf "$stage"; rm -f "$artifact"' EXIT
chmod 0755 "$stage"
install -d "$stage/DEBIAN" "$stage/opt/dicom-agent/host" "$stage/opt/dicom-agent/desktop" "$stage/lib/systemd/system" "$stage/usr/bin" "$stage/usr/share/applications" "$stage/usr/share/icons/hicolor/scalable/apps" "$stage/usr/share/doc/dicom-agent"
cp -a publish/linux/host/. "$stage/opt/dicom-agent/host/"
cp -a publish/linux/desktop/. "$stage/opt/dicom-agent/desktop/"
find "$stage/opt" -type f -name '*.pdb' -delete
find "$stage/opt" -type f -exec chmod 0644 {} +
chmod 0755 "$stage/opt/dicom-agent/host/DicomAgent.Host" "$stage/opt/dicom-agent/desktop/DicomAgent.Desktop"
find "$stage/opt" -type d -exec chmod 0755 {} +
install -m 0644 deploy/linux/dicom-agent.service "$stage/lib/systemd/system/"
install -m 0755 deploy/linux/debian/dicom-agent-desktop "$stage/usr/bin/"
install -m 0644 deploy/linux/debian/dicom-agent.desktop "$stage/usr/share/applications/"
install -m 0644 deploy/linux/debian/dicom-agent.svg "$stage/usr/share/icons/hicolor/scalable/apps/"
install -m 0644 README.md "$stage/usr/share/doc/dicom-agent/README.vi.md"
install -m 0644 docs/DEPLOYMENT.md "$stage/usr/share/doc/dicom-agent/DEPLOYMENT.vi.md"
for script in preinst postinst prerm postrm; do install -m 0755 "deploy/linux/debian/$script" "$stage/DEBIAN/$script"; done
size="$(du -sk "$stage/opt" | cut -f1)"
cat > "$stage/DEBIAN/control" <<EOF
Package: dicom-agent
Version: $version
Section: science
Priority: optional
Architecture: amd64
Maintainer: Local DicomAgent build <root@localhost>
Installed-Size: $size
Depends: adduser, passwd, systemd, init-system-helpers (>= 1.54~), libc6 (>= 2.34), libgcc-s1, libstdc++6, zlib1g, libssl3 | libssl3t64, libicu70 | libicu72 | libicu74 | libicu76 | libicu78, libfontconfig1, libfreetype6, libx11-6, libx11-xcb1, libxcb1, libice6, libsm6, libxrandr2, libxi6, libxcursor1, libxext6, libxrender1, libgl1
Recommends: xdg-utils
Description: Offline DICOM receiver and desktop management application
 Receives DICOM C-ECHO/C-STORE over a trusted local network, saves DICOM
 files and SQLite history, and provides an Avalonia desktop interface.
 Includes self-contained .NET 8 host and desktop binaries.
EOF
(cd "$stage"; find opt usr lib -type f -print0 | sort -z | xargs -0 md5sum > DEBIAN/md5sums)
mkdir -p publish/installers
output="$PWD/publish/installers/dicom-agent_${version}_amd64.deb"
dpkg-deb --root-owner-group -Zxz -z6 --build "$stage" "$artifact"
chmod 0644 "$artifact"
mv -f "$artifact" "$output"
echo "$output"
