#!/usr/bin/env bash
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo "Run with sudo." >&2; exit 1; }
source_dir="$(cd "${1:-$(dirname "$0")/../../publish/linux}" && pwd)"
operator="${2:-${SUDO_USER:-}}"
[[ -x "$source_dir/host/DicomAgent.Host" && -x "$source_dir/desktop/DicomAgent.Desktop" ]] || { echo "Publish both applications first." >&2; exit 1; }
getent group dicom-agent-operators >/dev/null || groupadd --system dicom-agent-operators
id dicom-agent >/dev/null 2>&1 || useradd --system --user-group --home-dir /var/lib/dicom-agent --shell /usr/sbin/nologin dicom-agent
if systemctl cat dicom-agent.service >/dev/null 2>&1; then systemctl stop dicom-agent.service; fi
install -d -m 0755 /opt/dicom-agent/host /opt/dicom-agent/desktop
cp -a "$source_dir/host/." /opt/dicom-agent/host/
cp -a "$source_dir/desktop/." /opt/dicom-agent/desktop/
chown -R root:root /opt/dicom-agent
chmod -R go-w /opt/dicom-agent
install -d -o dicom-agent -g dicom-agent-operators -m 2750 /var/lib/dicom-agent /var/lib/dicom-agent/storage
if [[ -n "$operator" ]]; then usermod -aG dicom-agent-operators "$operator"; fi
install -m 0644 "$(dirname "$0")/dicom-agent.service" /etc/systemd/system/dicom-agent.service
systemctl daemon-reload
systemctl enable --now dicom-agent.service
echo "Installed. Log out/in for operator group membership. No firewall ports were opened."
echo "Desktop: /opt/dicom-agent/desktop/DicomAgent.Desktop"
