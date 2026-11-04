#!/usr/bin/env bash
# Exercise retries and checksum failure without downloading the model.
set -euo pipefail
root=$(cd "$(dirname "$0")/../../.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir "$tmp/bin"
cat > "$tmp/bin/curl" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
out=
resume=false
while [ "$#" -gt 0 ]; do
  case "$1" in
    --continue-at) [ "$2" = - ]; resume=true; shift 2 ;;
    --max-time|-o) if [ "$1" = -o ]; then out=$2; fi; shift 2 ;;
    *) shift ;;
  esac
done
[ "$resume" = true ] || exit 9
[ -n "$out" ] || exit 9
count=0
[ ! -f "$MOCK_COUNT" ] || count=$(cat "$MOCK_COUNT")
count=$((count+1)); echo "$count" > "$MOCK_COUNT"
if [ "$MOCK_CASE" = exhausted ]; then printf 'partial' > "$out"; exit 28; fi
if [ "$count" -eq 1 ]; then printf 'partial' > "$out"; exit 28; fi
if [ "$MOCK_CASE" = unsupported ] && [ "$count" -eq 2 ]; then
  [ "$(cat "$out")" = partial ]; exit 33
fi
if [ "$MOCK_CASE" = unsupported ]; then [ ! -e "$out" ]; else [ "$(cat "$out")" = partial ]; fi
if [ "$MOCK_CASE" = corrupt ]; then printf 'corrupt' > "$out"
elif [ "$MOCK_CASE" = unsupported ]; then printf 'partialcomplete' > "$out"
else printf 'complete' >> "$out"; fi
STUB
cat > "$tmp/bin/sha256sum" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
read -r hash path
[ "$hash" = 30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498 ]
[ "$(cat "$path")" = partialcomplete ]
STUB
cat > "$tmp/bin/unzip" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
mkdir -p "$4/vosk-model-small-en-us-0.15/am"
touch "$MOCK_UNZIP"
STUB
printf '#!/usr/bin/env bash\nexit 0\n' > "$tmp/bin/sleep"
chmod +x "$tmp/bin/"*
export PATH="$tmp/bin:$PATH" MOCK_COUNT="$tmp/count" MOCK_UNZIP="$tmp/unzipped"
for MOCK_CASE in resume unsupported; do
  export MOCK_CASE
  rm -f "$MOCK_COUNT" "$MOCK_UNZIP"
  bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/$MOCK_CASE" > /dev/null
  [ -f "$MOCK_UNZIP" ]
  bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/$MOCK_CASE" > /dev/null
  if [ "$MOCK_CASE" = resume ]; then [ "$(cat "$MOCK_COUNT")" = 2 ]; else [ "$(cat "$MOCK_COUNT")" = 3 ]; fi
done
export MOCK_CASE=corrupt
rm -f "$MOCK_COUNT" "$MOCK_UNZIP"
if bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/corrupt" > /dev/null 2>&1; then
  echo 'corrupt download was accepted' >&2; exit 1
fi
[ ! -e "$MOCK_UNZIP" ]
export MOCK_CASE=exhausted
rm -f "$MOCK_COUNT" "$MOCK_UNZIP"
if bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/exhausted" > /dev/null 2>&1; then
  echo 'exhausted retries were accepted' >&2; exit 1
fi
[ "$(cat "$MOCK_COUNT")" = 4 ] && [ ! -e "$MOCK_UNZIP" ]
echo 'PASS: resume, no-range restart, cached model, checksum and exhausted retries'
