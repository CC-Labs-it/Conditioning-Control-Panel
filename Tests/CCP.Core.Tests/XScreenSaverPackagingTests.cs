using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Core.Tests;

public sealed class XScreenSaverPackagingTests
{
    private static string Read(string path, [CallerFilePath] string source = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", path));

    [Fact]
    public void AurDependsOnXScreenSaver()
    {
        var depends = Regex.Match(Read("packaging/aur/PKGBUILD"), @"(?m)^depends=\(([^)]*)\)");
        Assert.True(depends.Success);
        Assert.Contains("'libxss'", depends.Groups[1].Value);
    }

    [Fact]
    public void InstallGuideListsXScreenSaverForEachDistro()
    {
        string guide = Read("docs/avalonia-linux-install.html");
        var row = Regex.Match(guide, @"<tr><td>Awareness input and desktop idle \(X11 XScreenSaver\)</td>(.*?)</tr>");
        Assert.True(row.Success);
        foreach (string package in new[] { "libxss", "libxss1", "libXScrnSaver" })
            Assert.Contains(">" + package + "</td>", row.Groups[1].Value);
        Assert.Contains("libXss1", guide);
    }

    [Theory]
    [InlineData("arch", "libxss")]
    [InlineData("debian", "libxss1")]
    [InlineData("fedora", "libXScrnSaver")]
    [InlineData("suse", "libXss1")]
    public void DistroCheckInstallsTheRuntime(string family, string package)
    {
        string script = Read("packaging/linux/distro-check/inner.sh");
        var deps = Regex.Match(script, @"(?m)^\s*" + family + @"\)\s+deps=""([^""]+)""");
        Assert.True(deps.Success);
        Assert.Contains(package, deps.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
