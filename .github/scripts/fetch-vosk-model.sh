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
  # Keep partial bytes across retries. A slow upstream should not restart a 40 MB transfer.
  for attempt in 1 2 3 4 5; do
    if curl -fsSL --continue-at - --max-time 300 -o "$tmp/m.zip" "https://alphacephei.com/vosk/models/$name.zip"; then
      break
    else
      code=$?
      # A server that rejects ranges needs a clean download on the next attempt.
      if [ "$code" -eq 33 ]; then rm -f "$tmp/m.zip"; fi
      if [ "$attempt" -eq 5 ]; then exit "$code"; fi
      sleep 5
    fi
  done
  echo "$sha256  $tmp/m.zip" | sha256sum -c --quiet -
  unzip -q "$tmp/m.zip" -d "$dir"
fi
echo "$dir/$name"
