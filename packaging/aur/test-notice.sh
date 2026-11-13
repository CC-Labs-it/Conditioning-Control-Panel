#!/usr/bin/env bash
# Verify the AUR package function installs the licence and RT engine notice together.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/src/ConditioningControlPanel/share" "$tmp/pkg"
printf 'licence\n' > "$tmp/src/ConditioningControlPanel/LICENSE"
printf 'third party\n' > "$tmp/src/ConditioningControlPanel/THIRD-PARTY-NOTICES.txt"
printf 'notice\n' > "$tmp/src/ConditioningControlPanel/RT-ENGINE-NOTICES.txt"
cd "$tmp/src"
source "$here/PKGBUILD"
pkgdir=$tmp/pkg
package
licences=$pkgdir/usr/share/licenses/$pkgname
cmp ConditioningControlPanel/RT-ENGINE-NOTICES.txt "$licences/RT-ENGINE-NOTICES.txt"
cmp ConditioningControlPanel/THIRD-PARTY-NOTICES.txt "$licences/THIRD-PARTY-NOTICES.txt"
cmp ConditioningControlPanel/LICENSE "$licences/LICENSE"
echo 'AUR licence and notice installed'
