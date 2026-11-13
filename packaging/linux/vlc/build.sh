#!/usr/bin/env bash
# Builds the pinned libVLC runtime the Linux tarball bundles, plus its LGPL source bundle.
#
#   packaging/linux/vlc/build.sh [--dry-run] [OUT_DIR]     (default OUT_DIR: packaging/linux/vlc/out)
#
# Everything compiles inside BUILD_IMAGE (ubuntu:22.04, glibc 2.35 floor) via podman, or docker
# when podman is absent (CONTAINER_ENGINE overrides). Pins live in versions.env, the shipped
# plugin set in plugins.allow. Outputs, none of which are committed:
#   OUT/ccp-vlc-runtime-<ver>-linux-x64.tar.gz           lib/libvlc.so.5, lib/libvlccore.so.9,
#                                                       lib/vlc/plugins/**, lib/vlc/licenses/**
#   OUT/ccp-vlc-runtime-<ver>-linux-x64-sources.tar     VLC + contrib tarballs + these scripts (D2)
#   OUT/*.sha256, OUT/build.log
# Then run check.sh against the result.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
# shellcheck source-path=SCRIPTDIR source=versions.env
. "$here/versions.env"

dry=0
if [ "${1:-}" = --dry-run ]; then dry=1; shift; fi
out=$(realpath -m "${1:-$here/out}")
cache=$here/cache
engine=${CONTAINER_ENGINE:-$(command -v podman || command -v docker || true)}
run() { echo "+ $*"; [ "$dry" = 1 ] || "$@"; }

[ -n "$engine" ] || { echo "build.sh: neither podman nor docker found" >&2; exit 1; }
tarball=$cache/vlc-$VLC_VERSION.tar.xz
run mkdir -p "$out" "$cache"
if [ ! -f "$tarball" ]; then
    run curl -fL --retry 3 -o "$tarball.part" "$VLC_URL"
    run mv "$tarball.part" "$tarball"
fi
if [ "$dry" = 0 ]; then
    echo "$VLC_SHA256  $tarball" | sha256sum -c - || { rm -f "$tarball"; exit 1; }
fi

cmd=("$engine" run --rm
    -v "$here:/src:ro,z" -v "$tarball:/in/vlc.tar.xz:ro,z" -v "$out:/out:z"
    -e "JOBS=${JOBS:-$(nproc)}" -e "HOST_UID=$(id -u)" -e "HOST_GID=$(id -g)"
    "$BUILD_IMAGE" bash /src/build-inner.sh)
if [ "$dry" = 1 ]; then echo "+ ${cmd[*]}"; exit 0; fi

start=$(date +%s)
"${cmd[@]}" 2>&1 | tee "$out/build.log"
echo "build.sh: done in $(( $(date +%s) - start ))s with $(basename "$engine")"
ls -l "$out/$ARTIFACT.tar.gz" "$out/$ARTIFACT-sources.tar"
