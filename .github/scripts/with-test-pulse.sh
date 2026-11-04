#!/usr/bin/env bash
# Run a command against private silent sinks. Requires pulseaudio and pactl.
# Example: bash .github/scripts/with-test-pulse.sh dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~FirstShowPreviewBackendTests
set -euo pipefail
if [ "$#" -eq 0 ]; then echo 'Pass the test command as arguments' >&2; exit 2; fi
runtime=$(mktemp -d)
pid=
cleanup() {
  if [ -n "$pid" ]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  rm -rf "$runtime"
}
trap cleanup EXIT
export XDG_RUNTIME_DIR="$runtime" XDG_CONFIG_HOME="$runtime/config" XDG_STATE_HOME="$runtime/state"
export XDG_CACHE_HOME="$runtime/cache" PULSE_RUNTIME_PATH="$runtime/pulse" PULSE_SERVER="unix:$runtime/native"
mkdir -m 700 "$PULSE_RUNTIME_PATH"
pulseaudio -n --daemonize=no --exit-idle-time=-1 --log-target=stderr \
  --load="module-native-protocol-unix socket=$runtime/native auth-anonymous=1" \
  --load='module-null-sink sink_name=ccp_default' \
  --load='module-null-sink sink_name=ccp_headphones' &
pid=$!
for _ in {1..100}; do
  if pactl info >/dev/null 2>&1; then break; fi
  if ! kill -0 "$pid" 2>/dev/null; then echo 'Private PulseAudio server exited' >&2; exit 1; fi
  sleep .1
done
pactl set-default-sink ccp_default
export CCP_TEST_AUDIO_FIXTURE=1
"$@"
