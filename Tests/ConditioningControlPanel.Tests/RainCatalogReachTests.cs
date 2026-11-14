using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.2 lane L9 (handoff-1030/72-PRERELEASE-PLAN.md review pass item 8): every rain row the Prize Counter
/// ships (catalog v2, incl. action-yellow, hazmat-mask and back-katana) reaches the wardrobe's items.json and
/// RainWardrobeCatalog as a shop item on the right tab, and the earned pieces (choker, ribbons, collar) are
/// never counter rows and wear for an account that earned them.
/// </summary>
public sealed class RainCatalogReachTests
{
    [Fact]
    public void Every_rain_row_the_counter_ships_reaches_the_wardrobe_as_a_shop_item()
    {
        var root = RepoRoot();
        var counter = (JArray)JObject.Parse(File.ReadAllText(Path.Combine(root, "Assets", "web", "prizecounter", "assets", "manifest.json")))["items"]!;
        var rainIds = counter.Where(t => (string?)t["family"] == "rain").Select(t => (string)t["id"]!).ToList();
        Assert.Equal(19, rainIds.Count);   // server catalog v2: 7 dresses, 2 suits, 6 hair, 4 accessories
        foreach (var must in new[] { "rain.suit.action-yellow", "rain.acc.hazmat-mask", "rain.acc.back-katana" })
            Assert.Contains(must, rainIds);

        var manifest = LoadManifest();
        foreach (var id in rainIds)
        {
            var row = manifest.Items.SingleOrDefault(i => i.Grant == "cos." + id);
            Assert.True(row != null, $"counter row {id} has no items.json shop row");
            Assert.Equal(RainItemAccessKind.Shop, row!.Access);
            Assert.Equal(id.StartsWith("rain.dress.") || id.StartsWith("rain.suit.") ? "rain-clothes" : "rain-hair", row.Tab);
            Assert.Contains(RainWardrobeCatalog.Items, i => i.Grant == "cos." + id);
        }
    }

    [Fact]
    public void Earned_pieces_are_never_counter_rows_and_wear_for_an_earner()
    {
        var manifest = LoadManifest();
        var earned = new[] { "velvet-choker", "hair-bow", "midnight-ribbon", "rose-gold-collar" };
        foreach (var id in earned)
        {
            var row = manifest.Find("accessories", id)!;
            Assert.Equal(RainItemAccessKind.Unlock, row.Access);
            Assert.Null(row.Grant);
        }
        var earner = new RainItemAccess(manifest, new RainGrantSet(null), new Lvl { Level = 50, UnlockedAchievements = new[] { "first_week_graduate" } });
        Assert.All(earned, id => Assert.Equal(RainItemState.Unlocked, earner.StateOf("accessories", id).State));
        Assert.All(earned, id => Assert.DoesNotContain("accessories:" + id,
            RainWardrobeCatalog.LockedKeys(null, new Lvl { Level = 50, UnlockedAchievements = new[] { "first_week_graduate" } })));
    }

    private sealed class Lvl : IRainProgressSource
    {
        public int Level { get; init; } = 1;
        public IReadOnlyCollection<string> UnlockedAchievements { get; init; } = Array.Empty<string>();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static RainItemManifest LoadManifest() =>
        RainItemManifest.Load(Path.Combine(RepoRoot(), "Assets", "web", "rain-wardrobe", RainItemManifest.FileName));
}
