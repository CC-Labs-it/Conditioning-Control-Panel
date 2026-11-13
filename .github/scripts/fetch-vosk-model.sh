#!/usr/bin/env bash
# Fetch the small English Vosk model the speech tests run against (not in the repo, ~40 MB).
# Prints the model directory; point CCP_VOSK_MODEL at it. Idempotent.
set -euo pipefail
name=vosk-model-small-en-us-0.15
# Pinned: a changed upstream zip fails here instead of shipping unreviewed bytes. CI cache keys include it.
sha256=30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498
dir="${1:-$HOME/.cache/ccp-test/vosk}"
if [ ! -d "$dir/$name/am" ]; then
  mkdir -p "$dir"
  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  # Keep partial bytes between attempts: restarting the 40 MB transfer after every timeout
  # can exhaust all retries even when nearly the entire model arrived each time.
  downloaded=false
  for attempt in {1..8}; do
    if curl -fsSL --connect-timeout 30 --max-time 180 -C - -o "$tmp/m.zip" "https://alphacephei.com/vosk/models/$name.zip"; then
      downloaded=true
      break
    else
      status=$?
    fi
    # A timeout can occur after the last byte arrived. Only the pinned hash can accept it.
    if [ -f "$tmp/m.zip" ] && echo "$sha256  $tmp/m.zip" | sha256sum -c --quiet - 2>/dev/null; then
      downloaded=true
      break
    fi
    # Some servers do not support ranges. Start again if curl cannot resume.
    if [ "$status" -eq 33 ]; then rm -f "$tmp/m.zip"; fi
    if [ "$attempt" -lt 8 ]; then sleep 5; fi
  done
  if [ "$downloaded" != true ]; then
    echo "Vosk model download failed after 8 attempts (curl exit $status)" >&2
    exit "$status"
  fi
  echo "$sha256  $tmp/m.zip" | sha256sum -c --quiet -
  unzip -q "$tmp/m.zip" -d "$dir"
fi
echo "$dir/$name"
