#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "${1:-}" == --help || "${1:-}" == -h ]]; then
    cat <<'HELP'
Usage: bash deploy/release.sh [VERSION]
Build both Linux amd64 .deb and Windows x64 Setup.exe on Linux.
Without VERSION, increments the patch version in Directory.Build.props.
Runs restore, build, tests, fresh publish, installer verification and SHA256SUMS.
Requires .NET SDK, PowerShell, NSIS, Python 3.11+, dpkg-deb, 7z and flock.
NSIS overrides: MAKENSIS and NSISDIR; also searches the per-user build cache.
Does not install the application, restart services, commit or push.
HELP
    exit 0
fi
[[ $# -le 1 ]] || { echo 'Usage: bash deploy/release.sh [VERSION]' >&2; exit 1; }

for dependency in dotnet pwsh python3 dpkg-deb 7z flock; do
    command -v "$dependency" >/dev/null || { echo "Missing build tool: $dependency" >&2; exit 1; }
done
python3 -c 'import sys; sys.version_info >= (3, 11) or sys.exit("Python 3.11+ is required.")'
dotnet --version
compiler="${MAKENSIS:-$(command -v makensis || true)}"
cache="${XDG_CACHE_HOME:-$HOME/.cache}/dicom-agent-build/nsis"
if [[ -z "$compiler" && -x "$cache/usr/bin/makensis" ]]; then
    compiler="$cache/usr/bin/makensis"
    export NSISDIR="${NSISDIR:-$cache/usr/share/nsis}"
fi
compiler="$(command -v "$compiler" || true)"
[[ -n "$compiler" && -x "$compiler" ]] || { echo 'Install NSIS or set MAKENSIS/NSISDIR before releasing.' >&2; exit 1; }
export MAKENSIS="$compiler"
mkdir -p publish/installers
exec 9>publish/.release.lock
flock -n 9 || { echo 'Another release build is running.' >&2; exit 1; }

version="$(python3 - "${1:-}" <<'PY'
import re
import sys
from pathlib import Path

props = Path('Directory.Build.props')
text = props.read_text()
match = re.search(r'<Version>(\d+\.\d+\.\d+)</Version>', text)
if not match:
    sys.exit('Directory.Build.props must contain one major.minor.patch Version.')
current = match[1]
parts = tuple(map(int, current.split('.')))
version = sys.argv[1] or f'{parts[0]}.{parts[1]}.{parts[2] + 1}'
if not re.fullmatch(r'\d+\.\d+\.\d+', version):
    sys.exit('Version must be major.minor.patch.')
if tuple(map(int, version.split('.'))) <= parts:
    sys.exit(f'Use a version newer than {current}; published versions must not be reused.')
for name in [f'dicom-agent_{version}_amd64.deb', f'DicomAgent-{version}-win-x64-Setup.exe']:
    if (Path('publish/installers') / name).exists():
        sys.exit(f'Artifact already exists: {name}. Choose a new version.')

updates = {props: text.replace(f'<Version>{current}</Version>', f'<Version>{version}</Version>')}
for name in ['deploy/linux/build-deb.sh', 'deploy/windows/build-installer.sh',
             'deploy/windows/build-installer.ps1', 'deploy/windows/installer/setup.nsi']:
    path = Path(name)
    value = path.read_text()
    if current not in value:
        sys.exit(f'Version default is out of sync: {name}')
    updates[path] = value.replace(current, version)

for name in ['README.md', 'docs/PACKAGES.md', 'docs/HUONG-DAN-LINUX.md',
             'docs/HUONG-DAN-WINDOWS.md', 'docs/LOCALHOST-WEB-SAAS.md']:
    path = Path(name)
    value = path.read_text()
    # Update current package references without rewriting historical release notes.
    value = value.replace(f'dicom-agent_{current}_amd64.deb', f'dicom-agent_{version}_amd64.deb')
    value = value.replace(f'DicomAgent-{current}-win-x64-Setup.exe', f'DicomAgent-{version}-win-x64-Setup.exe')
    value = value.replace(f'Bản **{current}**', f'Bản **{version}**')
    value = value.replace(f'Phiên bản hiện tại: **{current}**', f'Phiên bản hiện tại: **{version}**')
    value = value.replace(f'Setup Windows {current}', f'Setup Windows {version}')
    value = value.replace(f'Cài bản {current},', f'Cài bản {version},')
    value = value.replace(f'→ SaaS ({current})', f'→ SaaS ({version})')
    for command in ['build-deb.sh ', 'build-installer.sh ', 'build-installer.ps1 -Version ']:
        value = value.replace(command + current, command + version)
    updates[path] = value
for path, value in updates.items():
    path.write_text(value)
print(version)
PY
)"
echo "Building DICOM Agent $version"

dotnet restore DicomAgent.sln
dotnet build DicomAgent.sln -c Release --no-restore
dotnet test DicomAgent.sln -c Release --no-build

# These directories contain build output only; keep older installers and the Linux simulator.
rm -rf publish/windows/host publish/windows/desktop publish/windows/scu
pwsh -NoProfile -File deploy/windows/publish.ps1
bash deploy/windows/build-installer.sh "$version"
rm -rf publish/linux/host publish/linux/desktop
bash deploy/linux/publish.sh
bash deploy/linux/build-deb.sh "$version"

python3 - "$version" <<'PY'
import hashlib
import subprocess
import sys
import tempfile
from pathlib import Path

version = sys.argv[1]
installers = Path('publish/installers').resolve()
exe = installers / f'DicomAgent-{version}-win-x64-Setup.exe'
deb = installers / f'dicom-agent_{version}_amd64.deb'

def digest(path, algorithm='sha256'):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, algorithm).hexdigest()

def compare(source, packaged):
    for item in source.rglob('*'):
        if item.is_file() and item.suffix != '.pdb':
            target = packaged / item.relative_to(source)
            if not target.is_file() or digest(item) != digest(target):
                sys.exit(f'Missing or stale installer payload: {target}')

with tempfile.TemporaryDirectory(prefix='dicom-release-') as temporary:
    temporary = Path(temporary)
    windows = temporary / 'windows'
    linux = temporary / 'linux'
    subprocess.run(['7z', 'x', '-y', f'-o{windows}', str(exe)], check=True, stdout=subprocess.DEVNULL)
    for part in ['host', 'desktop', 'scu']:
        compare(Path('publish/windows') / part, windows / '$PLUGINSDIR/payload' / part)
    subprocess.run(['dpkg-deb', '-R', str(deb), str(linux)], check=True)
    for part in ['host', 'desktop']:
        compare(Path('publish/linux') / part, linux / 'opt/dicom-agent' / part)
    for line in (linux / 'DEBIAN/md5sums').read_text().splitlines():
        expected, name = line.split(maxsplit=1)
        if digest(linux / name, 'md5') != expected:
            sys.exit(f'Invalid Debian checksum: {name}')
    if f'Version: {version}\n' not in (linux / 'DEBIAN/control').read_text():
        sys.exit('Debian package version does not match the release.')

manifest = installers / 'SHA256SUMS'
lines = manifest.read_text().splitlines() if manifest.exists() else []
for path in [exe, deb]:
    lines = [line for line in lines if not line.endswith('  ' + path.name)]
    lines.append(f'{digest(path)}  {path.name}')
manifest.write_text('\n'.join(lines) + '\n')
print(f'Verified release {version}:')
print(exe)
print(deb)
print(manifest)
PY
