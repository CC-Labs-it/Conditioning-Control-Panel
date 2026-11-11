#!/usr/bin/env bash
# Extract a built tarball to a temp dir and run --smoke from it, requiring the bundled Vosk model to load and the
# bundled libVLC (lib/, not the distro's) to load and decode H.264+AAC.
#   bash packaging/linux/smoke-tarball.sh <ConditioningControlPanel-*-linux-x64.tar.gz> [h264_aac.mp4]
# The clip defaults to CCP_SMOKE_VLC_CLIP, else the one packaging/linux/vlc/check.sh --clips makes (needs docker/podman).
# Needs no VLC on the host: run it in a container without one, and on Fedora with vlc-plugin-pipewire (#4441).
set -euo pipefail
# The AUR PKGBUILD downloads the release asset by pkgver, so it must follow Version.props (it sat at 6.11.3 under 7.0.5).
pkgver=$(sed -n 's/^pkgver=//p' "$(dirname "$0")/../aur/PKGBUILD")
case "$(basename "$1")" in
  "ConditioningControlPanel-$pkgver-linux-x64.tar.gz") ;;
  *) echo "packaging/aur/PKGBUILD pkgver=$pkgver does not match $(basename "$1"); bump it (see the PKGBUILD header)" >&2; exit 1 ;;
esac
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
tar -xzf "$1" -C "$tmp"
app=$tmp/ConditioningControlPanel
test -x "$app/CCP.Avalonia"
test -x "$app/conditioning-control-panel.sh"
test -f "$app/libvosk.so"
test -f "$app/lib/libvlc.so.5"
test -s "$app/lib/vlc/plugins/plugins.dat"
test -f "$app/VLC-NOTICES.txt"
grep -q 'releases/download/.*-sources.tar' "$app/SOURCES.txt"
clip=${2:-${CCP_SMOKE_VLC_CLIP:-}}
if [ -z "$clip" ]; then
  clip=$(dirname "$0")/vlc/out/clips/h264_aac.mp4
  [ -f "$clip" ] || bash "$(dirname "$0")/vlc/check.sh" --clips >&2
fi
export CCP_SMOKE_VLC_CLIP
CCP_SMOKE_VLC_CLIP=$(realpath "$clip")
id=io.github.CodeBambi.ConditioningControlPanel
test -f "$app/LICENSE"
test -f "$app/share/applications/$id.desktop"
test -f "$app/share/icons/hicolor/256x256/apps/$id.png"
test -f "$app/share/metainfo/$id.metainfo.xml"
# Sandboxed profile: never the user's real settings or keyring.
export CCP_USERDATA_DIR=$tmp/userdata XDG_CONFIG_HOME=$tmp/config
# CCP_VLC is emptied: the tester's escape hatch must not turn this into a distro-VLC run.
CCP_SMOKE_BUNDLED_VOSK=1 CCP_SMOKE_BUNDLED_VLC=1 CCP_VLC= timeout 300 "$app/conditioning-control-panel.sh" --smoke
