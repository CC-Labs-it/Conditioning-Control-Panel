#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../../.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin"
export VOSK_TEST_DIR="$tmp"
python3 - "$tmp/model.zip" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1], 'w') as z:
    z.writestr('vosk-model-small-en-us-0.15/am/final.mdl', 'test model')
PY
cat > "$tmp/bin/curl" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' call >> "$VOSK_TEST_DIR/calls"
out= resume=0
while [ "$#" -gt 0 ]; do
  case "$1" in
    -o) out=$2; shift 2 ;;
    -C|--continue-at) [ "$2" = - ] && resume=1; shift 2 ;;
    *) shift ;;
  esac
done
if [ "${VOSK_TEST_FAIL:-0}" = 1 ]; then
  printf partial > "$out"
  exit 28
fi
if [ "$(wc -l < "$VOSK_TEST_DIR/calls")" -eq 1 ]; then
  head -c 30 "$VOSK_TEST_DIR/model.zip" > "$out"
  exit 28
fi
[ "$resume" -eq 1 ] || exit 1
if [ "${VOSK_TEST_NORANGE:-0}" = 1 ]; then
  if [ "$(wc -l < "$VOSK_TEST_DIR/calls")" -eq 2 ]; then exit 33; fi
  [ ! -e "$out" ] || exit 1
  cp "$VOSK_TEST_DIR/model.zip" "$out"
  exit 0
fi
[ "$(wc -c < "$out")" -eq 30 ] || exit 1
if [ "${VOSK_TEST_CORRUPT:-0}" = 1 ]; then printf corrupt >> "$out"; else tail -c +31 "$VOSK_TEST_DIR/model.zip" >> "$out"; fi
SH
cat > "$tmp/bin/sha256sum" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
[ "$*" = '-c --quiet -' ]
read -r expected file
[ "$expected" = 30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498 ]
cmp -s "$file" "$VOSK_TEST_DIR/model.zip"
SH
cat > "$tmp/bin/sleep" <<'SH'
#!/usr/bin/env bash
exit 0
SH
chmod +x "$tmp/bin/"*
export PATH="$tmp/bin:$PATH"
bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/good" >/dev/null
[ "$(wc -l < "$tmp/calls")" -eq 2 ]
[ -f "$tmp/good/vosk-model-small-en-us-0.15/am/final.mdl" ]
bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/good" >/dev/null
[ "$(wc -l < "$tmp/calls")" -eq 2 ]
rm "$tmp/calls"
VOSK_TEST_NORANGE=1 bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/no-range" >/dev/null
[ "$(wc -l < "$tmp/calls")" -eq 3 ]
[ -f "$tmp/no-range/vosk-model-small-en-us-0.15/am/final.mdl" ]
rm "$tmp/calls"
if VOSK_TEST_CORRUPT=1 bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/bad" >/dev/null 2>&1; then
  echo 'corrupt download was accepted' >&2; exit 1
fi
[ ! -d "$tmp/bad/vosk-model-small-en-us-0.15/am" ]
rm "$tmp/calls"
if VOSK_TEST_FAIL=1 bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/failed" >/dev/null 2>&1; then
  echo 'failed download was accepted' >&2; exit 1
fi
[ "$(wc -l < "$tmp/calls")" -eq 8 ]
[ ! -d "$tmp/failed/vosk-model-small-en-us-0.15/am" ]
echo 'Vosk download resume, no-range fallback, checksum rejection, retry limit and cache reuse passed.'
