#!/usr/bin/env bash
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo "Run with sudo." >&2; exit 1; }
systemctl disable --now dicom-agent.service
rm -f /etc/systemd/system/dicom-agent.service
systemctl daemon-reload
# Keep application data, settings, storage, service account and group for recovery.
echo "Service removed. Binaries remain in /opt/dicom-agent; customer data remains in /var/lib/dicom-agent and custom storage."
