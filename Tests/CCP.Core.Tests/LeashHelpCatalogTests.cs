using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The holder's "?" slip catalog (WPF main 325115b21, moved to Core by sync7-leash-b): every holder
/// command and sheet row has a slip, each stamp is the lowest level the grammar allows, and every key is in all
/// ten languages. The WPF suite's LeashHelpSlipTests keeps the same checks on the WPF head.</summary>
public sealed class LeashHelpCatalogTests
{
    [Fact]
    public void EveryHolderButtonLetGoAndSheetRowHasASlip()
    {
        foreach (var id in LeashHelpCatalog.HolderButtons.Append(LeashHelpCatalog.LetGoId))
            Assert.NotNull(LeashHelpCatalog.Get(id));
        Assert.Equal(LeashHelpCatalog.All.Count, LeashHelpCatalog.All.Select(e => e.Id).Distinct().Count());
        foreach (var k in Enum.GetValues<PunishKind>()) Assert.Equal(LeashHelpCatalog.Id(k), LeashHelpCatalog.ForPunish(k).Id);
        foreach (var k in Enum.GetValues<AssignKind>()) Assert.Equal("assign", LeashHelpCatalog.ForAssign(k).Id);
        Assert.Equal(new[] { "credit", "pardon", "praise", "sticker" },
            Enum.GetValues<RewardKind>().Select(k => LeashHelpCatalog.ForReward(k).Id).OrderBy(x => x));
    }

    [Fact]
    public void StampsMatchTheGrammar()
    {
        foreach (var k in Enum.GetValues<PunishKind>())
            Assert.Equal(LeashGrammar.LowestIntensity(k), LeashHelpCatalog.ForPunish(k).Stamp);
        Assert.Equal(Enum.GetValues<PunishKind>().Select(LeashGrammar.LowestIntensity).Min(), LeashHelpCatalog.Get("punish")!.Stamp);
        foreach (var id in new[] { "assign", "tug", "letgo", "reward", "sticker", "praise", "pardon", "credit" })
            Assert.Null(LeashHelpCatalog.Get(id)!.Stamp);
        Assert.Equal(LeashIntensity.Strict, LeashHelpCatalog.ForPunish(PunishKind.Chaster).Stamp);
    }

    [Fact]
    public void StampAndYouLabelsReadLikeTheMockup()
    {
        string Loc(string k) => k switch
        {
            "leash_slip_any" => "ANY LEVEL",
            "leash_level_soft" => "Soft",
            "leash_level_standard" => "Standard",
            "leash_level_strict" => "Strict",
            _ => k,
        };
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal("ANY LEVEL", LeashHelpCatalog.StampText(null, Loc, inv));
        Assert.Equal("SOFT+", LeashHelpCatalog.StampText(LeashIntensity.Soft, Loc, inv));
        Assert.Equal("STANDARD+", LeashHelpCatalog.StampText(LeashIntensity.Standard, Loc, inv));
        Assert.Equal("STRICT", LeashHelpCatalog.StampText(LeashIntensity.Strict, Loc, inv));
        Assert.Equal("+30 min", LeashHelpCatalog.YouLabel(LeashHelpCatalog.ForPunish(PunishKind.Chaster), _ => "{0} min"));
        Assert.Equal("-30 min", LeashHelpCatalog.YouLabel(LeashHelpCatalog.ForReward(RewardKind.Credit), _ => "{0} min"));
        Assert.Equal("100", LeashHelpCatalog.YouLabel(LeashHelpCatalog.ForPunish(PunishKind.Bubbles), k => k));
    }

    [Fact]
    public void EverySlipKeyIsInAllTenLanguages()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "CCP.Core", "Localization"))) d = d.Parent;
        var dir = Path.Combine(d!.FullName, "CCP.Core", "Localization", "Languages");
        var keys = LeashHelpCatalog.AllKeys().Distinct().ToList();
        Assert.True(keys.Count > 40, "found only " + keys.Count);
        foreach (var lang in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, lang + ".json")));
            foreach (var k in keys) Assert.True(json[k] != null, $"{lang}.json is missing {k}");
            foreach (var e in LeashHelpCatalog.All)
                Assert.Equal(e.CaptionNamesThem, ((string)json[e.CaptionKey]!).Contains("{0}"));
            // WPF Slip_copy_says_what_the_player_sees: the slips name what the player sees, never "the gate".
            if (lang == "en")
                foreach (var e in LeashHelpCatalog.All)
                    foreach (var k in new[] { e.CaptionKey, e.LimitsKey })
                        Assert.DoesNotContain("gate", ((string)json[k]!).ToLowerInvariant());
        }
    }
}
