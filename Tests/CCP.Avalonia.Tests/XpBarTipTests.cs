using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class XpBarTipTests
{
    [Fact]
    public void TrackHasNoRoundTipLightAndKeepsTheFillSheenAndFlash()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", ".."));
        var shell = XDocument.Load(Path.Combine(root, "CCP.Avalonia", "Views", "Windows", "MainShellWindow.axaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var track = shell.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "XPBarTrack");
        var nodes = track.Descendants().ToArray();
        var grid = track.Elements().Single(e => e.Name.LocalName == "Grid");
        Assert.Equal(new[] { "XPBar", "XPBarFlashOverlay" }, grid.Elements().Select(e => (string?)e.Attribute(x + "Name")));
        var fill = grid.Elements().Single(e => (string?)e.Attribute(x + "Name") == "XPBar");
        Assert.Equal(new[] { "XPBarSheen" }, fill.Elements().Select(e => (string?)e.Attribute(x + "Name")));

        Assert.DoesNotContain(nodes, e => e.Name.LocalName is "Ellipse" or "RadialGradientBrush");
        Assert.DoesNotContain(nodes, e => e.Attributes().Any(a => a.Value.Contains("DepthTubeBead") || a.Value.Contains("Meniscus")));
        foreach (var name in new[] { "XPBar", "XPBarSheen", "XPBarFlashOverlay" })
            Assert.Contains(nodes, e => (string?)e.Attribute(x + "Name") == name);
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;
}
