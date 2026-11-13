#!/usr/bin/env bash
# Extract a built tarball to a temp dir and run --smoke from it, requiring the bundled Vosk model to load.
#   bash packaging/linux/smoke-tarball.sh <ConditioningControlPanel-*-linux-x64.tar.gz>
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
id=io.github.CodeBambi.ConditioningControlPanel
test -f "$app/LICENSE"
# Every bundled native needs a notice (packaging/linux/THIRD-PARTY-NOTICES.txt names each .so).
test -f "$app/RT-ENGINE-NOTICES.txt"
for so in "$app"/*.so; do
  grep -qF "$(basename "$so")" "$app/THIRD-PARTY-NOTICES.txt" ||
    { echo "$(basename "$so") has no entry in THIRD-PARTY-NOTICES.txt" >&2; exit 1; }
done
test -f "$app/share/applications/$id.desktop"
test -f "$app/share/applications/$id.Player.desktop"
test -f "$app/share/applications/$id.Editor.desktop"
test -f "$app/share/icons/hicolor/256x256/apps/$id.png"
test -f "$app/share/metainfo/$id.metainfo.xml"
# Sandboxed profile: never the user's real settings or keyring.
export CCP_USERDATA_DIR=$tmp/userdata XDG_CONFIG_HOME=$tmp/config
CCP_SMOKE_BUNDLED_VOSK=1 timeout 300 "$app/conditioning-control-panel.sh" --smoke
