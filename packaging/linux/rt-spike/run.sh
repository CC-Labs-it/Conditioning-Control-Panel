#!/usr/bin/env bash
# rt-linux-spike/run.sh - Racing Thoughts 3D on Linux: one command, about 25 minutes, no sudo.
#   bash run.sh
# Measures the game in every engine this box has, the same way on each: WebKitGTK (what the app uses today),
# WebKitGTK inside the Flatpak GNOME 50 runtime (what the Flatpak would use), Electron (our own Chromium,
# downloaded here, WebGL2 and WebGPU), the system Chrome/Chromium and Firefox (the browser fallback).
# Writes <results>/report.md and <results>.tar.gz: send the tar.gz back.
# Knobs (env): RACE_SECONDS=45 SIZE=1920x1080 REF=feat/glassroad-a GLASSROAD_DIR=<existing glassroad tree>
#              NO_FLATPAK=1 NO_ELECTRON=1 NO_BROWSERS=1 COURSES="threshold goonworks" OUT=<dir>
set -u
KIT="$(cd "$(dirname "$0")" && pwd)"
STAMP="$(date +%Y%m%d-%H%M)"
OUT="${OUT:-$PWD/rt-linux-spike-$(hostname -s 2>/dev/null || echo box)-$STAMP}"
CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/ccp-rt-spike"
RACE_SECONDS="${RACE_SECONDS:-45}"
SIZE="${SIZE:-1920x1080}"
REF="${REF:-feat/glassroad-a}"
COURSES="${COURSES:-threshold goonworks}"
PORT=8791
REPO_URL="https://github.com/CC-Labs-it/Conditioning-Control-Panel.git"
mkdir -p "$OUT/runs" "$OUT/engine" "$OUT/profiles" "$CACHE"
LOG="$OUT/run.log"
say() { echo "[spike] $*" | tee -a "$LOG"; }
have() { command -v "$1" >/dev/null 2>&1; }
case "$(uname -m)" in x86_64) ARCH=x64 ;; aarch64|arm64) ARCH=arm64 ;; *) ARCH="$(uname -m)" ;; esac

say "results -> $OUT"
say "Leave the machine alone while windows open and close (about 25 min). A gamepad? Plug it in and press a button"
say "whenever a race window is open."

# 1. the box
{
  echo "date: $(date -Is)"; echo "kernel: $(uname -srm)"
  [ -r /etc/os-release ] && grep -E '^(PRETTY_NAME|VERSION_ID|ID)=' /etc/os-release
  echo "session: ${XDG_SESSION_TYPE:-?} desktop: ${XDG_CURRENT_DESKTOP:-?} wayland_display: ${WAYLAND_DISPLAY:-none} display: ${DISPLAY:-none}"
  echo "cpu: $(grep -m1 'model name' /proc/cpuinfo 2>/dev/null | cut -d: -f2- | sed 's/^ *//') x$(nproc)"
  echo "ram: $(awk '/MemTotal/ {printf "%.1f GB", $2/1048576}' /proc/meminfo)"
  have lspci && lspci -nn | grep -Ei 'vga|3d|display'
  have glxinfo && glxinfo -B 2>/dev/null | grep -E 'OpenGL (vendor|renderer|core profile version)|Accelerated'
  have vulkaninfo && vulkaninfo --summary 2>/dev/null | grep -E 'deviceName|driverName|driverInfo|apiVersion' | head -8
  have nvidia-smi && nvidia-smi --query-gpu=name,driver_version --format=csv,noheader
  for m in webkit2gtk-4.1 webkit2gtk-4.0 gstreamer-1.0; do have pkg-config && pkg-config --exists $m && echo "$m (dev): $(pkg-config --modversion $m)"; done
  have dpkg-query && dpkg-query -W -f='${Package} ${Version}\n' 'libwebkit2gtk-4.1-0' 'libwebkit2gtk-4.0-37' 'gir1.2-webkit2-4.1' 'gstreamer1.0-libav' 'gstreamer1.0-plugins-bad' 2>/dev/null
  have rpm && rpm -q webkit2gtk4.1 webkit2gtk3 gstreamer1-libav 2>/dev/null | grep -v 'not installed'
  have pacman && pacman -Q webkit2gtk-4.1 webkit2gtk gst-libav 2>/dev/null
  have flatpak && { echo "flatpak: $(flatpak --version)"; flatpak list --runtime --columns=ref 2>/dev/null | grep -E 'org.gnome.Platform|org.freedesktop.Platform.GL' ; }
  for b in google-chrome-stable google-chrome chromium chromium-browser firefox; do have $b && echo "$b: $($b --version 2>/dev/null | head -1)"; done
  have xrandr && xrandr --current 2>/dev/null | grep -E '\*' | head -3
} > "$OUT/system.txt" 2>&1
say "system facts in system.txt"

# 2. node 22+ (the bench server)
NODE=node
nv() { "$1" -e 'process.stdout.write(process.versions.node.split(".")[0])' 2>/dev/null || echo 0; }
if ! have node || [ "$(nv node)" -lt 22 ]; then
  NDIR="$CACHE/node"
  if [ ! -x "$NDIR/bin/node" ]; then
    say "fetching Node 22 (no system node 22+)"
    base="https://nodejs.org/dist/latest-v22.x"
    sums="$(curl -fsSL "$base/SHASUMS256.txt")" || { say "cannot reach nodejs.org"; exit 2; }
    name="$(echo "$sums" | awk '{print $2}' | grep -E "linux-$ARCH\.tar\.xz$" | head -1)"
    curl -fsSL -o "$CACHE/$name" "$base/$name" || { say "node download failed"; exit 2; }
    (cd "$CACHE" && echo "$sums" | grep " $name\$" | sha256sum -c -) >>"$LOG" 2>&1 || { say "node checksum FAILED"; exit 2; }
    mkdir -p "$NDIR" && tar -xJf "$CACHE/$name" -C "$NDIR" --strip-components=1
  fi
  NODE="$NDIR/bin/node"
fi
say "node $("$NODE" --version)"

# 3. the game tree (public repo, sparse, LFS off: the glassroad tree has no LFS files)
if [ -n "${GLASSROAD_DIR:-}" ]; then
  GR="$GLASSROAD_DIR"; SHA="local"
else
  REPO="$CACHE/repo"
  if [ ! -d "$REPO/.git" ]; then
    say "fetching the game tree ($REF, ~110 MB)"
    GIT_LFS_SKIP_SMUDGE=1 git -c filter.lfs.process= -c filter.lfs.smudge= -c filter.lfs.required=false \
      clone --filter=blob:none --no-checkout --depth 1 --branch "$REF" "$REPO_URL" "$REPO" >>"$LOG" 2>&1 || { say "git clone failed (see run.log)"; exit 2; }
    git -C "$REPO" sparse-checkout set --no-cone '/ConditioningControlPanel/Resources/web/glassroad/' '/ConditioningControlPanel/Resources/web/arcademy/' >>"$LOG" 2>&1
  else
    git -C "$REPO" -c filter.lfs.process= -c filter.lfs.smudge= -c filter.lfs.required=false fetch --depth 1 origin "$REF" >>"$LOG" 2>&1
  fi
  GIT_LFS_SKIP_SMUDGE=1 git -C "$REPO" -c filter.lfs.process= -c filter.lfs.smudge= -c filter.lfs.required=false checkout -q --detach FETCH_HEAD 2>>"$LOG" \
    || GIT_LFS_SKIP_SMUDGE=1 git -C "$REPO" -c filter.lfs.process= -c filter.lfs.smudge= -c filter.lfs.required=false checkout -q "$REF" >>"$LOG" 2>&1
  GR="$REPO/ConditioningControlPanel/Resources/web/glassroad"
  SHA="$(git -C "$REPO" rev-parse --short=10 HEAD)"
fi
[ -f "$GR/index.html" ] || { say "no game tree at $GR"; exit 2; }
echo "glassroad: $SHA ($REF)" >> "$OUT/system.txt"
say "game tree $SHA"

# 4. the bench server
"$NODE" "$KIT/bench-server.mjs" --root "$GR" --out "$OUT" --port $PORT >>"$LOG" 2>&1 &
SERVER=$!
trap 'kill $SERVER 2>/dev/null' EXIT
for i in $(seq 1 50); do curl -fs "http://127.0.0.1:$PORT/__spike/hashes" >/dev/null 2>&1 && break; sleep 0.2; done
curl -fs "http://127.0.0.1:$PORT/__spike/hashes" > "$OUT/engine/node-hashes.json" || { say "bench server did not start (see run.log)"; exit 2; }

OPTS='%7B%22renderScale%22%3A1%2C%22quality%22%3A%22high%22%7D'   # {"renderScale":1,"quality":"high"} before boot
W="${SIZE%x*}"; H="${SIZE#*x}"

# one run: $1 = run id, $2 = page params, rest = the command (@URL@ anywhere in an argument becomes the URL, else it is appended)
case_run() {
  local run="$1" params="$2"; shift 2
  local url="http://127.0.0.1:$PORT/?bot=1&seed=7&spikeRun=$run&spikeSec=$RACE_SECONDS&spikeOpts=$OPTS&$params"
  local args=() hit=0
  for x in "$@"; do if [[ "$x" == *@URL@* ]]; then args+=("${x//@URL@/"$url"}"); hit=1; else args+=("$x"); fi; done
  [ $hit = 1 ] || args+=("$url")
  rm -f "$OUT/runs/$run.json"
  say "run $run"
  setsid "${args[@]}" >>"$OUT/engine/$run.log" 2>&1 &
  local pid=$!
  echo "$pid" > "$OUT/current.pid"
  local end=$(( $(date +%s) + 240 + RACE_SECONDS ))
  while [ ! -f "$OUT/runs/$run.json" ] && [ "$(date +%s)" -lt "$end" ] && kill -0 "$pid" 2>/dev/null; do sleep 2; done
  sleep 2
  kill -TERM -- -"$pid" 2>/dev/null; kill -TERM "$pid" 2>/dev/null; sleep 3
  kill -KILL -- -"$pid" 2>/dev/null; kill -KILL "$pid" 2>/dev/null
  if [ -f "$OUT/runs/$run.json" ]; then say "  done"; else say "  NO RESULT (window died or timed out; engine/$run.log)"; echo "{\"run\":\"$run\",\"failed\":\"no result (engine died or timed out)\"}" > "$OUT/runs/$run.json"; fi
  sleep 3
}

# 5a. WebKitGTK, system (what the app's web view uses today)
if python3 -c "import gi; gi.require_version('Gtk','3.0'); gi.require_version('WebKit2','4.1')" 2>/dev/null \
   || python3 -c "import gi; gi.require_version('Gtk','3.0'); gi.require_version('WebKit2','4.0')" 2>/dev/null; then
  first=1
  for c in $COURSES; do
    extra=(); sc=""
    [ $first = 1 ] && { extra=(--scrolller-block); sc="&spikeScrolller=check"; first=0; }
    case_run "wk-$c" "quality=high&track=$c$sc" python3 "$KIT/wk-bench.py" --size "$SIZE" --info "$OUT/engine/wk-$c.info.json" ${extra[@]+"${extra[@]}"} @URL@
  done
  # the auto ladder on this box (what a player gets with Quality on Auto)
  case_run "wk-threshold-auto" "track=threshold" python3 "$KIT/wk-bench.py" --size "$SIZE" --info "$OUT/engine/wk-threshold-auto.info.json" @URL@
else
  say "SKIP WebKitGTK system: no python3-gi WebKit2 typelib (Debian/Ubuntu: gir1.2-webkit2-4.1, Fedora: webkit2gtk4.1, Arch: webkit2gtk-4.1)"
  echo "wk: skipped, no typelib" >> "$OUT/system.txt"
fi

# 5b. WebKitGTK inside the Flatpak GNOME 50 runtime (packaging/flatpak uses org.gnome.Platform//50)
if [ -z "${NO_FLATPAK:-}" ] && have flatpak; then
  RT=""
  for r in org.gnome.Platform//50 org.gnome.Sdk//50; do flatpak info "$r" >/dev/null 2>&1 && { RT="$r"; break; }; done
  if [ -z "$RT" ]; then
    say "installing org.gnome.Platform//50 for this user (flathub, a few hundred MB; NO_FLATPAK=1 skips)"
    flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo >>"$LOG" 2>&1
    flatpak install --user -y --noninteractive flathub org.gnome.Platform//50 >>"$LOG" 2>&1 && RT=org.gnome.Platform//50
  fi
  if [ -n "$RT" ]; then
    FP=(flatpak run --die-with-parent --share=network --socket=x11 --socket=wayland --socket=pulseaudio --device=dri
        --filesystem="$KIT":ro --filesystem="$OUT" --command=python3 "$RT")
    if "${FP[@]}" -c "import gi; gi.require_version('Gtk','3.0'); gi.require_version('WebKit2','4.1')" >>"$LOG" 2>&1; then
      first=1
      for c in $COURSES; do
        extra=(); sc=""
        [ $first = 1 ] && { extra=(--scrolller-block); sc="&spikeScrolller=check"; first=0; }
        case_run "flatpak-$c" "quality=high&track=$c$sc" "${FP[@]}" "$KIT/wk-bench.py" --size "$SIZE" --info "$OUT/engine/flatpak-$c.info.json" ${extra[@]+"${extra[@]}"} @URL@
      done
    else
      say "SKIP Flatpak: $RT has no python3 WebKit2 4.1 typelib"; echo "flatpak: no typelib in $RT" >> "$OUT/system.txt"
    fi
  else
    say "SKIP Flatpak: runtime not installed"; echo "flatpak: runtime missing" >> "$OUT/system.txt"
  fi
fi

# 5c. Electron = our own Chromium for the game (the download-on-first-race candidate). Spike runs --no-sandbox:
# Ubuntu 24.04+ blocks the unprivileged user namespaces Chromium's sandbox needs (a shipping build must solve that).
if [ -z "${NO_ELECTRON:-}" ]; then
  EDIR="$CACHE/electron"
  if [ ! -x "$EDIR/electron" ]; then
    tag="$(curl -fsSL https://api.github.com/repos/electron/electron/releases/latest | grep -m1 '"tag_name"' | cut -d'"' -f4)"
    if [ -n "$tag" ]; then
      say "fetching Electron $tag (~110 MB)"
      zip="electron-$tag-linux-$ARCH.zip"
      curl -fsSL -o "$CACHE/$zip" "https://github.com/electron/electron/releases/download/$tag/$zip" \
        && curl -fsSL -o "$CACHE/SHASUMS256-electron.txt" "https://github.com/electron/electron/releases/download/$tag/SHASUMS256.txt" \
        && (cd "$CACHE" && grep " \*\?$zip\$" SHASUMS256-electron.txt | sed 's/ \*/  /' | sha256sum -c -) >>"$LOG" 2>&1 \
        && mkdir -p "$EDIR" && { if have unzip; then unzip -q -o "$CACHE/$zip" -d "$EDIR"; else python3 -m zipfile -e "$CACHE/$zip" "$EDIR"; fi; } \
        && chmod +x "$EDIR/electron" "$EDIR/chrome_crashpad_handler" 2>/dev/null
      [ -x "$EDIR/electron" ] || say "Electron download or checksum failed (run.log)"
    fi
  fi
  if [ -x "$EDIR/electron" ]; then
    for c in $COURSES; do
      SPIKE_SIZE="$SIZE" SPIKE_INFO="$OUT/engine/electron-gl-$c.info.json" \
        case_run "electron-gl-$c" "quality=high&track=$c&renderer=webgl2" env SPIKE_SIZE="$SIZE" SPIKE_INFO="$OUT/engine/electron-gl-$c.info.json" "$EDIR/electron" --no-sandbox "$KIT/electron-main.js" @URL@
      case_run "electron-gpu-$c" "quality=high&track=$c" env SPIKE_WEBGPU=1 SPIKE_SIZE="$SIZE" SPIKE_INFO="$OUT/engine/electron-gpu-$c.info.json" "$EDIR/electron" --no-sandbox "$KIT/electron-main.js" @URL@
    done
  else
    say "SKIP Electron"; echo "electron: skipped" >> "$OUT/system.txt"
  fi
fi

# 5d. the browser fallback: the system Chrome/Chromium and Firefox with their own defaults
if [ -z "${NO_BROWSERS:-}" ]; then
  for b in google-chrome-stable google-chrome chromium chromium-browser; do
    if have $b; then
      for c in $COURSES; do
        case_run "chrome-$c" "quality=high&track=$c" $b --user-data-dir="$OUT/profiles/chrome-$c" --mute-audio --no-first-run --no-default-browser-check \
          --autoplay-policy=no-user-gesture-required --window-size="$W,$H" --window-position=0,0 "--app=@URL@"
      done
      break
    fi
  done
  if have firefox; then
    for c in $COURSES; do
      P="$OUT/profiles/firefox-$c"; mkdir -p "$P"
      printf '%s\n' 'user_pref("media.volume_scale", "0.0");' 'user_pref("browser.shell.checkDefaultBrowser", false);' \
        'user_pref("datareporting.policy.dataSubmissionEnabled", false);' 'user_pref("media.autoplay.default", 0);' > "$P/user.js"
      case_run "firefox-$c" "quality=high&track=$c" firefox --no-remote --profile "$P" -width "$W" -height "$H" @URL@
    done
  fi
fi

# 6. report
"$NODE" "$KIT/make-report.mjs" "$OUT" "$KIT/report-template.md" >>"$LOG" 2>&1 && say "report: $OUT/report.md" || say "report step failed (run.log); the raw runs/*.json are still in the folder"
rm -rf "$OUT/profiles" "$OUT/current.pid"
tar -czf "$OUT.tar.gz" -C "$(dirname "$OUT")" "$(basename "$OUT")"
say "DONE. Open $OUT/report.md, fill the short 'By hand' part at the end, then run:"
say "  tar -czf $OUT.tar.gz -C $(dirname "$OUT") $(basename "$OUT")"
say "and send $OUT.tar.gz back."
