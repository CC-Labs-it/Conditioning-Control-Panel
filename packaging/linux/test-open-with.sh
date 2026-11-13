#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
export HOME="$tmp/home with spaces" XDG_DATA_HOME="$tmp/data" XDG_CACHE_HOME="$tmp/cache" XDG_CONFIG_HOME="$tmp/config" CCP_USERDATA_DIR="$tmp/userdata"
id=io.github.CodeBambi.ConditioningControlPanel
app="$tmp/build/ConditioningControlPanel"
mkdir -p "$app" "$HOME"
cp -r "$root/packaging/linux/share" "$app/"
printf '#!/bin/sh\nexit 0\n' > "$app/CCP.Avalonia"
chmod +x "$app/CCP.Avalonia"
tar -czf "$tmp/build.tar.gz" -C "$tmp/build" ConditioningControlPanel
bash "$root/packaging/linux/install.sh" --tarball "$tmp/build.tar.gz" --no-deps --no-check -y >/dev/null
for pair in Player:play Editor:edit; do
  name=${pair%:*}; action=${pair#*:}; entry="$XDG_DATA_HOME/applications/$id.$name.desktop"
  desktop-file-validate "$entry"
  grep -Fx "Exec=\"$HOME/.local/bin/conditioning-control-panel\" --$action %f" "$entry"
  grep -Fx 'NoDisplay=true' "$entry"
  for mime in video/mp4 video/webm video/x-matroska video/quicktime video/x-msvideo video/x-m4v audio/mpeg audio/x-wav audio/mp4 audio/aac audio/flac audio/ogg; do
    grep -F "$mime;" "$entry" >/dev/null
  done
  for mime in video/mp4 audio/mpeg; do
    grep -E "^$mime=.*$id\.$name\.desktop;" "$XDG_DATA_HOME/applications/mimeinfo.cache" >/dev/null
  done
done
bash "$root/packaging/linux/install.sh" --uninstall -y >/dev/null
for name in Player Editor; do
  test ! -e "$XDG_DATA_HOME/applications/$id.$name.desktop"
  ! grep -F "$id.$name.desktop" "$XDG_DATA_HOME/applications/mimeinfo.cache"
done
echo 'Open With install and uninstall passed'
