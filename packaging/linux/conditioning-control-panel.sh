#!/bin/sh
# Tarball launcher: the AUR wrapper and docs/avalonia-linux-install.html exec this, not CCP.Avalonia.
# .NET 10 loads libssl.so.3 (or 1.1) and aborts with exit 134 when neither exists, before any app code can
# explain why. Fedora 45 ships only OpenSSL 4 (libssl.so.4; #4175), so name the package here instead.
# CCP_SKIP_OPENSSL_CHECK=1 skips the probe. CCP_OPENSSL_PROBE_DIRS and CCP_OS_RELEASE are test seams.
here=$(dirname "$(readlink -f "$0")")

has_ssl() {
    [ -n "$CLR_OPENSSL_VERSION_OVERRIDE$DOTNET_OPENSSL_VERSION_OVERRIDE" ] && return 0   # the user picked a version for .NET
    ldc=$(command -v ldconfig || { [ -x /sbin/ldconfig ] && echo /sbin/ldconfig; })
    [ -n "$ldc" ] && "$ldc" -p 2>/dev/null | grep -Eq 'libssl\.so\.(3|1\.1) ' && return 0
    dirs=${CCP_OPENSSL_PROBE_DIRS-"/usr/lib64 /lib64 /usr/lib /lib /usr/lib/x86_64-linux-gnu /lib/x86_64-linux-gnu /usr/lib/aarch64-linux-gnu /usr/local/lib64 /usr/local/lib"}
    for d in $(printf %s "$LD_LIBRARY_PATH" | tr : ' ') $dirs; do
        [ -e "$d/libssl.so.3" ] || [ -e "$d/libssl.so.1.1" ] && return 0
    done
    return 1
}

if [ "$CCP_SKIP_OPENSSL_CHECK" != 1 ] && ! has_ssl; then
    ids=$(. "${CCP_OS_RELEASE:-/etc/os-release}" 2>/dev/null; echo " $ID $ID_LIKE ")
    case "$ids" in
        *" fedora "*) cmd="sudo dnf install openssl3-libs" ;;
        *" arch "*) cmd="sudo pacman -S openssl" ;;
        *" debian "*|*" ubuntu "*) cmd="sudo apt install libssl3t64   (libssl3 on older releases)" ;;
        *" suse "*|*" opensuse "*) cmd="sudo zypper install libopenssl3" ;;
        *) cmd= ;;
    esac
    {
        echo "Conditioning Control Panel needs OpenSSL 3 (libssl.so.3), and this system does not have it."
        echo ".NET 10 cannot use OpenSSL 4. OpenSSL 3 installs alongside it."
        if [ -n "$cmd" ]; then echo "Install it with:"; echo "  $cmd"
        else echo "Install your distribution's OpenSSL 3 library package (the one that provides libssl.so.3)."; fi
        echo "Then start the app again."
    } >&2
    exit 1
fi
# NVIDIA: WebKitGTK's DMA-BUF renderer cannot allocate GBM buffers there, so web views stay blank. Same rule as
# CCP.Core LinuxWebKitEnv (Program.Main repeats it for non-tarball runs); a value the user set, even empty or 0, wins.
# CCP_NVIDIA_PROBE is a test seam.
if [ -z "${WEBKIT_DISABLE_DMABUF_RENDERER+x}" ]; then
    for p in ${CCP_NVIDIA_PROBE-/proc/driver/nvidia/version /sys/module/nvidia}; do
        [ -e "$p" ] && { export WEBKIT_DISABLE_DMABUF_RENDERER=1; break; }
    done
fi
exec "$here/CCP.Avalonia" "$@"
