#!/usr/bin/env bash
# First-race host prerequisite, after checksum verification and extraction.
# stdout: zero or one Electron argument. stderr: the sandbox decision (no paths/tokens).
# Call once per engine launch, before the panic-owned race process starts.
set -euo pipefail
engine=${1:?usage: rt-engine-check.sh engine-directory}
for notice in LICENSE LICENSES.chromium.html; do
  if [[ ! -s "$engine/$notice" || -L "$engine/$notice" ]]; then
    echo "race-engine: missing licence $notice" >&2
    exit 1
  fi
done
# Never install chrome-sandbox setuid in the user cache. Flatpak forbids user namespaces.
# This capability probe does not prove Electron passes a distro's per-binary AppArmor policy.
if [[ -n "${FLATPAK_ID:-}" || -f /.flatpak-info ]]; then
  reason=flatpak
elif timeout --kill-after=1s 3s unshare --user --map-root-user --pid --fork --net --ipc true >/dev/null 2>&1; then
  echo 'race-engine: sandbox=enabled reason=user-namespaces' >&2
  exit 0
else
  reason=user-namespaces-unavailable
fi
echo "race-engine: sandbox=disabled reason=$reason" >&2
printf '%s\n' --no-sandbox
