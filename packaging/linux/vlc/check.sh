#!/usr/bin/env bash
# Proves a runtime built by build.sh is shippable. Exits non-zero on any failure.
#
#   packaging/linux/vlc/check.sh [RUNTIME_TAR_GZ]     (default: out/ccp-vlc-runtime-<ver>-linux-x64.tar.gz)
#
# In a clean BUILD_IMAGE (ubuntu:22.04, no VLC, no FFmpeg):
#   1. ldd -r: the only unresolved libraries are libpulse.so.0 / libasound.so.2 (distro deps);
#      after installing just libpulse0 + libasound2, nothing is unresolved and no symbol undefined.
#   2. No GLIBC symbol above GLIBC_CEILING, no GLIBCXX above GLIBCXX_CEILING.
#   3. The plugin tree equals plugins.allow exactly, and no plugin declares a GPL license.
#   4. probe.py decodes H.264+AAC (mp4, ts), VP9+Opus (webm), WMV2+WMA2 (wmv), FLV1+MP3 (flv)
#      and MP3 through the bundled libvlc
#      into vmem/amem, headless, with the default plugin discovery (no VLC_PLUGIN_PATH).
# Test clips come from ffmpeg in CLIP_IMAGE and are cached in out/clips; nothing from it ships.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
# shellcheck source-path=SCRIPTDIR source=versions.env
. "$here/versions.env"

if [ "${1:-}" = --inside ]; then
    fail=0
    bad() { echo "FAIL $*"; fail=1; }
    rt=/rt/lib
    mapfile -t libs < <(find "$rt" -name '*.so*' -type f | sort)
    echo "== 1a. ldd in pristine $BUILD_IMAGE (${#libs[@]} ELF files)"
    missing=$(for f in "${libs[@]}"; do ldd -r "$f" 2>&1 | awk '/not found/ {print $1}'; done | sort -u)
    echo "unresolved before distro deps: ${missing//$'\n'/ }"
    extra=$(grep -vxE 'libpulse\.so\.0|libasound\.so\.2' <<<"$missing" || true)
    [ -z "$extra" ] || bad "unresolved beyond libpulse/libasound: $extra"

    export DEBIAN_FRONTEND=noninteractive
    apt-get update -qq && apt-get install -y -qq --no-install-recommends \
        libpulse0 libasound2 python3 binutils >/dev/null
    echo "== 1b. ldd -r with libpulse0 + libasound2"
    for f in "${libs[@]}"; do
        if ldd -r "$f" 2>&1 | grep -E 'not found|undefined symbol'; then bad "$f"; fi
    done

    echo "== 2. symbol version ceilings"
    for v in "GLIBC:$GLIBC_CEILING" "GLIBCXX:$GLIBCXX_CEILING"; do
        tag=${v%%:*} cap=${v#*:}
        max=$(for f in "${libs[@]}"; do objdump -T "$f"; done | grep -oE "\b${tag}_[0-9.]+" | sed "s/${tag}_//" | sort -uV | tail -1)
        echo "max $tag: ${max:-none} (ceiling $cap)"
        [ -z "$max" ] || [ "$(printf '%s\n' "$max" "$cap" | sort -V | tail -1)" = "$cap" ] || bad "$tag $max > $cap"
    done

    echo "== 3. modules"
    diff <(grep -Ev '^[[:space:]]*(#|$)' /src/plugins.allow | sort) \
        <(cd "$rt/vlc/plugins" && find . -name '*.so' | sed 's|^\./||' | sort) || bad "plugin tree != plugins.allow"
    for f in "$rt"/vlc/plugins/*/*.so; do
        if grep -qa 'GNU General Public License' "$f"; then bad "GPL module: $f"; fi
    done
    test -s "$rt/vlc/plugins/plugins.dat" || bad "no plugins.dat"
    (cd "$rt/vlc/plugins" && find . -name '*.so' | sed 's|^\./||' | sort | awk -F/ '
        {s=$2; sub(/^lib/, "", s); sub(/_plugin\.so$/, "", s); n[$1]++; m[$1]=m[$1] " " s}
        END {for (d in n) printf "  %-14s %2d:%s\n", d, n[d], m[d]}' | sort)
    echo "$(find "$rt/vlc/plugins" -name '*.so' | wc -l) modules, runtime $(du -sh /rt | cut -f1) unpacked"

    echo "== 4. decode"
    for c in "h264_aac.mp4 40 1.5" "vp9_opus.webm 40 1.5" "audio.mp3 0 1.5" \
        "h264_aac.ts 40 1.5" "wmv2_wma.wmv 40 1.5" "flv1_mp3.flv 40 1.5"; do
        read -r clip frames secs <<<"$c"
        python3 /src/probe.py "$rt/libvlc.so.5" "/clips/$clip" "$frames" "$secs" || bad "decode $clip"
    done
    [ "$fail" = 0 ] && echo "check: PASS" || echo "check: FAIL"
    exit "$fail"
fi

runtime=$(realpath "${1:-$here/out/$ARTIFACT.tar.gz}")
engine=${CONTAINER_ENGINE:-$(command -v podman || command -v docker || true)}
[ -n "$engine" ] || { echo "check.sh: neither podman nor docker found" >&2; exit 1; }
clips=$here/out/clips
if [ ! -f "$clips/flv1_mp3.flv" ]; then
    mkdir -p "$clips"
    # shellcheck disable=SC2016 # expanded by the container's sh
    "$engine" run --rm -v "$clips:/clips:z" "$CLIP_IMAGE" sh -euc '
        apk add -q --no-cache ffmpeg
        v="-f lavfi -i testsrc2=size=320x240:rate=25:duration=2"
        a="-f lavfi -i sine=frequency=440:duration=2:sample_rate=48000"
        ffmpeg -v error -y $v $a -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest /clips/h264_aac.mp4
        ffmpeg -v error -y $v $a -c:v libvpx-vp9 -c:a libopus -shortest /clips/vp9_opus.webm
        ffmpeg -v error -y $a -c:a libmp3lame /clips/audio.mp3
        ffmpeg -v error -y $v $a -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest /clips/h264_aac.ts
        ffmpeg -v error -y $v $a -c:v wmv2 -c:a wmav2 -shortest /clips/wmv2_wma.wmv
        ffmpeg -v error -y $v $a -c:v flv -c:a libmp3lame -ar 44100 -shortest /clips/flv1_mp3.flv
        chown '"$(id -u):$(id -g)"' /clips/*'
fi
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
tar xzf "$runtime" -C "$tmp"
"$engine" run --rm -v "$here:/src:ro,z" -v "$tmp:/rt:ro,z" -v "$clips:/clips:ro,z" \
    "$BUILD_IMAGE" bash /src/check.sh --inside
