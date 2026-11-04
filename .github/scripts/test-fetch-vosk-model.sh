#!/usr/bin/env bash
# Exercise interrupted downloads without a network or an actual speech model.
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/bin" "$tmp/fixture/vosk-model-small-en-us-0.15/am"
printf model > "$tmp/fixture/vosk-model-small-en-us-0.15/am/final.mdl"
(cd "$tmp/fixture" && zip -qr "$tmp/model.zip" vosk-model-small-en-us-0.15)
export CCP_VOSK_TEST_FIXTURE="$tmp/model.zip"
cat > "$tmp/bin/curl" <<'MOCK'
#!/usr/bin/env bash
set -eu
out=; resume=0
while [ "$#" -gt 0 ]; do
  case "$1" in
    -o) out=$2; shift 2;;
    --continue-at) [ "$2" = - ]; resume=1; shift 2;;
    *) shift;;
  esac
done
[ "$resume" = 1 ]
if [ ! -f "$out" ]; then printf partial > "$out"; exit 28; fi
[ "$(cat "$out")" = partial ]
cp "$CCP_VOSK_TEST_FIXTURE" "$out"
MOCK
cat > "$tmp/bin/sha256sum" <<'MOCK'
#!/usr/bin/env bash
set -eu
read -r hash file
[ "$hash" = 30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498 ]
cmp "$file" "$CCP_VOSK_TEST_FIXTURE"
MOCK
printf '#!/usr/bin/env bash\nexit 0\n' > "$tmp/bin/sleep"
chmod +x "$tmp/bin/"*
PATH="$tmp/bin:$PATH" bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/good" > /dev/null
test -f "$tmp/good/vosk-model-small-en-us-0.15/am/final.mdl"
printf '#!/usr/bin/env bash\nexit 1\n' > "$tmp/bin/sha256sum"
if PATH="$tmp/bin:$PATH" bash "$root/.github/scripts/fetch-vosk-model.sh" "$tmp/bad" > /dev/null; then
  echo 'checksum failure was accepted' >&2; exit 1
fi
test ! -d "$tmp/bad/vosk-model-small-en-us-0.15"
echo 'Vosk fetch retry and checksum checks passed'
