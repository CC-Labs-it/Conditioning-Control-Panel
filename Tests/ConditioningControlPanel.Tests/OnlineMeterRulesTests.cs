using System;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Online media today" (owner mockup, 2026-10-19): shown only while the media source includes
/// online media, percent only, amber from 80%, grey when spent, one plan line and one "See plans"
/// per plan, the official note once and only while official.
/// </summary>
public class OnlineMeterRulesTests
{
    private static OnlineMeterState S(string? source = "mixed", bool on = true, int pct = 42, bool spent = false,
        ScrolllerPlan plan = ScrolllerPlan.Free, bool official = false, bool seen = false)
        => OnlineMeterRules.For(source, on, pct, spent, plan, official, seen);

    [Theory]
    [InlineData("online", true)]
    [InlineData("mixed", true)]
    [InlineData("local", false)]
    [InlineData(null, false)]
    [InlineData("Online", false)]   // AppSettings whitelists lower case; anything else reads as local
    public void ShownOnlyWhileTheSourceIncludesOnlineMedia(string? source, bool shown)
        => Assert.Equal(shown, S(source).Show);

    [Fact]
    public void HiddenWhileTheServerSwitchIsOff() => Assert.False(S(on: false).Show);

    [Theory]
    [InlineData(0, false, (int)OnlineMeterTone.Pink)]
    [InlineData(79, false, (int)OnlineMeterTone.Pink)]
    [InlineData(80, false, (int)OnlineMeterTone.Amber)]
    [InlineData(99, false, (int)OnlineMeterTone.Amber)]
    [InlineData(100, true, (int)OnlineMeterTone.Spent)]
    [InlineData(37, true, (int)OnlineMeterTone.Spent)]   // a lowered limit can spend the day early
    public void Tone(int pct, bool spent, int tone) => Assert.Equal((OnlineMeterTone)tone, S(pct: pct, spent: spent).Tone);

    [Fact]
    public void HundredOnlyWhenSpent()
    {
        Assert.Equal(99, S(pct: 100, spent: false).Percent);
        Assert.Equal(100, S(pct: 37, spent: true).Percent);
        Assert.Equal(0, S(pct: -5).Percent);
    }

    [Theory]
    [InlineData((int)ScrolllerPlan.Free, "online_meter_under_free", "online_meter_upsell_free", 1)]
    [InlineData((int)ScrolllerPlan.Basic, "online_meter_under_basic", "online_meter_upsell_basic", 2)]
    [InlineData((int)ScrolllerPlan.Prime, "online_meter_under_prime", null, null)]
    public void PlanLines(int plan, string under, string? upsell, int? tier)
    {
        var st = S(plan: (ScrolllerPlan)plan);
        Assert.Equal(under, st.UnderKey);
        Assert.Equal(upsell, st.UpsellKey);
        Assert.Equal(tier, st.SeePlansTier);
    }

    [Theory]
    [InlineData("mixed", true, false, true, true)]
    [InlineData("mixed", true, true, false, true)]     // "Nice" folds the note, the stamped line stays
    [InlineData("mixed", false, false, false, false)]  // not official: neither
    [InlineData("local", true, false, false, false)]   // no online media in the source: neither
    public void OfficialNoteAndStampedLine(string source, bool official, bool seen, bool note, bool stamped)
    {
        var st = S(source, official: official, seen: seen);
        Assert.Equal(note, st.ShowOfficialNote);
        Assert.Equal(stamped, st.ShowStampedLine);
    }

    [Theory]
    [InlineData((int)ScrolllerPlan.Free, "online_official_plan_free")]
    [InlineData((int)ScrolllerPlan.Basic, "online_official_plan_basic")]
    [InlineData((int)ScrolllerPlan.Prime, "online_official_plan_prime")]
    public void OfficialPlanLine(int plan, string key) => Assert.Equal(key, OnlineMeterRules.OfficialPlanKey((ScrolllerPlan)plan));

    [Theory]
    [InlineData(18, 0, "online_meter_hours", 6)]
    [InlineData(17, 59, "online_meter_hours", 6)]
    [InlineData(23, 0, "online_meter_hours", 1)]     // 59 min 30 s reads "1 h", not "60 min"
    [InlineData(20, 40, "online_meter_hours", 3)]
    [InlineData(23, 20, "online_meter_minutes", 40)]
    [InlineData(23, 59, "online_meter_minutes", 1)]
    public void BackIn(int hour, int minute, string key, int value)
    {
        var now = new DateTime(2026, 10, 19, hour, minute, 30);
        Assert.Equal((key, value), OnlineMeterRules.BackIn(now, now.Date.AddDays(1)));
    }

    [Fact]
    public void BackIn_NeverZeroMinutes()
    {
        var reset = new DateTime(2026, 10, 20);
        Assert.Equal(("online_meter_minutes", 1), OnlineMeterRules.BackIn(reset.AddSeconds(-1), reset));
        Assert.Equal(("online_meter_minutes", 1), OnlineMeterRules.BackIn(reset.AddSeconds(5), reset));
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(1, false, 0.03)]     // a started day shows a sliver
    [InlineData(42, false, 0.42)]
    [InlineData(99, false, 0.99)]
    [InlineData(100, true, 0)]       // spent: the grey circle says it, no arc
    public void RingSweep(int pct, bool spent, double sweep)
        => Assert.Equal(sweep, OnlineMeterRules.RingSweep(pct, spent), 3);
}
