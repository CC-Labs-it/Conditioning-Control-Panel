#!/usr/bin/env bash
# Conditioning Control Panel - Linux installer (x86_64).
#
#   curl -fsSL <url-of-this-file> | bash                 install or update for this user (~/.local)
#   bash install.sh --tarball ConditioningControlPanel-<ver>-linux-x64.tar.gz
#   bash install.sh --uninstall [--purge]
#   bash install.sh --help
#
# Layout matches packaging/aur/PKGBUILD and "Installing the tarball by hand" in docs/avalonia-linux-install.html:
#   <opt>/conditioning-control-panel/   the extracted tarball (replaced as a whole on update, never patched in place)
#   <bin>/conditioning-control-panel    launcher
#   <share>/applications|icons|metainfo desktop entry (Exec is the launcher's absolute path), icons, AppStream
# User data (~/.local/share/ConditioningControlPanel, ~/.config/ConditioningControlPanel) is never touched,
# except by --uninstall --purge.
#
# System packages: the script probes the libraries the app needs (the same list as
# CCP.Core LinuxDependencies, plus the VLC pulse output the --aout=pulse,alsa,jack,none fix uses) and only asks
# the package manager for the ones that are missing. Supported: Arch/CachyOS/Manjaro, Debian/Ubuntu,
# Fedora/Nobara, openSUSE. Anything else gets the list of missing libraries and the install continues.
#
# Everything sits inside main(), called on the last line, so `curl | bash` never runs a half-downloaded script.

main() {
set -euo pipefail

REPO=CC-Labs-it/Conditioning-Control-Panel
APP_ID=io.github.CodeBambi.ConditioningControlPanel
NAME=conditioning-control-panel

# ---------- options ----------
tarball='' release='' mode=user deps=1 deps_only=0 uninstall=0 purge=0 yes=0 check=1
usage() { cat <<EOF
Conditioning Control Panel installer

  --tarball FILE   install from a local tarball instead of downloading
  --release TAG    install this release tag (default: the newest release with a Linux build)
  --system         install for every user into /opt and /usr/local (asks for sudo)
  --no-deps        do not install system packages, only report what is missing
  --deps-only      only install the system packages
  --no-check       skip the post-install self-check (launcher smoke + GUI test)
  --uninstall      remove CCP; your settings and data stay
  --purge          with --uninstall: also delete your settings and data
  -y, --yes        do not ask questions
  -h, --help       this text
EOF
}
while [ $# -gt 0 ]; do
  case $1 in
    --tarball) tarball=${2:?--tarball needs a file}; shift ;;
    --release) release=${2:?--release needs a tag}; shift ;;
    --system) mode=system ;;
    --no-deps) deps=0 ;;
    --deps-only) deps_only=1 ;;
    --no-check) check=0 ;;
    --uninstall) uninstall=1 ;;
    --purge) purge=1 ;;
    -y|--yes) yes=1 ;;
    -h|--help) usage; return 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; return 2 ;;
  esac
  shift
done

# ---------- output ----------
if [ -t 1 ]; then B=$'\e[1m' G=$'\e[32m' Y=$'\e[33m' R=$'\e[31m' N=$'\e[0m'; else B='' G='' Y='' R='' N=''; fi
say()  { printf '%s\n' "$*"; }
step() { printf '%s==>%s %s\n' "$B" "$N" "$*"; }
ok()   { printf '  %s✓%s %s\n' "$G" "$N" "$*"; }
warn() { printf '  %s!%s %s\n' "$Y" "$N" "$*" >&2; }
die()  { printf '%serror:%s %s\n' "$R" "$N" "$*" >&2; exit 1; }

# Questions read the terminal, not stdin: under `curl | bash` stdin is the script itself.
tty=; { : </dev/tty; } 2>/dev/null && tty=/dev/tty
ask() {  # ask "question" -> 0 yes / 1 no. Default yes. No terminal and no --yes: no.
  [ $yes = 1 ] && return 0
  [ -n "$tty" ] || return 1
  local a; printf '%s [Y/n] ' "$1" >/dev/tty; read -r a </dev/tty || return 1
  case $a in ''|y|Y|yes|Yes) return 0 ;; *) return 1 ;; esac
}

# ---------- privilege ----------
SUDO=
if [ "$(id -u)" -ne 0 ]; then
  if command -v sudo >/dev/null; then SUDO=sudo
  elif command -v doas >/dev/null; then SUDO=doas
  fi
fi
as_root() {  # stdin from the terminal so sudo/package managers can prompt
  if [ "$(id -u)" -eq 0 ]; then "$@" </dev/null
  elif [ -n "$SUDO" ]; then $SUDO "$@" <"${tty:-/dev/null}"
  else return 127
  fi
}

# ---------- paths ----------
if [ $mode = system ]; then
  OPT=/opt BIN=/usr/local/bin SHARE=/usr/local/share
  [ "$(id -u)" -eq 0 ] || [ -n "$SUDO" ] || die "--system needs root, sudo or doas"
  T() { as_root "$@"; }
else
  [ "$(id -u)" -ne 0 ] || warn "running as root without --system installs into root's home ($HOME)"
  OPT=$HOME/.local/opt BIN=$HOME/.local/bin SHARE=${XDG_DATA_HOME:-$HOME/.local/share}
  T() { "$@"; }
fi
APP=$OPT/$NAME
LAUNCHER=$BIN/$NAME
DATA_DIRS=("${XDG_DATA_HOME:-$HOME/.local/share}/ConditioningControlPanel" "${XDG_CONFIG_HOME:-$HOME/.config}/ConditioningControlPanel")

refresh_desktop() {
  command -v update-desktop-database >/dev/null && T update-desktop-database -q "$SHARE/applications" 2>/dev/null || true
  command -v gtk-update-icon-cache >/dev/null && T gtk-update-icon-cache -q -t "$SHARE/icons/hicolor" 2>/dev/null || true
}

app_running() { pgrep -f "^$APP/CCP.Avalonia" >/dev/null 2>&1; }

# ---------- uninstall ----------
if [ $uninstall = 1 ]; then
  step "Removing Conditioning Control Panel ($mode install)"
  app_running && die "CCP is running from $APP; close it first"
  [ -e "$APP" ] || [ -e "$LAUNCHER" ] || warn "nothing installed at $APP"
  T rm -rf "$APP" "$APP.new" "$APP.old"
  T rm -f "$LAUNCHER" "$SHARE/applications/$APP_ID.desktop" "$SHARE/applications/$APP_ID.Player.desktop" "$SHARE/applications/$APP_ID.Editor.desktop" "$SHARE/metainfo/$APP_ID.metainfo.xml"
  T find "$SHARE/icons/hicolor" -path "*/apps/$APP_ID.*" -delete 2>/dev/null || true
  refresh_desktop
  ok "app, launcher and menu entry removed"
  if [ $purge = 1 ]; then
    if [ $yes = 1 ] || ask "Delete your CCP settings, progress and downloaded content (${DATA_DIRS[*]})?"; then
      rm -rf "${DATA_DIRS[@]}"; ok "settings and data deleted"
    else say "  kept your settings and data"; fi
  else
    say "  Your settings and data were kept: ${DATA_DIRS[*]}"
  fi
  return 0
fi

# ---------- preflight ----------
step "Checking this system"
[ "$(uname -s)" = Linux ] || die "this installer is for Linux"
[ "$(uname -m)" = x86_64 ] || die "only x86_64 builds exist (this machine is $(uname -m))"
for c in tar gzip sha256sum find sed; do command -v $c >/dev/null || die "missing command: $c"; done
dl=
if command -v curl >/dev/null; then dl=curl; elif command -v wget >/dev/null; then dl=wget; fi
[ -n "$tarball" ] || [ $deps_only = 1 ] || [ -n "$dl" ] || die "need curl or wget to download (or pass --tarball)"
fetch() {  # fetch URL [file]  (no file: stdout)
  if [ "$dl" = curl ]; then
    if [ -n "${2:-}" ]; then curl -fL --retry 3 --progress-bar -o "$2" "$1"; else curl -fsSL --retry 3 "$1"; fi
  else
    if [ -n "${2:-}" ]; then wget -q --show-progress -O "$2" "$1"; else wget -qO- "$1"; fi
  fi
}

fam=unknown pretty=$(uname -sr)
if [ -r /etc/os-release ]; then
  # shellcheck disable=SC1091
  pretty=$(. /etc/os-release; echo "${PRETTY_NAME:-$ID}")
  for id in $(. /etc/os-release; echo "${ID:-} ${ID_LIKE:-}"); do
    case $id in
      arch|cachyos|manjaro|endeavouros|garuda) fam=arch; break ;;
      debian|ubuntu) fam=debian; break ;;
      fedora) fam=fedora; break ;;
      suse|opensuse|opensuse-*|sles) fam=suse; break ;;
    esac
  done
fi
ok "$pretty ($fam family)"

# ---------- dependency probe ----------
# Each need: a probe and its package names per family (empty = not packaged / part of the base system).
have_lib() { local l; for l in "$@"; do ldconfig -p 2>/dev/null | grep -q " $l " && return 0
  for d in /usr/lib /usr/lib64 /usr/lib/x86_64-linux-gnu /lib/x86_64-linux-gnu /lib64; do [ -e "$d/$l" ] && return 0; done; done; return 1; }
have_file() { local f; for f in "$@"; do [ -e "$f" ] && return 0; done; return 1; }
gst_dirs="/usr/lib/gstreamer-1.0 /usr/lib64/gstreamer-1.0 /usr/lib/x86_64-linux-gnu/gstreamer-1.0"
vlc_aout_dirs="/usr/lib/vlc/plugins/audio_output /usr/lib64/vlc/plugins/audio_output /usr/lib/x86_64-linux-gnu/vlc/plugins/audio_output"
probe() {
  case $1 in
    web)    have_lib libwebkit2gtk-4.1.so.0 libwebkit2gtk-4.0.so.37 ;;
    websnd) local d; for d in $gst_dirs; do [ -e "$d/libgstautodetect.so" ] && return 0; done; return 1 ;;
    # libvlc alone is not enough: Fedora's vlc-libs has no plugins, and the --aout fix wants the pulse output.
    audio)  have_lib libvlc.so.5 && { local d; for d in $vlc_aout_dirs; do [ -e "$d/libpulse_plugin.so" ] && return 0; done; return 1; } ;;
    pactl)  command -v pactl >/dev/null ;;
    secret) have_lib libsecret-1.so.0 ;;
    x11)    have_lib libX11.so.6 && have_lib libXi.so.6 && have_lib libICE.so.6 && have_lib libSM.so.6 ;;
    # The app pins the X11 backend (overlays are override-redirect windows), so a Wayland session
    # shows it through XWayland. Only require it when installing from inside a Wayland session.
    xwl)    [ -z "${WAYLAND_DISPLAY:-}" ] || { local f; for f in /usr/bin/Xwayland /usr/lib/xorg/Xwayland /usr/libexec/Xwayland; do [ -e "$f" ] && return 0; done; return 1; } ;;
    tls)    have_lib libssl.so.3 libssl.so.1.1 ;;
  esac
}
label() { case $1 in web) echo "web views (WebKitGTK 4.1)";; websnd) echo "web page sound (GStreamer good plugins)";;
  audio) echo "audio and video (VLC + pulse output)";; pactl) echo "ducking and microphone list (pactl)";;
  secret) echo "sign-in token store (libsecret)";; x11) echo "overlays and panic key (X11, XInput2)";;
  xwl) echo "XWayland (the GUI is X11; a Wayland session shows it through XWayland)";;
  tls) echo "HTTPS (OpenSSL 3)";; esac; }
pkgs() {  # pkgs need family
  case $2:$1 in
    arch:web) echo webkit2gtk-4.1 ;;            debian:web) echo libwebkit2gtk-4.1-0 ;;
    fedora:web) echo webkit2gtk4.1 ;;           suse:web) echo libwebkit2gtk-4_1-0 ;;
    arch:websnd) echo gst-plugins-good ;;       debian:websnd) echo gstreamer1.0-plugins-good ;;
    fedora:websnd) echo gstreamer1-plugins-good ;; suse:websnd) echo gstreamer-plugins-good ;;
    arch:audio) echo vlc ;;                     debian:audio) echo libvlc5 vlc-plugin-base ;;
    fedora:audio) echo vlc-libs vlc-plugins-base vlc-plugin-ffmpeg vlc-plugin-pulseaudio ;;
    suse:audio) echo libvlc5 vlc-noX vlc-plugins-desktop ;;   # vlc-noX alone has no pulse output on Tumbleweed
    arch:pactl) echo libpulse ;;                *:pactl) echo pulseaudio-utils ;;
    arch:secret|fedora:secret) echo libsecret ;; debian:secret|suse:secret) echo libsecret-1-0 ;;
    arch:x11) echo libx11 libxi libice libsm ;;  debian:x11) echo libx11-6 libxi6 libice6 libsm6 ;;
    fedora:x11) echo libX11 libXi libICE libSM ;; suse:x11) echo libX11-6 libXi6 libICE6 libSM6 ;;  # ICE/SM: Avalonia X11 session mgmt; absent on minimal SUSE, crashes startup
    arch:xwl) echo xorg-xwayland ;;             debian:xwl) echo xwayland ;;   # apt has a dedicated xwayland pkg since 22.04
    fedora:xwl) echo xorg-x11-server-Xwayland ;; suse:xwl) echo xwayland ;;   # TW renamed the Xwayland package
    # Fedora 45+ ships OpenSSL 4 (libssl.so.4); .NET 10 needs the libssl.so.3 compat package.
    arch:tls) echo openssl ;;                   debian:tls) echo libssl3 ;;
    fedora:tls) echo openssl3-libs ;;           suse:tls) echo libopenssl3 ;;
  esac
}
NEEDS="web websnd audio pactl secret x11 xwl tls"
missing() { local n out=; for n in $NEEDS; do probe $n || out+="$n "; done; echo "$out"; }

install_deps() {
  local miss list n
  miss=$(missing)
  if [ -z "$miss" ]; then ok "all system libraries are present"; return 0; fi
  for n in $miss; do warn "missing: $(label $n)"; done
  if [ $fam = unknown ]; then
    warn "unknown distribution: install the packages that provide the items above with your package manager"
    return 0
  fi
  list=; for n in $miss; do list+="$(pkgs $n $fam) "; done; list=${list% }
  local cmd
  case $fam in
    arch)   cmd="pacman -S --needed $list" ;;
    debian) cmd="apt-get install $list" ;;
    fedora) cmd="dnf install $list" ;;
    suse)   cmd="zypper install $list" ;;
  esac
  if [ $deps = 0 ]; then say "  Install them with: sudo $cmd"; return 0; fi
  if [ "$(id -u)" -ne 0 ] && [ -z "$SUDO" ]; then say "  No sudo/doas here. As root, run: $cmd"; return 0; fi
  ask "Install with: ${SUDO:+$SUDO }$cmd ?" || { say "  Skipped. Install them later with: sudo $cmd"; return 0; }
  local y=; [ $yes = 1 ] || [ -z "$tty" ] && y=1
  # shellcheck disable=SC2086
  local skip=--setopt=strict=0; command -v dnf5 >/dev/null && skip=--skip-unavailable   # one unknown name must not sink the rest
  case $fam in
    arch)   as_root pacman -S --needed ${y:+--noconfirm} $list ;;
    debian) if [ -n "$y" ]; then as_root env DEBIAN_FRONTEND=noninteractive sh -c "apt-get update -qq && apt-get install -y $list"
            else as_root sh -c "apt-get update -qq && apt-get install $list"; fi ;;
    fedora) as_root dnf install ${y:+-y} $skip $list ;;
    suse)   as_root zypper ${y:+-n} install $list ;;
  esac || warn "the package manager reported an error (checked again below)"
  miss=$(missing)
  if [ -z "$miss" ]; then ok "all system libraries are present"
  else for n in $miss; do warn "still missing: $(label $n) - CCP starts, but this part stays off"; done; fi
}

step "System libraries"
install_deps
[ $deps_only = 1 ] && return 0

# ---------- find the build ----------
cache=${XDG_CACHE_HOME:-$HOME/.cache}/ccp-install
mkdir -p "$cache"
sha=
if [ -n "$tarball" ]; then
  [ -f "$tarball" ] || die "no such file: $tarball"
  tarball=$(cd "$(dirname "$tarball")" && pwd)/$(basename "$tarball")
  [ -f "$tarball.sha256" ] && sha=$(awk '{print $1; exit}' "$tarball.sha256")
else
  step "Finding the newest Linux build"
  api=https://api.github.com/repos/$REPO/releases
  if [ -n "$release" ]; then json=$(fetch "$api/tags/$release") || die "release $release not found"
  else json=$(fetch "$api?per_page=40") || die "could not reach GitHub ($api)"; fi
  # Releases come newest first; the Linux builds are pre-releases, so /releases/latest (Windows) is not used.
  url=$(printf '%s' "$json" | grep -o '"browser_download_url": *"[^"]*-linux-x64\.tar\.gz"' | head -1 | sed 's/.*"\(https[^"]*\)"/\1/')
  [ -n "$url" ] || die "no Linux build found${release:+ in release $release}"
  ok "$(basename "$url")"
  sha=$(fetch "$url.sha256" 2>/dev/null | awk '{print $1; exit}') || sha=
  tarball=$cache/$(basename "$url")
  if [ -f "$tarball" ] && [ -n "$sha" ] && echo "$sha  $tarball" | sha256sum -c --status; then
    ok "already downloaded"
  else
    step "Downloading ($(basename "$url"))"
    fetch "$url" "$tarball.part" && mv "$tarball.part" "$tarball" || die "download failed"
  fi
fi
ver=$(basename "$tarball" | sed -n 's/^ConditioningControlPanel-\(.*\)-linux-x64\.tar\.gz$/\1/p'); ver=${ver:-unknown}
if [ -n "$sha" ]; then
  echo "$sha  $tarball" | sha256sum -c --status || die "checksum mismatch for $(basename "$tarball") - delete it and run again"
  ok "sha256 verified"
else
  warn "no .sha256 next to the tarball: not verified"
fi

# ---------- install ----------
old=; [ -f "$APP/.ccp-version" ] && old=$(cat "$APP/.ccp-version")
if [ -n "$old" ]; then step "Updating $old -> $ver"; else step "Installing $ver into $APP"; fi
if app_running; then
  ask "CCP is running. Close it now so it can be updated?" || die "close CCP and run the installer again"
  pkill -f "^$APP/CCP.Avalonia" || true
  for _ in $(seq 20); do app_running || break; sleep 0.5; done
  app_running && die "CCP did not close"
fi
# KiB: uncompressed size (gzip -l wraps at 4 GiB, so at least 3x the archive) plus slack.
need=$(gzip -l "$tarball" | awk 'END{print int($2/1024)}'); comp=$(( $(stat -c %s "$tarball") * 3 / 1024 ))
[ "$need" -ge "$comp" ] || need=$comp; need=$((need + 200000))
T mkdir -p "$OPT" "$BIN"
avail=$(df -Pk "$OPT" | awk 'END{print $4}')
[ "$avail" -ge "$need" ] || die "not enough space in $OPT: need $((need/1024)) MiB, have $((avail/1024)) MiB"

# Extract next to the old copy and swap, so a failed extract leaves the working install alone.
T rm -rf "$APP.new" "$APP.old"
T mkdir -p "$APP.new"
T tar -xzf "$tarball" -C "$APP.new" --strip-components=1 --no-same-owner || { T rm -rf "$APP.new"; die "extract failed"; }
[ -x "$APP.new/CCP.Avalonia" ] || { T rm -rf "$APP.new"; die "the tarball has no CCP.Avalonia"; }
echo "$ver" | T tee "$APP.new/.ccp-version" >/dev/null
[ -e "$APP" ] && T mv "$APP" "$APP.old"
T mv "$APP.new" "$APP"
T rm -rf "$APP.old"
ok "app files"

# Newer tarballs carry their own launcher (it names the missing OpenSSL 3 package instead of a bare abort).
target=CCP.Avalonia; [ -x "$APP/conditioning-control-panel.sh" ] && target=conditioning-control-panel.sh
printf '#!/bin/sh\nexec "%s/%s" "$@"\n' "$APP" "$target" | T tee "$LAUNCHER" >/dev/null
T chmod 755 "$LAUNCHER"
ok "launcher $LAUNCHER"

# Desktop entry: Exec points at the launcher's absolute path (~/.local/bin is often not on the session PATH).
# Released tarballs were built on Windows runners, so the .desktop/metainfo arrive with CRLF endings -
# desktop-file-validate rejects them and some environments then hide the menu entry. Normalize on install.
T mkdir -p "$SHARE/applications" "$SHARE/metainfo"
# Quote the executable for desktop-entry syntax and escape sed replacement characters.
exec_path=${LAUNCHER//\\/\\\\}
exec_path=${exec_path//\"/\\\"}
exec_path=${exec_path//\$/\\\$}
exec_path=${exec_path//\`/\\\`}
exec_path=${exec_path//%/%%}
exec_path=${exec_path//&/\\&}
exec_path=${exec_path//|/\\|}
for entry in "" .Player .Editor; do
  source="$APP/share/applications/$APP_ID$entry.desktop"
  [ -f "$source" ] || { [ -n "$entry" ] && continue; die "missing desktop entry: $source"; } # Older tarballs lack the handlers.
  args=
  case $entry in .Player) args=' --play %f';; .Editor) args=' --edit %f';; esac
  sed -e 's/\r$//' -e "s|^Exec=.*|Exec=\"$exec_path\"$args|" "$source" | T tee "$SHARE/applications/$APP_ID$entry.desktop" >/dev/null
done
sed 's/\r$//' "$APP/share/metainfo/$APP_ID.metainfo.xml" | T tee "$SHARE/metainfo/$APP_ID.metainfo.xml" >/dev/null
for icon in "$APP"/share/icons/hicolor/*/apps/$APP_ID.png; do
  rel=${icon#"$APP/share/"}; T mkdir -p "$SHARE/$(dirname "$rel")"; T cp "$icon" "$SHARE/$rel"
done
refresh_desktop
ok "menu entry and icons"

# A broken or missing menu entry is invisible to every CLI test, yet leaves the app unlaunchable
# from the desktop - validate it with the same tool the desktop environment trusts.
if command -v desktop-file-validate >/dev/null; then
  if desktop-file-validate "$SHARE/applications/$APP_ID.desktop" >"$cache/desktop-validate.log" 2>&1; then
    ok "menu entry passes desktop-file-validate"
  else
    warn "menu entry problems (desktop-file-validate):"; sed 's/^/    /' "$cache/desktop-validate.log" >&2
  fi
fi

# ---------- self-check ----------
if [ $check = 1 ]; then
  # Run through the installed launcher: the self-check must prove the same file the menu entry runs.
  step "Self-check (headless, throwaway profile)"
  sb=$(mktemp -d); log=$cache/smoke-$ver.log
  if CCP_USERDATA_DIR=$sb/userdata XDG_CONFIG_HOME=$sb/config HOME=$sb timeout 300 "$LAUNCHER" --smoke >"$log" 2>&1 </dev/null; then
    ok "passed"
  else
    warn "self-check failed (log: $log). CCP may still start; the log shows which part failed."
    grep -iE 'fail|error' "$log" | head -5 | sed 's/^/    /' >&2 || true
  fi

  # GUI start-up test: the desktop experience itself - launcher -> Avalonia -> a mapped window -
  # on a private Xvfb display, never on the user's screen. The headless check above loads zero
  # X11/Wayland libraries, which is exactly how "missing libX11 crashes the GUI" survived every
  # CLI test before. WAYLAND_DISPLAY is dropped so this can never attach to the user's own
  # compositor; on Wayland systems it proves the X11/XWayland path, and the Wayland-client path
  # has no display server an installer could run per-user, so the distro test harness covers it
  # with a real compositor (weston headless).
  if command -v Xvfb >/dev/null; then
    step "GUI start-up test (private Xvfb display, throwaway profile)"
    gl=$cache/gui-$ver.log; sb2=$(mktemp -d)
    # Software rendering keeps the WebKit web view off a /dev/dri that VMs and VNC lack; a blank
    # web page is still proof the GUI came up.
    # openSUSE ships Xvfb but not Debian's xvfb-run wrapper; start the server ourselves then.
    # One retry: the WebKitGTK software path is CPU-hungry, and a machine busy elsewhere can
    # starve it into a native abort (observed under artificial load); a clean second attempt
    # must not fail the install or scare the user.
    guiok=0
    for try in 1 2; do
      [ $try = 2 ] && { echo "    retrying once (a busy machine can starve the first attempt)"; sleep 10; }
      xpid=
      if command -v xvfb-run >/dev/null; then
        pre=(xvfb-run -a)
      else
        for n in $(seq 42 64); do [ ! -e "/tmp/.X11-unix/X$n" ] && break; done
        Xvfb ":$n" -screen 0 1280x800x24 >"$cache/xvfb-$ver.log" 2>&1 & xpid=$!
        sleep 2
        pre=(env "DISPLAY=:$n")
      fi
      rc=0
      CCP_USERDATA_DIR=$sb2/userdata XDG_CONFIG_HOME=$sb2/config HOME=$sb2 LIBGL_ALWAYS_SOFTWARE=1 \
        WEBKIT_DISABLE_DMABUF_RENDERER=1 WEBKIT_DISABLE_COMPOSITING_MODE=1 \
        timeout 150 "${pre[@]}" env -u WAYLAND_DISPLAY -u GDK_BACKEND sh -c '
          "$0" >"$1" 2>&1 & ap=$!
          win=; early=; i=0
          for i in $(seq 1 90); do
            kill -0 $ap 2>/dev/null || { early=$(wait $ap 2>/dev/null; echo $?); break; }
            win=$(xdotool search --name "Conditioning Control Panel" 2>/dev/null | head -1)
            [ -n "$win" ] && break
            if ! command -v xdotool >/dev/null; then [ $i -ge 25 ] && break; fi
            sleep 1
          done
          if [ -n "$early" ]; then echo "app exited after ${i}s (rc=$early)" >>"$1"; exit 3; fi
          nm=$(xdotool getwindowname "$win" 2>/dev/null)
          kill $ap 2>/dev/null; wait $ap 2>/dev/null
          if [ -n "$win" ]; then echo "window mapped: $nm" >>"$1"; exit 0; fi
          if command -v xdotool >/dev/null; then
            for w in $(xdotool search --name "" 2>/dev/null | head -8); do
              echo "seen window: $(xdotool getwindowname "$w" 2>/dev/null)" >>"$1"
            done
            echo "no matching window within ${i}s" >>"$1"; exit 4
          fi
          echo "alive after ${i}s (xdotool absent, no window assert)" >>"$1"; exit 0
        ' "$LAUNCHER" "$gl" || rc=$?
      [ -n "$xpid" ] && { kill "$xpid" 2>/dev/null || true; }
      # xvfb-run sometimes loses its X-server teardown race and returns 1 after the child exited
      # 0; the log is the truth: the mapped window (or, with no xdotool, the app staying up).
      if [ $rc = 0 ] || grep -qE "^(window mapped|alive after)" "$gl" 2>/dev/null; then
        guiok=1; break
      fi
    done
    if [ $guiok = 1 ]; then
      ok "GUI came up on a virtual display"
      grep -E "^(window mapped|alive after)" "$gl" | sed 's/^/    /' || true
    else
      warn "GUI start-up test failed (log: $gl) - a missing display library is a common cause"
      tail -14 "$gl" | sed 's/^/    /' >&2 || true
    fi
    rm -rf "$sb2"
  else
    say "  (tip: install xvfb + xdotool and run this installer again to also test the real GUI start-up; the headless check above loads no X11/Wayland libraries)"
  fi
  rm -rf "$sb"
fi

# ---------- done ----------
say ""
say "${G}${B}Conditioning Control Panel $ver is installed.${N}"
say "  Start it from your app menu, or run: $LAUNCHER"
case ":$PATH:" in *":$BIN:"*) ;; *) say "  ($BIN is not on your PATH; the menu entry works anyway.)" ;; esac
say "  Update: run this installer again.  Remove: add --uninstall."
[ -x "$HOME/ConditioningControlPanel/CCP.Avalonia" ] && [ "$APP" != "$HOME/ConditioningControlPanel" ] && \
  say "  An older hand-extracted copy is in ~/ConditioningControlPanel; you can delete it (your data is stored elsewhere)."
return 0
}

main "$@"
