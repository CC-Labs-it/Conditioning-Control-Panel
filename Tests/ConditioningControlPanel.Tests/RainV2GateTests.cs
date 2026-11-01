using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.PrizeCounter;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner 2026-10-30: Rain V2 is postponed out of 7.2.0 behind ONE release constant (RainV2Gate). With the
/// gate off: no V1 | V2 pill, a saved "v2" plays V1 and is kept, the counter host hides Rain rows and
/// refuses their Buy and Wear, and no dressing-room door opens. The gated helpers take the gate as a
/// parameter, so these tests pin both states without touching the constant.
/// </summary>
[Collection(GameSurfacesCollection.Name)]
public class RainV2GateTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Src(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", Path.Combine(parts)));

    private static JObject SampleState() => JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(),
        "ConditioningControlPanel", "Resources", "web", "prizecounter", "mock", "state.sample.json")));

    [Fact]
    public void TheGate_IsOneReleaseConstant()
        => Assert.Equal(RainV2Gate.Enabled, RainV2Gate.On);

    [Fact]
    public void GateOff_ThePillNeverShows()
    {
        Assert.False(AvatarGenerationRule.PillVisible(false, modHasV2Row: true, portraitMode: false, glitchUp: false));
        Assert.True(AvatarGenerationRule.PillVisible(true, modHasV2Row: true, portraitMode: false, glitchUp: false));
        var src = Src("AvatarTube", "AvatarTubeWindow.GenerationPill.cs");
        Assert.Contains("AvatarGenerationRule.PillVisible(RainV2Gate.On,", src);
        Assert.Contains("bool hasRow = RainV2Gate.On", src);
    }

    [Fact]
    public void GateOff_ASavedV2PlaysV1_AndTheSettingIsKept()
    {
        Assert.Equal(AvatarGenerationRule.V1, AvatarGenerationRule.Effective("v2", v2On: false));
        Assert.Equal(AvatarGenerationRule.V2, AvatarGenerationRule.Effective("v2", v2On: true));
        Assert.Equal(AvatarGenerationRule.V1, AvatarGenerationRule.Effective(null, v2On: true));
        Assert.Equal(RainV2Gate.On ? "v2" : "v1", AvatarGenerationRule.Effective("v2"));

        // The tube resolves through Effective; the saved field is never written outside the pill's click.
        var s = new ConditioningControlPanel.Models.AppSettings { AvatarGeneration = "v2" };
        Assert.Equal(AvatarGenerationRule.V1, AvatarGenerationRule.Effective(s.AvatarGeneration, v2On: false));
        Assert.Equal("v2", s.AvatarGeneration);
        Assert.Contains("AvatarGenerationRule.Effective(App.Settings?.Current?.AvatarGeneration)",
            Src("AvatarTube", "AvatarTubeWindow.CirceEmotes.cs"));
        Assert.Equal("rain_emotes_v1", RainPackPlayback.ResolveTubeFolder(
            AvatarGenerationRule.Effective("v2", v2On: false), "rain_emotes", "abc", _ => null, () => "rain_emotes_v1"));
    }

    [Fact]
    public void GateOff_TheCounterHostHidesRainTabsAndRows()
    {
        var state = SampleState();
        state["tabs"]!.OfType<JObject>().First(t => (string?)t["id"] == "profile")["shelves"]![0]!["rows"]!
            .Value<JArray>()!.Add(new JObject { ["id"] = "rain.stray.piece", ["priceSp"] = 1 });

        var hidden = PrizeCounterHostService.HideRain(state, rainOn: false)!;
        var ids = hidden["tabs"]!.Select(t => (string?)t["id"]).ToList();
        Assert.DoesNotContain("rain-clothes", ids);
        Assert.DoesNotContain("rain-hair", ids);
        Assert.Contains("kart", ids);
        Assert.Empty(hidden.SelectTokens("tabs[*].shelves[*].rows[*].id").Select(t => (string?)t)
            .Where(id => id != null && id.StartsWith("rain.", StringComparison.Ordinal)));
        Assert.Empty(PrizeCounterHostService.RainPricesFrom(hidden));
        // A refusal's fresh state is filtered the same way; the source body is never changed.
        var refusal = PrizeCounterHostService.HideRain(new JObject { ["ok"] = false, ["reason"] = "stock_changed", ["state"] = SampleState() }, false)!;
        Assert.DoesNotContain("rain-clothes", refusal["state"]!["tabs"]!.Select(t => (string?)t["id"]));
        Assert.Contains("rain-clothes", state["tabs"]!.Select(t => (string?)t["id"]));

        Assert.Same(state, PrizeCounterHostService.HideRain(state, rainOn: true));
        Assert.Null(PrizeCounterHostService.HideRain(null, rainOn: false));

        var src = Src("Services", "PrizeCounter", "PrizeCounterHostService.cs");
        Assert.Contains("reply = HideRain(reply, rainOn);", src);
        Assert.Contains("!Companion.RainV2Gate.On && IsRainId(_tab) ? null : _tab", src);
    }

    [Fact]
    public void GateOff_EmptyRainTabsFromTheServerAreHiddenWhole_AndThePortraitStays()
    {
        // CCP-Server #382 with SHOP_RAIN unset: both Rain tabs still come, empty or with an owner's stamped row.
        var body = new JObject
        {
            ["ok"] = true,
            ["tabs"] = new JArray
            {
                new JObject { ["id"] = "rain-clothes", ["shelves"] = new JArray() },
                new JObject { ["id"] = "rain-hair", ["shelves"] = new JArray { new JObject { ["rows"] = new JArray {
                    new JObject { ["id"] = "rain.hair.rose-waves", ["owned"] = new JObject { ["at"] = "2026-10-30", ["paidSp"] = 360 } } } } } },
                new JObject { ["id"] = "profile", ["shelves"] = new JArray { new JObject { ["rows"] = new JArray {
                    new JObject { ["id"] = "profile.avatar.rain-portrait", ["priceSp"] = 200 } } } } },
            },
        };
        var hidden = PrizeCounterHostService.HideRain(body, rainOn: false)!;
        Assert.Equal(new[] { "profile" }, hidden["tabs"]!.Select(t => (string?)t["id"]));
        Assert.Equal("profile.avatar.rain-portrait", (string?)hidden.SelectToken("tabs[0].shelves[0].rows[0].id"));
    }

    [Fact]
    public void GateOff_RainBuyAndWearAreRefused()
    {
        var buy = new JObject { ["itemId"] = "rain.dress.rose-flare" };
        Assert.True(PrizeCounterHostService.RefusesBuy(PrizeCounterRelay.OpBuy, buy, rainOn: false));
        Assert.False(PrizeCounterHostService.RefusesBuy(PrizeCounterRelay.OpBuy, buy, rainOn: true));
        Assert.False(PrizeCounterHostService.RefusesBuy(PrizeCounterRelay.OpBuy, new JObject { ["itemId"] = "kart.outfit.ballerina" }, false));
        Assert.False(PrizeCounterHostService.RefusesBuy(PrizeCounterRelay.OpState, null, false));

        Assert.DoesNotContain(PrizeCounterHostService.FamilyRain, PrizeCounterHostService.WearsFor("wallet", rainOn: false));
        Assert.DoesNotContain(PrizeCounterHostService.FamilyRain, PrizeCounterHostService.WearsFor("glassroad", rainOn: false));
        Assert.Contains(PrizeCounterHostService.FamilyRain, PrizeCounterHostService.WearsFor("wallet", rainOn: true));

        if (!RainV2Gate.On)
        {
            var opened = PrizeCounterHostService.OpenWardrobe;
            bool called = false;
            try
            {
                PrizeCounterHostService.OpenWardrobe = _ => called = true;
                Assert.Equal("refused", PrizeCounterHostService.RouteWear("rain.dress.rose-flare", "wallet"));
                Assert.False(called);
            }
            finally { PrizeCounterHostService.OpenWardrobe = opened; }
        }
    }

    [Fact]
    public void GateOff_NoDressingRoomDoor()
    {
        // The backstop: Open() returns before the asset install card or the window.
        var host = Src("Services", "Companion", "RainWardrobeHostService.cs");
        int open = host.IndexOf("public static void Open()", StringComparison.Ordinal);
        int gate = host.IndexOf("if (!RainV2Gate.On)", open, StringComparison.Ordinal);
        int install = host.IndexOf("RainAssetInstallWindow.ShowFor", open, StringComparison.Ordinal);
        Assert.True(open > 0 && gate > open && install > gate, "the gate comes first in Open()");

        Assert.Contains("_circeEmoteMode && Services.Companion.RainV2Gate.On", Src("AvatarTube", "AvatarTubeWindow.CirceEmotes.cs"));
        Assert.Contains("IsRainWardrobeAvailable = Services.Companion.RainV2Gate.On &&",
            Src("Views", "Controls", "Companion", "Runtime", "CompanionHeroRuntimeVm.cs"));
        Assert.Contains("if (!Services.Companion.RainV2Gate.On) HelpRainRow.Visibility = Visibility.Collapsed;",
            Src("MainWindow", "MainWindow.Presets.cs"));
        Assert.Contains("if (RainV2Gate.On && RebakeOwed(", Src("Services", "Companion", "RainLookSync.cs"));
    }

    [Fact]
    public void GateOff_TheSignedOutWindowHasNoRainArt()
    {
        // The counter hides the Rain bay and the portrait unless the host says otherwise.
        Assert.Contains("rainV2 = Companion.RainV2Gate.On,", Src("Services", "PrizeCounter", "PrizeCounterHostService.cs"));
        Assert.Contains("windowShelves(scene.items, { rain: init.rainV2 === true })", Src("Resources", "web", "prizecounter", "main.js"));
    }

    [Fact]
    public void GateOff_TheEmiBookHasNoRainPage()
    {
        Assert.DoesNotContain(EmiBookDeckShop.Shipping(false), c => c.Id == "rain");
        Assert.Contains(EmiBookDeckShop.Shipping(false), c => c.Id == "prize-counter");
        Assert.Contains(EmiBookDeckShop.Shipping(true), c => c.Id == "rain");
        Assert.Equal(RainV2Gate.On, EmiBookCards.All.Any(c => c.Id == "rain"));
    }
}
