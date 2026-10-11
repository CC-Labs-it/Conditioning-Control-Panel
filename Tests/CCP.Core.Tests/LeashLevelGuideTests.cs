using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace CCP.Core.Tests;

/// <summary>The words next to the leash level switch (Soft, Standard, Strict) are built from the
/// grammar. These pin the guide to the rules it explains, and to the figures the players were
/// told on 2026-10-10, so a change to either shows up here first.</summary>
public sealed class LeashLevelGuideTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static JObject Lang(string lang)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Core", "Localization", "Languages")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return JObject.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "CCP.Core", "Localization", "Languages", lang + ".json")));
    }

    [Fact]
    public void EachLevel_AddsWhatTheGrammarFirstAllowsThere()
    {
        Assert.Equal(new[] { PunishKind.Lines, PunishKind.Pink }, LeashLevelGuide.Adds(LeashIntensity.Soft));
        Assert.Equal(new[] { PunishKind.Bubbles, PunishKind.Detention, PunishKind.Video }, LeashLevelGuide.Adds(LeashIntensity.Standard));
        Assert.Equal(new[] { PunishKind.Chaster }, LeashLevelGuide.Adds(LeashIntensity.Strict));

        // Every kind the wire knows sits in exactly one level, and the guide lists them as the sheet does.
        var all = Enum.GetValues<PunishKind>();
        Assert.Equal(all.OrderBy(k => k), LeashLevelGuide.Levels.SelectMany(LeashLevelGuide.Adds).OrderBy(k => k));
        Assert.Equal(LeashUiRules.PunishOrder, LeashLevelGuide.Order);
        foreach (var k in all)
            Assert.Contains(k, LeashLevelGuide.Adds(LeashGrammar.LowestIntensity(k)));
    }

    [Fact]
    public void EachLevel_KeepsEverythingFromTheOneBefore()
    {
        foreach (var level in LeashLevelGuide.Levels)
        {
            var allows = LeashLevelGuide.Allows(level);
            foreach (var k in Enum.GetValues<PunishKind>())
            {
                Assert.Equal(LeashGrammar.Allowed(k, level), allows.Contains(k));
                Assert.Equal(LeashUiRules.Allowed(k, level), allows.Contains(k));
            }
        }
        Assert.Equal(2, LeashLevelGuide.Allows(LeashIntensity.Soft).Count);
        Assert.Equal(5, LeashLevelGuide.Allows(LeashIntensity.Standard).Count);
        Assert.Equal(6, LeashLevelGuide.Allows(LeashIntensity.Strict).Count);
    }

    [Fact]
    public void Figures_AreTheGrammarsOwn()
    {
        Assert.Equal(new[] { 3, 5, 10 }, LeashLevelGuide.Figures(PunishKind.Lines));
        Assert.Equal(new[] { 10, 15, 20 }, LeashLevelGuide.Figures(PunishKind.Pink));
        Assert.Equal(new[] { 50, 100, 200 }, LeashLevelGuide.Figures(PunishKind.Bubbles));
        Assert.Equal(new[] { 10, 20, 30 }, LeashLevelGuide.Figures(PunishKind.Detention));
        Assert.Equal(new[] { 15, 30, 60 }, LeashLevelGuide.Figures(PunishKind.Chaster));   // minutes; 900/1800/3600 s on the wire
        Assert.Equal(new[] { LeashVideoCap.Max }, LeashLevelGuide.Figures(PunishKind.Video));

        foreach (var k in new[] { PunishKind.Lines, PunishKind.Pink, PunishKind.Bubbles, PunishKind.Detention })
        {
            Assert.Equal(LeashGrammar.PunishSizes(k), LeashLevelGuide.Figures(k));
            Assert.Equal(LeashUiRules.Sizes(k), LeashLevelGuide.Figures(k));
        }
        Assert.Equal(LeashGrammar.PunishSizes(PunishKind.Chaster).Select(s => s / 60), LeashLevelGuide.Figures(PunishKind.Chaster));

        Assert.Equal(3, LeashLevelGuide.PunishmentsPerDay);
        Assert.Equal(72, LeashLevelGuide.ExpiresAfterHours);
    }

    [Fact]
    public void Summary_NamesWhatTheLevelAdds()
    {
        Func<string, string> key = k => k;
        Assert.Equal("leash_pun_lines, leash_pun_pink", LeashLevelGuide.Summary(LeashIntensity.Soft, key));
        Assert.Equal("+ leash_pun_bubbles, leash_pun_detention, leash_pun_video", LeashLevelGuide.Summary(LeashIntensity.Standard, key));
        Assert.Equal("+ leash_pun_chaster", LeashLevelGuide.Summary(LeashIntensity.Strict, key));
    }

    [Fact]
    public void EveryLanguage_CarriesTheGuide_AndEveryLineTakesItsFigures()
    {
        var keys = LeashLevelGuide.AllKeys().Distinct().ToList();
        foreach (var lang in Languages)
        {
            var json = Lang(lang);
            foreach (var k in keys)
            {
                var text = (string?)json[k];
                Assert.False(string.IsNullOrWhiteSpace(text), lang + " lacks " + k);
                // Copy voice: no exclamation marks, no em or en dashes, in any language.
                Assert.DoesNotContain("!", text);
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
            }
            string Loc(string k) => (string)json[k]!;
            foreach (var kind in LeashLevelGuide.Order)
            {
                var row = LeashLevelGuide.Row(kind);
                var line = LeashLevelGuide.Detail(row, Loc);
                foreach (var n in row.Figures) Assert.Contains(n.ToString(), line);
                Assert.DoesNotContain("{", line);
            }
            var limits = string.Format(Loc(LeashLevelGuide.LimitsKey), LeashLevelGuide.PunishmentsPerDay, LeashLevelGuide.ExpiresAfterHours);
            Assert.Contains("3", limits);
            Assert.Contains("72", limits);
            Assert.DoesNotContain("{", limits);
        }
    }
}
