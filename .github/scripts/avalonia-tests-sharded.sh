#!/usr/bin/env bash
# Runs Tests/CCP.Avalonia.Tests in several testhosts instead of one.
#
# Why: each MainShellWindow a test built used to leave ~250 MB of native memory behind
# (docs/avalonia-decisions.html), so one testhost for the whole suite peaked at 12-15 GB RSS and
# the 16 GB ubuntu runner died mid-suite ("runner has received a shutdown signal", MSB4166).
# The owners are fixed (docs/avalonia-decisions.html, suite-memory: shards now peak at 1.6-2.9 GB); the
# shards stay as headroom and for wall time.
# Shards are by the first letter of the test class; the last shard is the complement of all the
# others, so a test cannot fall between shards. The guard below still proves it: the shards'
# totals must add up to the suite's own test list.
#
# Modes (CI runs each shard as its own matrix job, see .github/workflows/build.yml):
#   (no argument)  build, run every shard in turn, check the sum. bin/gate calls it this way.
#   --shard N      run only shard N (0-based, no build) and write its Total: to avalonia-shard-N.total.
#   --expected     print the suite's --list-tests count (no build).
# The avalonia-coverage jobs sum the per-shard files and compare them to --expected.
set -euo pipefail

project=Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj
prefix=CCP.Avalonia.Tests.
shards=("A B C D E" "F I J K L" "G H M N O P Q R" "S T")

contains() { local f="" l; for l in $1; do f+="${f:+|}FullyQualifiedName~$prefix$l"; done; echo "$f"; }
filters=()
rest=""
for s in "${shards[@]}"; do
  filters+=("$(contains "$s")")
  for l in $s; do rest+="${rest:+&}FullyQualifiedName!~$prefix$l"; done
done
filters+=("$rest")

list_expected() { dotnet test "$project" -c Release --no-build --list-tests | grep -cE "^\s+$prefix"; }
# Runs one filter; sets status and total.
run_filter() {
  local out; out=$(mktemp)
  if dotnet test "$project" -c Release --no-build --filter "$1" 2>&1 | tee "$out"; then status=0; else status=$?; fi
  total=$(grep -oE 'Total: +[0-9]+' "$out" | grep -oE '[0-9]+' | tail -1 || true)
  rm -f "$out"
}

case "${1:-}" in
  "") ;;
  --expected) list_expected; exit 0 ;;
  --shard)
    n=${2:-}
    if ! [[ "$n" =~ ^[0-9]+$ ]] || [ "$n" -ge "${#filters[@]}" ]; then
      echo "::error::--shard needs 0..$((${#filters[@]} - 1)), got '$n'"; exit 2
    fi
    run_filter "${filters[$n]}"
    echo "${total:-0}" > "avalonia-shard-$n.total"
    echo "avalonia shard $n ran ${total:-0} tests"
    exit "$status" ;;
  *) echo "usage: $0 [--shard N | --expected]" >&2; exit 2 ;;
esac

dotnet build "$project" -c Release

expected=$(list_expected)
ran=0
worst=0
for f in "${filters[@]}"; do
  run_filter "$f"
  # Keep going so one run reports every failing shard; exit with the worst status at the end.
  [ "$status" -eq 0 ] || worst=$status
  ran=$((ran + ${total:-0}))
done

echo "avalonia shards ran $ran of $expected tests"
[ "$worst" -eq 0 ] || exit "$worst"
if [ "$ran" -ne "$expected" ]; then
  echo "::error::Avalonia test shards ran $ran tests but the suite lists $expected"
  exit 1
fi
