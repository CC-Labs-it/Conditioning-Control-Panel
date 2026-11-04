using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

// Structural paint contracts from the real compiled view, without font/platform setup.
public sealed class Sync6HeaderPaintTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);
    private static XDocument View(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "CCP.Avalonia"))) root = root.Parent;
        return XDocument.Load(Path.Combine(root!.FullName, "CCP.Avalonia", relative));
    }

    [Fact]
    public void AccountChipImmediatelyPrecedesProfileWithSixPixelGap()
    {
        var doc = View("Views/Windows/MainShellWindow.axaml");
        var chip = Named(doc, "BtnAccountChip");
        var bubble = Named(doc, "BtnProfileBubble");
        Assert.Same(bubble.Parent, chip.Parent);
        Assert.Same(chip, bubble.ElementsBeforeSelf().Last());
        Assert.Equal("6,0,0,0", (string?)bubble.Attribute("Margin"));
        Assert.Equal("BtnAccountChip_Click", (string?)chip.Attribute("Click"));
    }

    [Fact]
    public void CompactWalletKeepsHelpInsidePillAsIndependentClickTarget()
    {
        var doc = View("Views/Controls/SparkleWallet.axaml");
        Assert.Equal("29", (string?)doc.Root!.Attribute("Height"));
        Assert.Equal("138", (string?)doc.Root.Attribute("Width"));
        var help = Named(doc, "HelpButton");
        Assert.Same(Named(doc, "WalletButton").Parent, help.Parent);
        Assert.Equal("17", (string?)help.Attribute("Width"));
        Assert.Equal("17", (string?)help.Attribute("Height"));
        Assert.Equal("0,0,5,0", (string?)help.Attribute("Margin"));
        Assert.Equal("2,0,23,0", (string?)Named(doc, "Outline").Attribute("Padding"));
    }
}
