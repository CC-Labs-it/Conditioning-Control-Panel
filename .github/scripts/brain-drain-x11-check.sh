#!/usr/bin/env bash
# Isolated native capture proof. Requires Xorg + dummy driver + xcompmgr and a built test assembly.
set -euo pipefail
cd "$(dirname "$0")/../.."
if [[ -e /tmp/.X99-lock || -S /tmp/.X11-unix/X99 ]]; then
  echo 'Refusing to use an existing :99 desktop.' >&2
  exit 1
fi
command -v Xorg >/dev/null
command -v xcompmgr >/dev/null
work=$(mktemp -d)
xpid= cpid=
trap '[[ -z "$cpid" ]] || kill "$cpid" 2>/dev/null || true; [[ -z "$xpid" ]] || kill "$xpid" 2>/dev/null || true; rm -rf "$work"' EXIT
cat > "$work/xorg.conf" <<'CONF'
Section "Device"
 Identifier "dummy"
 Driver "dummy"
 VideoRam 256000
EndSection
Section "Monitor"
 Identifier "monitor"
 HorizSync 28-80
 VertRefresh 48-75
 Modeline "800x600" 40 800 840 968 1056 600 601 605 628
EndSection
Section "Screen"
 Identifier "screen"
 Device "dummy"
 Monitor "monitor"
 DefaultDepth 24
 SubSection "Display"
  Depth 24
  Modes "800x600"
 EndSubSection
EndSection
Section "ServerFlags"
 Option "AutoAddDevices" "false"
 Option "DontVTSwitch" "true"
EndSection
CONF
Xorg :99 -config "$work/xorg.conf" -logfile "$work/Xorg.log" -noreset -nolisten tcp > "$work/server.log" 2>&1 &
xpid=$!
export DISPLAY=:99 CCP_BRAIN_X11_TEST=1 DiffEngine_Disabled=true
# Startup only, never a production capture wait. XOpenDisplay is scoped to our fresh server.
for attempt in {1..10}; do
  kill -0 "$xpid"
  if python3 -c 'import ctypes; x=ctypes.CDLL("libX11.so.6"); x.XOpenDisplay.restype=ctypes.c_void_p; d=x.XOpenDisplay(None); exit(0 if d else 1)'; then break; fi
  sleep 1
done
xcompmgr -n > "$work/compositor.log" 2>&1 &
cpid=$!
sleep 1
kill -0 "$cpid"
dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build \
  --filter FullyQualifiedName~BrainDrainX11Tests --logger "console;verbosity=detailed"
