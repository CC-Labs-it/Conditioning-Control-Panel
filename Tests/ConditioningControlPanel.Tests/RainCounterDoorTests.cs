using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.PrizeCounter;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Rain V2 lane R11: the two doors between the Prize Counter and Rain's dressing room. Startup wires the
/// wardrobe's seam to the counter, the counter caches Rain prices from each shop-state, and an owned
/// card's Wear on a Rain row opens the dressing room with that piece tried on (anything else is refused).
/// </summary>
[Collection(GameSurfacesCollection.Name)]
public class RainCounterDoorTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Web(params string[] parts)
        => Path.Combine(RepoRoot(), "Assets", "web", Path.Combine(parts));

    private static RainItemManifest Shipped() => RainItemManifest.Load(Web("rain-wardrobe", RainItemManifest.FileName));

    [Fact]
    public void Startup_WiresTheWardrobeSeamToTheCounter()
    {
        var launcher = RainWardrobeHostService.CounterLauncher;
        var prices = RainWardrobeHostService.CounterPrices;
        try
        {
            RainWardrobeHostService.CounterLauncher = null;
            RainWardrobeHostService.CounterPrices = null;
            PrizeCounterHostService.WireRainWardrobe();
            Assert.NotNull(RainWardrobeHostService.CounterLauncher);
            Assert.NotNull(RainWardrobeHostService.CounterPrices);
        }
        finally
        {
            RainWardrobeHostService.CounterLauncher = launcher;
            RainWardrobeHostService.CounterPrices = prices;
        }
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "App.xaml.cs"));
        Assert.Contains("PrizeCounterHostService.WireRainWardrobe();", app);
    }

    [Theory]
    [InlineData("rain.dress.midnight-cocktail", "dress:midnight-cocktail")]
    [InlineData("rain.suit.hazmat-yellow", "dress:hazmat-yellow")]
    [InlineData("rain.hair.rose-waves", "hair:rose-waves")]
    [InlineData("rain.acc.cat-eye-frames", "accessories:cat-eye-frames")]
    public void ACounterRainRow_WearsItsWardrobeKey(string rowId, string key)
        => Assert.Equal(key, PrizeCounterHostService.WearKey(rowId, Shipped()));

    [Fact]
    public void AWearFrame_WithAKnownRainId_OpensTheWardrobeWithThatTryOn()
    {
        var opened = new List<string>();
        Assert.True(PrizeCounterHostService.HandleWear(
            new JObject { ["type"] = "wear", ["id"] = "rain.dress.rose-flare" }, Shipped(), opened.Add));
        Assert.Equal(new[] { "dress:rose-flare" }, opened);
    }

    [Theory]
    [InlineData("kart.outfit.pit-crew")]
    [InlineData("rain.dress.no-such-dress")]
    [InlineData("cos.rain.dress.rose-flare")]
    [InlineData("")]
    public void AWearFrame_WithAnUnknownId_IsRefused(string id)
    {
        var opened = new List<string>();
        Assert.False(PrizeCounterHostService.HandleWear(new JObject { ["type"] = "wear", ["id"] = id }, Shipped(), opened.Add));
        Assert.False(PrizeCounterHostService.HandleWear(new JObject { ["type"] = "wear", ["id"] = 7 }, Shipped(), opened.Add));
        Assert.False(PrizeCounterHostService.HandleWear(new JObject { ["type"] = "wear" }, Shipped(), opened.Add));
        Assert.Empty(opened);
    }

    [Fact]
    public void TheWardrobe_TriesOnOnlyItemsJsonKeys()
    {
        var m = Shipped();
        Assert.Equal("dress:midnight-cocktail", RainWardrobeHostService.TryOnKey("dress:midnight-cocktail", m));
        Assert.Equal("accessories:hair-bow", RainWardrobeHostService.TryOnKey("accessories:hair-bow", m));
        Assert.Null(RainWardrobeHostService.TryOnKey("dress:no-such-dress", m));
        Assert.Null(RainWardrobeHostService.TryOnKey("rain.dress.midnight-cocktail", m));
        Assert.Null(RainWardrobeHostService.TryOnKey(":", m));
        Assert.Null(RainWardrobeHostService.TryOnKey(null, m));
    }

    [Fact]
    public void ShopState_YieldsRainPricesByGrant_AndARefusalsFreshStateToo()
    {
        var state = JObject.Parse(File.ReadAllText(Web("prizecounter", "mock", "state.sample.json")));
        var prices = PrizeCounterHostService.RainPricesFrom(state);
        Assert.Equal(730, prices["cos.rain.dress.midnight-cocktail"]);
        Assert.Equal(360, prices["cos.rain.hair.rose-waves"]);
        Assert.All(prices.Keys, k => Assert.StartsWith("cos.rain.", k));

        var refusal = new JObject { ["ok"] = false, ["reason"] = "stock_changed", ["state"] = state };
        Assert.Equal(prices.Count, PrizeCounterHostService.RainPricesFrom(refusal).Count);
        Assert.Empty(PrizeCounterHostService.RainPricesFrom(new JObject { ["ok"] = false, ["reason"] = "offline" }));
        Assert.Empty(PrizeCounterHostService.RainPricesFrom(null));
    }

    [Fact]
    public void TheCounterHost_RoutesWearFrames_AndClosesBeforeTheWardrobeOpens()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.cs"));
        Assert.True(src.IndexOf("case \"wear\":", StringComparison.Ordinal) > 0);
        Assert.Contains("RouteWear(wearId, from)", src);
        // Fix wave F1: the frame routes by family; the Rain branch still closes before the wardrobe opens.
        int wear = src.IndexOf("case FamilyRain:", StringComparison.Ordinal);
        Assert.True(wear > 0);
        int close = src.IndexOf("CloseFor(CloseWear);", wear, StringComparison.Ordinal);
        int open = src.IndexOf("OpenWardrobe(key)", wear, StringComparison.Ordinal);
        Assert.True(close > wear && open > close, "the counter closes first: never two WebView2 windows fighting focus");
    }
}
