#!/usr/bin/env bash
# Recover expired shard artifacts by actually running the missing shards, then check coverage.
set -euo pipefail
mode=${1:?mode required}
root=${2:?artifact directory required}
os=${3:?os required}
case "$os" in linux|windows) ;; *) echo "invalid OS: $os" >&2; exit 2 ;; esac

missing() {
  local i dir
  for i in 0 1 2 3 4; do
    dir="$root/avalonia-$os-shard-$i"
    if [[ ! -s "$dir/avalonia-shard-$i.total" || ! -s "$dir/avalonia-expected.txt" ]]; then
      printf '%s ' "$i"
    fi
  done
}

case "$mode" in
  missing) missing ;;
  recover)
    for i in $(missing); do
      dir="$root/avalonia-$os-shard-$i"
      mkdir -p "$dir"
      bash .github/scripts/avalonia-tests-sharded.sh --shard "$i"
      mv "avalonia-shard-$i.total" "$dir/avalonia-shard-$i.total"
      bash .github/scripts/avalonia-tests-sharded.sh --expected > "$dir/avalonia-expected.txt"
    done ;;
  check)
    absent=$(missing)
    [[ -z "$absent" ]] || { echo "::error::Missing shard results: $absent"; exit 1; }
    expected=''
    ran=0
    for i in 0 1 2 3 4; do
      dir="$root/avalonia-$os-shard-$i"
      size=$(tr -d '\r' < "$dir/avalonia-expected.txt")
      total=$(tr -d '\r' < "$dir/avalonia-shard-$i.total")
      [[ "$size" =~ ^[1-9][0-9]*$ && "$total" =~ ^(0|[1-9][0-9]*)$ ]] || { echo "::error::Invalid counts in shard $i"; exit 1; }
      [[ -z "$expected" || "$expected" == "$size" ]] || { echo "::error::Shards disagree on the suite size"; exit 1; }
      expected=$size
      ran=$((ran + total))
    done
    echo "avalonia $os shards ran $ran of $expected tests (5 shard files)"
    [[ "$ran" -eq "$expected" ]] || { echo "::error::Avalonia $os shards ran $ran tests but the suite lists $expected"; exit 1; } ;;
  *) echo "invalid mode: $mode" >&2; exit 2 ;;
esac
