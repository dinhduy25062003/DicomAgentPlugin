#!/usr/bin/env bash
# Resolve the SDK pinned by this repository before invoking any child build tools.
dicom_use_required_dotnet() {
    local repository candidate sdk host_path host_directory requested
    local -a candidates
    repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
    candidates=("$(command -v dotnet || true)")
    if [[ -n "${DOTNET_ROOT:-}" ]]; then
        candidates+=("$DOTNET_ROOT/dotnet")
    fi
    if [[ -n "${HOME:-}" ]]; then
        candidates+=("$HOME/.dotnet/dotnet")
    fi
    candidates+=("/usr/share/dotnet/dotnet" "/usr/lib/dotnet/dotnet")

    for candidate in "${candidates[@]}"; do
        [[ -n "$candidate" && -x "$candidate" ]] || continue
        # The host resolves global.json itself, including its rollForward policy.
        if sdk="$(cd "$repository" && "$candidate" --version 2>/dev/null)"; then
            host_path="$(python3 - "$candidate" <<'PY'
import os
import sys
print(os.path.realpath(sys.argv[1]))
PY
)"
            host_directory="$(dirname "$host_path")"
            export PATH="$host_directory:$PATH"
            export DOTNET_ROOT="$host_directory"
            export DOTNET_ROOT_X64="$host_directory"
            export DOTNET_HOST_PATH="$host_path"
            hash -r
            echo "Using .NET SDK $sdk ($host_path)"
            return 0
        fi
    done

    requested="$(python3 - "$repository/global.json" <<'PY'
import json
import sys
with open(sys.argv[1]) as stream:
    print(json.load(stream)['sdk']['version'])
PY
)"
    echo "No compatible .NET SDK found for global.json (requested $requested)." >&2
    echo 'Checked PATH, DOTNET_ROOT, ~/.dotnet, /usr/share/dotnet and /usr/lib/dotnet.' >&2
    echo "Install SDK $requested into ~/.dotnet or set DOTNET_ROOT to an installation containing it, then run release again." >&2
    return 1
}

if dicom_use_required_dotnet; then
    unset -f dicom_use_required_dotnet
else
    unset -f dicom_use_required_dotnet
    return 1
fi
