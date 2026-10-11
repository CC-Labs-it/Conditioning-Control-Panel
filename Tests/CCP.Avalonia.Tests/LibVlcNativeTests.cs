using System;
using System.Collections.Generic;
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
}
