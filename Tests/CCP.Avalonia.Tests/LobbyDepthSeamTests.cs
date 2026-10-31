using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LobbyDepthSeamTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "CCP.Avalonia"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void LobbyCardsAndJoinKeyUseTheDepthPaint()
    {
        var doc = XDocument.Load(Path.Combine(Root(), "CCP.Avalonia/Views/Tabs/AvailableSubjectsTabView.axaml"));
        var elements = doc.Descendants().ToArray();
        Assert.Contains(elements, e => (string?)e.Attribute("Tag") == "lobby-float"
            && (string?)e.Attribute("Fill") == "{Binding CardShadow}"
            && (string?)e.Attribute("Height") == "{Binding CardShadowPx}");
        Assert.Contains(elements, e => (string?)e.Attribute("BorderBrush") == "{Binding CardRim}");
        Assert.Contains(elements, e => (string?)e.Attribute("BorderBrush") == "{Binding ButtonBevel}");
        Assert.Contains(elements, e => (string?)e.Attribute("Background") == "{Binding ButtonSheen}");
        Assert.Contains(elements, e => (string?)e.Attribute("Theme") == "{StaticResource LobbyJoin}");
    }

    [Fact]
    public void WashFeedsTheExistingRailAndQuestsPainters()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia/Views/Windows/MainShellWindow.SectionChrome.cs"));
        Assert.Contains("PaintDepthRail(hue);", source);
        Assert.Contains("?.PaintDepthQuests(hue);", source);
    }
}
