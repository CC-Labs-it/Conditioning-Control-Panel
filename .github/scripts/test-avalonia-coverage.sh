#!/usr/bin/env bash
set -euo pipefail
repo=$(pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/.github/scripts" "$tmp/bin" "$tmp/shards"
cp "$repo/.github/scripts/avalonia-coverage.sh" "$repo/.github/scripts/avalonia-tests-sharded.sh" "$tmp/.github/scripts/"
cat > "$tmp/bin/dotnet" <<'DOTNET'
#!/usr/bin/env bash
printf '%s\n' "$*" >> "$CCP_COVERAGE_CALLS"
if [[ " $* " == *" --list-tests "* ]]; then
  for i in 1 2 3 4 5; do echo "    CCP.Avalonia.Tests.Example.Test$i"; done
elif [[ "${CCP_COVERAGE_FAIL:-0}" == 1 ]]; then
  echo 'Failed! - Failed: 1, Total: 1'
  exit 1
else
  echo 'Passed! - Failed: 0, Passed: 1, Total: 1'
fi
DOTNET
chmod +x "$tmp/bin/dotnet"
export PATH="$tmp/bin:$PATH" CCP_COVERAGE_CALLS="$tmp/calls"
cd "$tmp"
script=.github/scripts/avalonia-coverage.sh
for i in 0 1; do
  mkdir -p "shards/avalonia-windows-shard-$i"
  echo 1 > "shards/avalonia-windows-shard-$i/avalonia-shard-$i.total"
  echo 5 > "shards/avalonia-windows-shard-$i/avalonia-expected.txt"
done
[[ $(bash "$script" missing shards windows) == '2 3 4 ' ]]
if bash "$script" check shards windows; then echo 'missing shard accepted' >&2; exit 1; fi
bash "$script" recover shards windows
[[ $(grep -c -- '--filter' "$CCP_COVERAGE_CALLS") == 3 ]]
bash "$script" check shards windows
[[ -z $(bash "$script" missing shards windows) ]]
bash "$script" recover shards windows
[[ $(grep -c -- '--filter' "$CCP_COVERAGE_CALLS") == 3 ]]
echo 2 > shards/avalonia-windows-shard-3/avalonia-shard-3.total
if bash "$script" check shards windows; then echo 'wrong total accepted' >&2; exit 1; fi
echo 1 > shards/avalonia-windows-shard-3/avalonia-shard-3.total
echo 6 > shards/avalonia-windows-shard-3/avalonia-expected.txt
if bash "$script" check shards windows; then echo 'disagreeing suite size accepted' >&2; exit 1; fi
echo bad > shards/avalonia-windows-shard-2/avalonia-shard-2.total
if bash "$script" check shards windows; then echo 'invalid count accepted' >&2; exit 1; fi
rm shards/avalonia-windows-shard-4/avalonia-shard-4.total
if CCP_COVERAGE_FAIL=1 bash "$script" recover shards windows; then echo 'failed shard accepted' >&2; exit 1; fi
echo 'avalonia coverage self-test passed'
