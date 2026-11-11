#!/usr/bin/env bash
# Build the linux-x64 tarball the Flatpak manifest and the AUR PKGBUILD consume
# (docs/avalonia-linux-install.html). Local only: never uploads anything.
#   bash packaging/linux/build-tarball.sh [out-dir]   -> prints the tarball path
#   CCP_VLC_RUNTIME=<ccp-vlc-runtime-*.tar.gz>: the libVLC runtime (default packaging/linux/vlc/out/). Two paths, by
#   RUNTIME_URL in packaging/linux/vlc/versions.env (owner decision D2, 2026-11-11):
#     set:   fetched from RUNTIME_URL when missing; its sha256 must match RUNTIME_SHA256.
#     empty: built by packaging/linux/vlc/build.sh when missing, accepted once vlc/check.sh passes on it
#            (check.sh stamps <runtime>.checked with the sha256 it passed; no stamp or a different file = re-check).
# Layout: ConditioningControlPanel/{CCP.Avalonia, conditioning-control-panel.sh, LICENSE, Resources/Models/vosk/<model>,
#         lib/{libvlc.so.5,libvlccore.so.9,vlc/plugins}, VLC-NOTICES.txt, SOURCES.txt,
#         share/{applications,icons/hicolor/*/apps,metainfo}/<app-id>.*}  (share/ maps onto /usr/share or /app/share)
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
out=${1:-$root/packaging/linux/out}
ver=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Version.props")
pub=$root/CCP.Avalonia/bin/publish/linux-x64

# shellcheck source=vlc/versions.env
. "$root/packaging/linux/vlc/versions.env"
vlc=$root/packaging/linux/vlc
vlcrt=${CCP_VLC_RUNTIME:-$vlc/out/$ARTIFACT.tar.gz}
srcsha=$SOURCES_SHA256
if [ -n "${RUNTIME_URL:-}" ]; then
  if [ ! -f "$vlcrt" ]; then
    mkdir -p "$(dirname "$vlcrt")"
    curl -fL --retry 3 -o "$vlcrt.part" "$RUNTIME_URL" >&2
    mv "$vlcrt.part" "$vlcrt"
  fi
  echo "$RUNTIME_SHA256  $vlcrt" | sha256sum -c --quiet - >&2 \
    || { echo "build-tarball: $vlcrt is not the pinned runtime (RUNTIME_SHA256, packaging/linux/vlc/versions.env)" >&2; exit 1; }
  echo "build-tarball: libVLC runtime = FETCHED $RUNTIME_URL (sha256 pin matched)" >&2
else
  [ -f "$vlcrt" ] || bash "$vlc/build.sh" "$(dirname "$vlcrt")" >&2
  [ "$(cat "$vlcrt.checked" 2>/dev/null)" = "$(sha256sum < "$vlcrt" | cut -d' ' -f1)" ] || bash "$vlc/check.sh" "$vlcrt" >&2
  srcsha="(local build, not published)"
  if [ -f "$(dirname "$vlcrt")/$ARTIFACT-sources.tar" ]; then
    srcsha=$(sha256sum < "$(dirname "$vlcrt")/$ARTIFACT-sources.tar" | cut -d' ' -f1)
  fi
  echo "build-tarball: libVLC runtime = LOCAL BUILD $vlcrt (vlc/check.sh passed; no RUNTIME_URL, sha pin not applied)" >&2
fi

rm -rf "$pub"
dotnet publish "$root/CCP.Avalonia/CCP.Avalonia.csproj" -c Release -p:PublishProfile=linux-x64 >&2
test -f "$pub/libvosk.so"
# The small model (~40 MB, sha256-pinned), where SpeechEngine.DefaultModelRoots looks first.
model=$(bash "$root/.github/scripts/fetch-vosk-model.sh")
mkdir -p "$pub/Resources/Models/vosk"
cp -r "$model" "$pub/Resources/Models/vosk/"
cp -r "$root/packaging/linux/share" "$pub/"
cp "$root/LICENSE" "$pub/"
cp "$root/packaging/linux/RT-ENGINE-NOTICES.txt" "$pub/"
# libVLC loads in-process from lib/ (LibVlcNative), never through LD_LIBRARY_PATH. Sources: LGPL-2.1 s6, owner decision D2.
test ! -e "$pub/lib"
tar -xzf "$vlcrt" -C "$pub"
test -f "$pub/lib/libvlc.so.5"
cp "$root/packaging/linux/VLC-NOTICES.txt" "$pub/"
printf '%s\n' "Corresponding source for the bundled libVLC (lib/, see VLC-NOTICES.txt):" \
  "https://github.com/CC-Labs-it/Conditioning-Control-Panel/releases/download/v$ver/$ARTIFACT-sources.tar" \
  "sha256 $srcsha" > "$pub/SOURCES.txt"
cp "$root/packaging/linux/conditioning-control-panel.sh" "$pub/"   # the launcher: OpenSSL 3 hint, then CCP.Avalonia
mkdir -p "$pub/packaging/linux"
cp "$root/packaging/linux/rt-engine-check.sh" "$pub/packaging/linux/"
# A Windows checkout can hand these over with CRLF: a CRLF shebang cannot run, and desktop tools reject CRLF entries.
find "$pub" -type f \( -name '*.sh' -o -name '*.desktop' -o -name '*.metainfo.xml' \) -exec sed -i 's/\r$//' {} +

# A checkout with LFS off publishes the media as pointer files, and the app then plays nothing.
# Refuse them: run `git lfs pull --include "Assets/**"` first (CI does).
pointers=$(find "$pub" -type f -size -1025c -exec grep -l '^version https://git-lfs' {} + || true)
if [ -n "$pointers" ]; then
  echo "build-tarball: LFS pointer files instead of media (git lfs pull --include \"Assets/**\"):" >&2
  echo "$pointers" >&2
  exit 1
fi

mkdir -p "$out"
tar=$out/ConditioningControlPanel-$ver-linux-x64.tar.gz
# Reproducible: sorted names, fixed owner and mtime, no gzip timestamp. Modes are set here, not read from disk:
# a build on NTFS (Git Bash) has no exec bits, and the beta-1 tarball shipped a 0644 CCP.Avalonia
# ("Permission denied"). Executables (apphost, createdump, *.sh) and dirs 0755, everything else 0644.
py=
for c in python3 python; do "$c" -c 'import tarfile' >/dev/null 2>&1 && { py=$c; break; }; done   # Windows: python3 may be the Store stub
[ -n "$py" ] || { echo "build-tarball: python3 is required to pack the tarball" >&2; exit 1; }
"$py" - "$pub" "$tar" <<'PY'
import gzip, os, sys, tarfile
pub, out = sys.argv[1], sys.argv[2]
EXEC = {"CCP.Avalonia", "createdump"}
def norm(ti):
    ti.uid = ti.gid = 0; ti.uname = ti.gname = ""; ti.mtime = 0
    base = os.path.basename(ti.name)
    ti.mode = 0o755 if ti.isdir() or base in EXEC or base.endswith(".sh") else 0o644
    return ti
with open(out, "wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as gz, \
        tarfile.open(fileobj=gz, mode="w", format=tarfile.GNU_FORMAT) as t:
    t.add(pub, arcname="ConditioningControlPanel", filter=norm)   # add() recurses in sorted order
PY
echo "$tar"
