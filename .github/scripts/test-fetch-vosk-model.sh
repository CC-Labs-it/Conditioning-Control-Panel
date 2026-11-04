#!/usr/bin/env bash
# Exercise interrupted transfers without depending on the external model host.
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin"
printf 'complete archive' > "$tmp/fixture"
export TEST_FETCH_TMP="$tmp" PATH="$tmp/bin:$PATH"
cat > "$tmp/bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
count=0
[ ! -f "$TEST_FETCH_TMP/count" ] || count=$(cat "$TEST_FETCH_TMP/count")
count=$((count + 1)); echo "$count" > "$TEST_FETCH_TMP/count"
args=("$@"); out=''; resume=0
for ((i=0; i<${#args[@]}; i++)); do
  [ "${args[i]}" != -o ] || out=${args[i+1]}
  [ "${args[i]}" != --continue-at ] || { [ "${args[i+1]}" = - ]; resume=1; }
done
[ "$resume" = 1 ] && [ -n "$out" ] || exit 99
case "$TEST_FETCH_CASE:$count" in
  partial:1|no_range:1) printf partial > "$out"; exit 28 ;;
  complete_on_timeout:1) cp "$TEST_FETCH_TMP/fixture" "$out"; exit 28 ;;
  partial:2) [ "$(cat "$out")" = partial ] || exit 98; cp "$TEST_FETCH_TMP/fixture" "$out" ;;
  no_range:2) [ "$(cat "$out")" = partial ] || exit 98; exit 33 ;;
  no_range:3) [ ! -e "$out" ] || exit 97; cp "$TEST_FETCH_TMP/fixture" "$out" ;;
  corrupt:1) printf corrupt > "$out" ;;
  exhausted:*) printf partial > "$out"; exit 28 ;;
  *) exit 96 ;;
esac
MOCK
cat > "$tmp/bin/sha256sum" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
read -r hash path
[ "$hash" = 30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498 ]
cmp -s "$path" "$TEST_FETCH_TMP/fixture"
MOCK
cat > "$tmp/bin/unzip" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail
[ "$1" = -q ] && [ "$3" = -d ]
mkdir -p "$4/vosk-model-small-en-us-0.15/am"
MOCK
cat > "$tmp/bin/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK
chmod +x "$tmp/bin/"*
for scenario in partial no_range complete_on_timeout; do
  export TEST_FETCH_CASE=$scenario
  rm -f "$tmp/count"
  bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/$scenario" > /dev/null
  [ -d "$tmp/$scenario/vosk-model-small-en-us-0.15/am" ]
  expected=2
  [ "$scenario" != no_range ] || expected=3
  [ "$scenario" != complete_on_timeout ] || expected=1
  [ "$(cat "$tmp/count")" -eq "$expected" ]
  bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/$scenario" > /dev/null
  [ "$(cat "$tmp/count")" -eq "$expected" ] # cached model never downloads again
done
for scenario in corrupt exhausted; do
  export TEST_FETCH_CASE=$scenario
  rm -f "$tmp/count"
  if bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/$scenario" > /dev/null 2>&1; then
    echo "unexpected success: $scenario" >&2; exit 1
  fi
  [ ! -d "$tmp/$scenario/vosk-model-small-en-us-0.15/am" ]
  if [ "$scenario" = exhausted ]; then [ "$(cat "$tmp/count")" -eq 4 ]; fi
done
echo 'Vosk downloader retry and integrity checks passed.'
