using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Rain's wardrobe on WPF (7.2 preview, lane R0): the stored look (RainLookCodec), the Prize Counter
/// hook (only owned sellable items can be worn, single items only), the page URL the window opens,
/// the big-asset folder rule, and the panic entry.
/// </summary>
[Collection(GameSurfacesCollection.Name)]
public class RainWardrobeTests
{
    private static string RepoRoot()
    {
        return SourceRoots.RepoRoot;
    }

    private const string Look =
        "{\"rev\":1,\"items\":{\"dress\":\"rose-flare\",\"hair\":\"original-blonde\",\"shoes\":\"original-boots\"," +
        "\"accessories\":[\"hoops-original\",\"silver-drops\"]},\"colors\":{\"fabric\":\"#AA3366\"}}";

    [Fact]
    public void Codec_IsCanonical_AndDropsJunk()
    {
        var n = RainLookCodec.Normalize(Look);
        var o = JObject.Parse(n);
        Assert.Equal(1, (int)o["rev"]!);
        Assert.Equal("#aa3366", (string)o["colors"]!["fabric"]!);
        Assert.Equal(new[] { "hoops-original", "silver-drops" }, o["items"]!["accessories"]!.Select(t => (string)t!));
        Assert.Equal("", RainLookCodec.Normalize(null));
        Assert.Equal("", RainLookCodec.Normalize("not json"));
        Assert.Equal("", RainLookCodec.Normalize("{\"rev\":2,\"items\":{\"dress\":\"x\"}}"));
        Assert.Equal("", RainLookCodec.Normalize("{\"rev\":1,\"items\":{},\"colors\":{\"nope\":\"#ffffff\"}}"));
        Assert.True(RainLookCodec.IsDefault(""));
        Assert.Equal(n, RainLookCodec.Normalize(n));
    }

    [Fact]
    public void Catalog_LocksEverySellableItem_UntilItsGrant()
    {
        var none = RainWardrobeCatalog.LockedKeys(null);
        Assert.Equal(RainWardrobeCatalog.Items.Count + 4, none.Count);      // + the four level / achievement unlocks (items.json)
        Assert.Contains("dress:rose-flare", none);
        Assert.Contains("dress:hazmat-yellow", none);           // rain.suit.* sits in the dress slot
        Assert.Contains("accessories:silver-drops", none);
        Assert.Contains("hair:ink-sleek", none);
        // Rain's authored default look and the unsold slots stay free.
        foreach (var free in new[] { "dress:satin-original", "hair:original-blonde", "accessories:hoops-original", "shoes:original-boots", "skin:original" })
            Assert.DoesNotContain(free, none);

        var some = RainWardrobeCatalog.LockedKeys(new[] { "cos.rain.dress.rose-flare", "cos.rain.acc.silver-drops", "backroom.x" });
        Assert.DoesNotContain("dress:rose-flare", some);
        Assert.DoesNotContain("accessories:silver-drops", some);
        Assert.Contains("dress:midnight-cocktail", some);
        Assert.True(RainWardrobeCatalog.IsLocked("hair", "ink-sleek", null));
        Assert.False(RainWardrobeCatalog.IsLocked("hair", "ink-sleek", new[] { "cos.rain.hair.ink-sleek" }));
    }

    [Fact]
    public void Catalog_GrantsMatchTheShopIdShape()
    {
        foreach (var item in RainWardrobeCatalog.Items)
        {
            Assert.Matches("^rain\\.(dress|suit|hair|acc)\\.[a-z0-9-]+$", item.ShopId);
            Assert.Equal("cos." + item.ShopId, item.Grant);
            Assert.EndsWith("." + item.WardrobeId, item.ShopId);
        }
        Assert.Equal(RainWardrobeCatalog.Items.Count, RainWardrobeCatalog.Items.Select(i => i.ShopId).Distinct().Count());
    }

    [Fact]
    public void Clamp_TakesOffWhatIsNotOwned_KeepsTheRest()
    {
        var clamped = JObject.Parse(RainWardrobeCatalog.Clamp(Look, grants: null));
        Assert.Null(clamped["items"]!["dress"]);                         // rose-flare not owned: back to default
        Assert.Equal("original-blonde", (string)clamped["items"]!["hair"]!);
        Assert.Equal(new[] { "hoops-original" }, clamped["items"]!["accessories"]!.Select(t => (string)t!));
        Assert.Equal("#aa3366", (string)clamped["colors"]!["fabric"]!);  // colours are free

        var owned = JObject.Parse(RainWardrobeCatalog.Clamp(Look, new[] { "cos.rain.dress.rose-flare", "cos.rain.acc.silver-drops" }));
        Assert.Equal("rose-flare", (string)owned["items"]!["dress"]!);
        Assert.Equal(2, owned["items"]!["accessories"]!.Count());
    }

    [Fact]
    public void PageUrl_IsHostMode_WithLocksAndTheLockLine_NeverAStudio()
    {
        var url = new Uri(RainWardrobeHostService.PageUrl(null, null, "In the Prize Counter"));
        Assert.Equal("ccp.game", url.Host);
        Assert.Equal("/rain-wardrobe/index.html", url.AbsolutePath);
        var q = System.Web.HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("ccp", q["host"]);
        Assert.Null(q["look"]);                                     // default look: omitted
        Assert.Contains("dress:rose-flare", q["locked"]!.Split(','));
        Assert.Equal("In the Prize Counter", q["lockNote"]);
        Assert.Null(q["review"]);
        Assert.Null(q["qa"]);

        var withLook = System.Web.HttpUtility.ParseQueryString(new Uri(RainWardrobeHostService.PageUrl(Look, null, "x")).Query);
        Assert.DoesNotContain("rose-flare", withLook["look"]!);    // clamped before it goes in
    }

    [Fact]
    public void BigAssets_ComeFromTheEnvFolderElseUserData()
    {
        Assert.Equal(Path.Combine(@"C:\ud", "rain-assets"), RainWardrobeAssets.ResolveDir(null, @"C:\ud"));
        Assert.Equal(Path.Combine(@"C:\ud", "rain-assets"), RainWardrobeAssets.ResolveDir("  ", @"C:\ud"));
        Assert.Equal(@"D:\rain", RainWardrobeAssets.ResolveDir(@"D:\rain", @"C:\ud"));
        Assert.Equal(RainWardrobeAssets.Files, RainWardrobeAssets.Missing(null));
        Assert.Equal(RainWardrobeAssets.Files, RainWardrobeAssets.Missing(@"C:\no\such\folder"));
    }

    [Fact]
    public void PageAndHost_AgreeOnTheBigAssetHost()
    {
        var bridge = File.ReadAllText(Path.Combine(RepoRoot(), "Assets", "web", "rain-wardrobe", "host-bridge.js"));
        Assert.Contains($"'https://{RainWardrobeHostService.AssetsHost}/'", bridge);
        foreach (var f in RainWardrobeAssets.Files) Assert.True(f == "avatar.glb" || f == "pelvis-data.js");
    }

    [Fact]
    public void Panic_ClosesTheWardrobe()
        => Assert.Contains("rain-wardrobe", GameSurfaces.Ids());

    [Fact]
    public void RainFolder_IsRecognisedByNameOrPath()
    {
        Assert.True(AvatarTubeWindow.IsRainFolder("rain_emotes"));
        Assert.True(AvatarTubeWindow.IsRainFolder(@"C:\packs\rain_emotes\"));
        Assert.False(AvatarTubeWindow.IsRainFolder("avatar0_emotes"));
        Assert.False(AvatarTubeWindow.IsRainFolder(null));
    }
}
