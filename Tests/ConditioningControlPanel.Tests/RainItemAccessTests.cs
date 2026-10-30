using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Rain V2 lane R3 (handoff-1030/RAIN-V2-PLAN.md 2.5 + section 3): items.json, the per-item access state and the
/// look clamp. The manifest is read from the checked-in file so the tests pin the shipped data, not a copy.
/// </summary>
public class RainItemAccessTests
{
    private sealed class Progress : IRainProgressSource
    {
        public int Level { get; init; } = 1;
        public IReadOnlyCollection<string> UnlockedAchievements { get; init; } = Array.Empty<string>();
    }

    private static string RepoRoot()
    {
        return SourceRoots.RepoRoot;
    }

    private static string WardrobeDir() => Path.Combine(RepoRoot(), "Assets", "web", "rain-wardrobe");

    private static RainItemManifest Shipped() => RainItemManifest.Load(Path.Combine(WardrobeDir(), RainItemManifest.FileName));

    private static RainItemAccess Access(int level = 1, string[]? achievements = null, params string[] grants)
        => new(Shipped(), new RainGrantSet(grants), new Progress { Level = level, UnlockedAchievements = achievements ?? Array.Empty<string>() });

    [Fact]
    public void Shipped_manifest_parses_with_unique_slot_qualified_ids()
    {
        var m = Shipped();
        Assert.True(m.Items.Count >= 70, "items.json lost rows: " + m.Items.Count);
        Assert.Equal(m.Items.Count, m.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(m.Items, i => Assert.Equal(i.Slot + ":" + i.PageId, i.Id));
    }

    [Fact]
    public void Every_grant_is_a_rain_cosmetic_and_matches_its_shop_row()
    {
        var shop = Shipped().Items.Where(i => i.Access == RainItemAccessKind.Shop).ToList();
        Assert.Equal(21, shop.Count); // catalog v2's 19 rain rows + the two later hair drops
        foreach (var i in shop)
        {
            Assert.Matches(new Regex(@"^cos\.rain\.(dress|suit|hair|acc)\.[a-z0-9-]+$"), i.Grant!);
            Assert.EndsWith("." + i.PageId, i.Grant!);
            Assert.Contains(i.Tab, new[] { "rain-clothes", "rain-hair" });
        }
        Assert.All(Shipped().Items.Where(i => i.Access != RainItemAccessKind.Shop), i => Assert.Null(i.Grant));
    }

    [Fact]
    public void Every_page_id_exists_in_the_wardrobe_page_modules()
    {
        var dir = WardrobeDir();
        var source = string.Concat(
            Directory.EnumerateFiles(dir, "*.js").Select(File.ReadAllText)
                .Append(File.ReadAllText(Path.Combine(dir, "assets", "runtime-info.json"))));
        var missing = Shipped().Items
            .Where(i => !source.Contains("'" + i.PageId + "'") && !source.Contains("\"" + i.PageId + "\""))
            .Select(i => i.Id).ToList();
        Assert.True(missing.Count == 0, "items.json names page ids the wardrobe page lacks: " + string.Join(", ", missing));
    }

    [Fact]
    public void Defaults_skin_tones_and_none_are_free()
    {
        var a = Access();
        foreach (var (slot, id) in new[] { ("dress", "satin-original"), ("hair", "original-blonde"), ("accessories", "hoops-original"),
                     ("skin", "original"), ("skin", "warm"), ("skin", "deep"), ("shoes", "original-boots"),
                     ("accessories", "glasses"), ("arms", "none"), ("ankles", "none") })
            Assert.Equal(RainItemState.Free, a.StateOf(slot, id).State);
    }

    [Fact]
    public void Shop_items_are_buyable_until_the_grant_arrives()
    {
        Assert.Equal(RainItemState.Buyable, Access().StateOf("dress", "midnight-cocktail").State);
        var owned = Access(grants: "cos.rain.dress.midnight-cocktail").StateOf("dress", "midnight-cocktail");
        Assert.Equal(RainItemState.Owned, owned.State);
        Assert.True(owned.Wearable);
        Assert.Equal(RainItemState.Owned, Access(grants: "cos.rain.suit.hazmat-yellow").StateOf("dress", "hazmat-yellow").State);
    }

    [Fact]
    public void Level_unlocks_open_at_their_level()
    {
        var low = Access(level: 9).StateOf("accessories", "velvet-choker");
        Assert.Equal(RainItemState.Locked, low.State);
        Assert.Equal(10, low.NeedsLevel);
        Assert.Equal(RainItemState.Unlocked, Access(level: 10).StateOf("accessories", "velvet-choker").State);
        Assert.Equal(RainItemState.Locked, Access(level: 29).StateOf("accessories", "midnight-ribbon").State);
        Assert.Equal(RainItemState.Unlocked, Access(level: 30).StateOf("accessories", "midnight-ribbon").State);
        Assert.Equal(RainItemState.Locked, Access(level: 49).StateOf("accessories", "rose-gold-collar").State);
        Assert.Equal(RainItemState.Unlocked, Access(level: 50).StateOf("accessories", "rose-gold-collar").State);
    }

    [Fact]
    public void Rose_ribbon_opens_on_the_first_achievement()
    {
        var none = Access().StateOf("accessories", "hair-bow");
        Assert.Equal(RainItemState.Locked, none.State);
        Assert.Equal(RainItemManifest.AnyAchievement, none.NeedsAchievement);
        Assert.Equal(RainItemState.Unlocked, Access(achievements: new[] { "first_week_graduate" }).StateOf("accessories", "hair-bow").State);
    }

    [Fact]
    public void Slot_qualified_ids_resolve_per_slot()
    {
        var a = Access();
        Assert.Equal("arms", a.StateOf("arms", "fine-gold").Item!.Slot);
        Assert.Equal("ankles", a.StateOf("ankles", "fine-gold").Item!.Slot);
        Assert.Equal(RainItemState.Free, a.StateOf("arms", "forearm-wrap").State);
        Assert.Equal(RainItemState.Unknown, a.StateOf("ankles", "forearm-wrap").State); // an arms-only piece
        Assert.Equal(RainItemState.Unknown, a.StateOf("dress", "fine-gold").State);
    }
    [Fact]
    public void Clamp_sends_unowned_and_locked_pieces_back_to_the_slot_default()
    {
        const string look = "{\"rev\":1,\"items\":{\"dress\":\"midnight-cocktail\",\"hair\":\"copper-lob\",\"skin\":\"warm\"," +
            "\"arms\":\"fine-gold\",\"accessories\":[\"velvet-choker\",\"silver-drops\",\"glasses\",\"glasses\"]},\"colors\":{\"fabric\":\"#112233\"}}";
        var access = Access(level: 12, grants: "cos.rain.hair.copper-lob");
        var clamped = JObject.Parse(RainLookClamp.Clamp(look, access));
        var items = (JObject)clamped["items"]!;

        Assert.Null(items["dress"]);                                // not owned: default dress
        Assert.Equal("copper-lob", (string)items["hair"]!);         // owned
        Assert.Equal("warm", (string)items["skin"]!);               // free
        Assert.Equal("fine-gold", (string)items["arms"]!);
        Assert.Equal(new[] { "velvet-choker", "glasses" }, items["accessories"]!.Select(t => (string)t!));  // Lv 12 choker, no unowned drops, no repeat
        Assert.Equal("#112233", (string)clamped["colors"]!["fabric"]!);

        Assert.Equal(new[] { "dress:midnight-cocktail", "accessories:silver-drops" }, RainLookClamp.Blockers(look, access));
    }

    [Fact]
    public void Clamp_drops_unknown_ids_and_slots_and_keeps_an_empty_accessory_list()
    {
        var json = RainLookClamp.Clamp(
            "{\"rev\":1,\"items\":{\"dress\":\"made-up\",\"cape\":\"red\",\"ankles\":\"forearm-wrap\",\"accessories\":[\"nope\"]}}",
            Access());
        Assert.Equal("{\"rev\":1,\"items\":{\"accessories\":[]},\"colors\":{}}", json);
        Assert.Equal("", RainLookClamp.Clamp("not json", Access()));
        Assert.Equal("", RainLookClamp.Clamp("{\"rev\":2,\"items\":{\"dress\":\"satin-original\"}}", Access()));
    }

    [Fact]
    public void Wardrobe_window_clamp_is_this_clamp_and_honours_level_unlocks()
    {
        const string look = "{\"rev\":1,\"items\":{\"accessories\":[\"midnight-ribbon\",\"rose-gold-collar\"]}}";
        Assert.Equal(RainLookClamp.Clamp(look, Access(level: 30)), RainWardrobeCatalog.Clamp(look, null, new Progress { Level = 30 }));
        Assert.Contains("accessories:rose-gold-collar", RainWardrobeCatalog.LockedKeys(null, new Progress { Level = 30 }));
        Assert.DoesNotContain("accessories:midnight-ribbon", RainWardrobeCatalog.LockedKeys(null, new Progress { Level = 30 }));
        // The two later hair drops stay locked until the counter sells them.
        Assert.True(RainWardrobeCatalog.IsLocked("hair", "honey-roundbob", null, new Progress { Level = 200 }));
        Assert.True(RainWardrobeCatalog.IsLocked("hair", "espresso-sidesweep", null, new Progress { Level = 200 }));
    }

    [Fact]
    public void Missing_manifest_wears_nothing_but_the_defaults()
    {
        var a = new RainItemAccess(RainItemManifest.Load(Path.Combine(Path.GetTempPath(), "no-such-rain-items.json")), new RainGrantSet(null), new Progress());
        Assert.Empty(a.Manifest.Items);
        Assert.Equal("", RainLookClamp.Clamp("{\"rev\":1,\"items\":{\"skin\":\"warm\"}}", a));
    }
}
