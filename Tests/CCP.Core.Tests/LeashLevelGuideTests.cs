using System;
using System.Linq;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Core LeashLevelGuide (moved from the WPF head, main a12bb6c53): the words next to the leash level
/// switch are built from the grammar. Pins the guide to the rules it explains and to the figures the players
/// were told on 2026-10-10. The WPF suite's LeashLevelGuideTests keeps the 9-language check and its UI facts.</summary>
public sealed class LeashLevelGuideTests
{
    [Fact]
    public void EachLevel_AddsWhatTheGrammarFirstAllowsThere()
    {
        Assert.Equal(new[] { PunishKind.Lines, PunishKind.Pink }, LeashLevelGuide.Adds(LeashIntensity.Soft));
        Assert.Equal(new[] { PunishKind.Bubbles, PunishKind.Detention, PunishKind.Video }, LeashLevelGuide.Adds(LeashIntensity.Standard));
        Assert.Equal(new[] { PunishKind.Chaster }, LeashLevelGuide.Adds(LeashIntensity.Strict));

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
                Assert.Equal(LeashGrammar.Allowed(k, level), allows.Contains(k));
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
        Assert.Equal(3, LeashLevelGuide.PunishmentsPerDay);
        Assert.Equal(72, LeashLevelGuide.ExpiresAfterHours);
    }

    [Fact]
    public void Summary_NamesWhatTheLevelAdds_AndDetailFillsTheFigures()
    {
        Func<string, string> key = k => k;
        Assert.Equal("leash_pun_lines, leash_pun_pink", LeashLevelGuide.Summary(LeashIntensity.Soft, key));
        Assert.Equal("+ leash_pun_bubbles, leash_pun_detention, leash_pun_video", LeashLevelGuide.Summary(LeashIntensity.Standard, key));
        Assert.Equal("+ leash_pun_chaster", LeashLevelGuide.Summary(LeashIntensity.Strict, key));
        Assert.Equal("15/30/60", LeashLevelGuide.Detail(LeashLevelGuide.Row(PunishKind.Chaster), _ => "{0}/{1}/{2}"));
        Assert.Equal("leash_guide_chaster", LeashLevelGuide.Row(PunishKind.Chaster).DetailKey);
    }
}
