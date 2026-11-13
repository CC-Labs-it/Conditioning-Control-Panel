#!/usr/bin/env bash
# shellcheck disable=SC2016 # '$ORIGIN' is literal, for the dynamic loader
# Runs inside BUILD_IMAGE, started by build.sh. Mounts: /src = this directory (ro),
# /in/vlc.tar.xz = the sha256-verified VLC tarball (ro), /out = where the two artifacts land.
set -euo pipefail
# shellcheck source-path=SCRIPTDIR source=versions.env
. /src/versions.env
jobs=${JOBS:-$(nproc)}
prefix=/opt/ccp-vlc            # configure prefix; nothing outside lib/ of it ships
cprefix=/build/contrib-prefix  # contrib's static libs and .pc files
rt=/build/rt                   # runtime tree, becomes $ARTIFACT.tar.gz
tarx() { tar --sort=name --mtime=@0 --owner=0 --group=0 --numeric-owner "$@"; }

echo "== apt"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y -qq --no-install-recommends \
    build-essential pkg-config autoconf automake libtool libtool-bin autopoint gettext \
    cmake meson ninja-build nasm curl ca-certificates xz-utils bzip2 python3 flex bison \
    patch patchelf git zstd libpulse-dev libasound2-dev >/dev/null

mkdir -p /build && cd /build
tar xf /in/vlc.tar.xz
cd "vlc-$VLC_VERSION"
src=$PWD

echo "== contrib: $CONTRIB_PKGS"
enable=()
for p in $CONTRIB_PKGS; do enable+=("--enable-$p"); done
mkdir -p contrib/native && cd contrib/native
../bootstrap --prefix="$cprefix" --disable-gpl --disable-gnuv3 --disable-sout --disable-net \
    --disable-disc --disable-all "${enable[@]}"
make -j"$jobs" --no-print-directory fetch
make -j"$jobs" --no-print-directory install

echo "== configure + build libVLC"
mkdir -p "$src/build" && cd "$src/build"
# Contrib ships static archives only, so their private link deps must come from pkg-config.
cat > /build/pkg-config <<'EOF'
#!/bin/sh
# --static only for contrib .pc files; system ones (libpulse, alsa) stay dynamic.
for a; do case $a in -*) ;; *) [ -f "$CPREFIX/lib/pkgconfig/${a%% *}.pc" ] && exec pkg-config --static "$@";; esac; done
exec pkg-config "$@"
EOF
chmod +x /build/pkg-config
export CPREFIX=$cprefix
# avformat = the demux plugin for .flv; 3.0.24 avcommon.h also needs it for avcodec to build.
config=(--prefix="$prefix" --with-contrib="$cprefix" PKG_CONFIG=/build/pkg-config
    BUILDCC="gcc -std=gnu99"
    --disable-vlc --disable-lua --disable-qt --disable-skins2 --disable-ncurses --disable-dbus
    --disable-nls --disable-xcb --disable-wayland --disable-libgcrypt --disable-sout --disable-vlm
    --disable-postproc --disable-a52 --disable-update-check
    --enable-avcodec --enable-avformat --enable-swscale --enable-mpg123 --enable-vorbis --enable-opus --enable-flac
    --enable-vpx --enable-dav1d --enable-soxr --enable-ogg --enable-mkv --enable-dvbpsi --enable-pulse --enable-alsa)
../configure "${config[@]}"
make -j"$jobs" --no-print-directory
make install --no-print-directory >/dev/null

echo "== assemble runtime"
mkdir -p "$rt/lib/vlc/plugins" "$rt/lib/vlc/licenses"
# libvlc_pulse = the helper library the pulse output links against.
cp -L "$prefix/lib/libvlc.so.5" "$prefix/lib/libvlccore.so.9" "$prefix/lib/vlc/libvlc_pulse.so.0" "$rt/lib/"
while read -r p; do
    install -D -m644 "$prefix/lib/vlc/plugins/$p" "$rt/lib/vlc/plugins/$p"
done < <(grep -Ev '^[[:space:]]*(#|$)' /src/plugins.allow)
find "$rt/lib" -name '*.so*' -exec strip --strip-unneeded {} +
# libvlc finds libvlccore beside it; plugins find it three levels up (lib/vlc/plugins/<dir>/).
# VLC 3 itself looks for plugins at <dir of libvlccore>/vlc/plugins, so no env var is needed.
patchelf --set-rpath '$ORIGIN' "$rt/lib/libvlc.so.5"
patchelf --set-rpath '$ORIGIN' "$rt/lib/libvlc_pulse.so.0"
patchelf --remove-rpath "$rt/lib/libvlccore.so.9"
find "$rt/lib/vlc/plugins" -name '*.so' -exec patchelf --set-rpath '$ORIGIN/../../..' {} \;
"$prefix/lib/vlc/vlc-cache-gen" "$rt/lib/vlc/plugins"
test -s "$rt/lib/vlc/plugins/plugins.dat"

cp "$src/COPYING.LIB" "$rt/lib/vlc/licenses/VLC-COPYING.LIB"
cp "$src/AUTHORS" "$rt/lib/vlc/licenses/VLC-AUTHORS"
for d in "$src"/contrib/native/*/; do
    for f in "$d"COPYING* "$d"LICENSE* "$d"LICENCE* "$d"LGPL*; do
        [ -f "$f" ] && install -D -m644 "$f" "$rt/lib/vlc/licenses/$(basename "$d")/$(basename "$f")"
    done
done
ls "$src/contrib/tarballs" > /build/contrib-tarballs.txt
{
    echo "libVLC $VLC_VERSION for Conditioning Control Panel (Linux x86_64)"
    echo "Source: $VLC_URL (sha256 $VLC_SHA256)"
    echo "Corresponding source: $ARTIFACT-sources.tar on the same GitHub release"
    echo "Built in: $BUILD_IMAGE"
    echo "Contrib (static): $CONTRIB_PKGS"
    sed 's/^/  /' /build/contrib-tarballs.txt
    echo "Configure: ${config[*]}"
    echo "Plugins:"
    (cd "$rt/lib/vlc/plugins" && find . -name '*.so' | sed 's|^\./|  |' | sort)
} > "$rt/lib/vlc/BUILDINFO.txt"
tarx -cf - -C "$rt" lib | gzip -9n > "/out/$ARTIFACT.tar.gz"

echo "== source bundle (LGPL-2.1 s6, owner decision D2)"
sb=/build/sb/$ARTIFACT-sources
mkdir -p "$sb/contrib-tarballs" "$sb/packaging-linux-vlc"
cp /in/vlc.tar.xz "$sb/vlc-$VLC_VERSION.tar.xz"
cp "$src"/contrib/tarballs/* "$sb/contrib-tarballs/"
cp /src/*.sh /src/*.py /src/versions.env /src/plugins.allow "$sb/packaging-linux-vlc/"
cp "$rt/lib/vlc/BUILDINFO.txt" "$sb/"
tarx -cf "/out/$ARTIFACT-sources.tar" -C /build/sb "$ARTIFACT-sources"

cd /out
for f in "$ARTIFACT.tar.gz" "$ARTIFACT-sources.tar"; do sha256sum "$f" > "$f.sha256"; done
[ -z "${HOST_UID:-}" ] || chown "$HOST_UID:${HOST_GID:-$HOST_UID}" /out/*
ls -l /out
