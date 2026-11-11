using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Distro-check F3: Debian/Ubuntu/Fedora ship only libvlc.so.5 / libvlccore.so.9.</summary>
public sealed class LibVlcNativeTests
{
    [Theory]
    [InlineData("libvlc", "libvlc.so.5")]
    [InlineData("libvlccore", "libvlccore.so.9")]
    public void ResolvesToTheSonameWhenOnlyTheRuntimePackageIsInstalled(string import, string soname)
    {
        var tried = new List<string>();
        var h = LibVlcNative.Resolve(import, f => { tried.Add(f); return f == soname ? (IntPtr)42 : IntPtr.Zero; });
        Assert.Equal((IntPtr)42, h);
        Assert.Equal(import + ".so", tried[0]);   // the -dev symlink still wins where it exists (Arch, CI)
    }

    [Fact]
    public void OtherImportsAndMissingLibsFallBackToDefaultProbing()
    {
        Assert.Equal(IntPtr.Zero, LibVlcNative.Resolve("libc", _ => (IntPtr)1));
        Assert.Equal(IntPtr.Zero, LibVlcNative.Resolve("libvlc", _ => IntPtr.Zero));
    }

    // The tarball's pinned runtime (packaging/linux/build-tarball.sh) wins unless CCP_VLC=system.
    [Theory]
    [InlineData(true, null, true, true)]
    [InlineData(true, "", true, true)]
    [InlineData(true, "bundled", true, true)]
    [InlineData(true, "system", true, false)]
    [InlineData(true, " SYSTEM ", true, false)]
    [InlineData(true, null, false, false)]    // no lib/libvlc.so.5: the distro libvlc
    [InlineData(false, null, true, false)]    // Windows/macOS never take the Linux bundle
    public void BundleDirFollowsTheLoadRule(bool linux, string? ccpVlc, bool present, bool bundled)
    {
        var app = Path.Combine(Path.GetTempPath(), "ccp-app");
        var asked = new List<string>();
        var dir = LibVlcNative.BundleDir(app, ccpVlc, linux, f => { asked.Add(f); return present; });
        Assert.Equal(bundled ? Path.Combine(app, "lib") : null, dir);
        if (linux && ccpVlc?.Trim().ToLowerInvariant() != "system")
            Assert.Equal(Path.Combine(app, "lib", "libvlc.so.5"), Assert.Single(asked));
    }

    [Theory]
    [InlineData("libvlc", "libvlc.so.5")]
    [InlineData("libvlccore", "libvlccore.so.9")]
    public void ABundleLoadsOnlyItsOwnFilesSoSystemAndBundleNeverMix(string import, string file)
    {
        var tried = new List<string>();
        Assert.Equal(IntPtr.Zero, LibVlcNative.Resolve(import, f => { tried.Add(f); return IntPtr.Zero; }, "/opt/ccp/lib"));
        Assert.Equal(new[] { Path.Combine("/opt/ccp/lib", file) }, tried);
        Assert.Equal((IntPtr)7, LibVlcNative.Resolve(import, _ => (IntPtr)7, "/opt/ccp/lib"));
    }
}
