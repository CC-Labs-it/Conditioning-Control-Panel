#!/usr/bin/env bash
set -euo pipefail
# Run with pulseaudio and pactl installed. The parent server must remain untouched.
original_server=${PULSE_SERVER-}
marker=$(mktemp)
trap 'rm -f "$marker"' EXIT
bash .github/scripts/with-test-pulse.sh bash -c '
  set -euo pipefail
  test "$CCP_TEST_AUDIO_FIXTURE" = 1
  printf "%s" "$PULSE_SERVER" > "$1"
  pactl list short sinks | grep -q ccp_default
  pactl list short sinks | grep -q ccp_headphones
  test "$(pactl get-default-sink)" = ccp_default
' _ "$marker"
socket=$(cat "$marker")
test ! -e "${socket#unix:}"
set +e
bash .github/scripts/with-test-pulse.sh bash -c 'printf "%s" "$PULSE_SERVER" > "$1"; exit 23' _ "$marker"
result=$?
set -e
test "$result" = 23
test ! -e "$(sed 's/^unix://' "$marker")"
test "${PULSE_SERVER-}" = "$original_server"
echo 'Pulse fixture isolation passed'
