using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class LinuxPackageVersionTests
{
    [Fact]
    public void AurDownloadVersionMatchesThePublishedTarballVersion()
    {
        var root = RepoRoot();
        var version = XDocument.Load(Path.Combine(root, "Version.props")).Root!
            .Element("PropertyGroup")!.Element("Version")!.Value;
        var recipe = File.ReadAllText(Path.Combine(root, "packaging", "aur", "PKGBUILD"));
        var match = Regex.Match(recipe, @"(?m)^pkgver=([^\s]+)");
        Assert.True(match.Success, "The AUR recipe must name the version its release URL downloads.");
        Assert.Equal(version, match.Groups[1].Value);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
