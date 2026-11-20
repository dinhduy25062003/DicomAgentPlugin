#!/bin/sh
set -eu
# Grant a named user's existing login session access; group membership alone needs a new login.
[ "$#" -eq 2 ] || { echo 'Usage: configure-operator-access DATA_DIRECTORY OPERATOR_UID' >&2; exit 1; }
data=$1
operator_uid=$2
case "$data" in /*) ;; *) echo 'Data directory must be absolute.' >&2; exit 1;; esac
case "$operator_uid" in ''|*[!0-9]*|0) echo 'A non-root numeric operator UID is required.' >&2; exit 1;; esac
[ -d "$data" ] && [ ! -L "$data" ] || { echo 'Data directory is missing or is a symlink.' >&2; exit 1; }
setfacl -m "u:${operator_uid}:rx,d:u:${operator_uid}:rx" "$data"
if [ -d "$data/storage" ]; then
    setfacl -m "u:${operator_uid}:rx,d:u:${operator_uid}:rx" "$data/storage"
fi
if [ -f "$data/desktop-connection.json" ]; then
    setfacl -m "u:${operator_uid}:r" "$data/desktop-connection.json"
fi
