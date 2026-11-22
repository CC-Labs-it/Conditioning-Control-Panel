using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Gold box rule (owner): 2 to 5 option rows on show. Owner 2026-11-22: the Whirlpool box keeps
/// Pool size, Smallest size, Stay centred and Restless pool on show and folds the rest behind
/// "More options" in the same box; the fold opens on its own when a folded row is off its default.
/// </summary>
public class SuperBoxFoldTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Rows(string page, string name)
    {
        var path = Path.Combine(Root(), "Features", page);
        var doc = XDocument.Load(path);
        var el = doc.Descendants().SingleOrDefault(e => (string?)e.Attribute(X + "Name") == name);
        Assert.True(el != null, $"{page}: no element named {name}");
        return el!;
    }

    /// <summary>Rows on show = the panel's direct children, minus the gold rule line and the fold.</summary>
    private static XElement[] ShownRows(XElement rows) => rows.Elements()
        .Where(e => e.Name.LocalName != "Border" && e.Name.LocalName != "MoreFold")
        .ToArray();

    private static bool HasNamed(XElement root, string name) =>
        root.DescendantsAndSelf().Any(e => (string?)e.Attribute(X + "Name") == name);

    [Fact]
    public void The_Whirlpool_box_shows_at_most_five_rows_unfolded()
    {
        var rows = Rows("SpiralFeatureControl.xaml", "PoolRows");
        var shown = ShownRows(rows);
        Assert.InRange(shown.Length, 2, 5);
        foreach (var n in new[] { "PoolSizeRow", "PoolSmallestRow", "PoolCentredRow", "PoolRestlessRow" })
            Assert.Contains(shown, e => (string?)e.Attribute(X + "Name") == n);

        var fold = rows.Elements().Single(e => e.Name.LocalName == "MoreFold");
        foreach (var n in new[] { "PoolSpiralRow", "PoolEveryRow", "PoolPopsRow", "PoolDragsRow" })
            Assert.True(HasNamed(fold, n), $"{n} should sit in the fold");
    }

    [Fact]
    public void The_Deck_box_shows_two_to_five_rows()
    {
        var shown = ShownRows(Rows("FlashFeatureControl.xaml", "DeckRows"));
        Assert.InRange(shown.Length, 2, 5);
    }

    [Fact]
    public void The_Whirlpool_fold_opens_when_a_folded_row_is_off_its_default()
    {
        var s = new AppSettings();
        Assert.False(SpiralFeatureControl.PoolFoldedRowsChanged(s));

        // Rows on show never open the fold.
        s.WhirlpoolSize = 140; s.WhirlpoolSmallest = 20; s.WhirlpoolStayCentred = true; s.WhirlpoolRestless = true;
        Assert.False(SpiralFeatureControl.PoolFoldedRowsChanged(s));

        Assert.True(SpiralFeatureControl.PoolFoldedRowsChanged(new AppSettings { WhirlpoolSpiral = "held" }));
        Assert.True(SpiralFeatureControl.PoolFoldedRowsChanged(new AppSettings { WhirlpoolNewEvery = WhirlpoolOptions.HoldDefault + 5 }));
        Assert.True(SpiralFeatureControl.PoolFoldedRowsChanged(new AppSettings { MovingEffectsPop = true }));
        Assert.True(SpiralFeatureControl.PoolFoldedRowsChanged(new AppSettings { WhirlpoolDragsWords = false }));
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
