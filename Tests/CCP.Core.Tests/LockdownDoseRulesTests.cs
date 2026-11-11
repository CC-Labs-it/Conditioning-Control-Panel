using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class LockdownDoseRulesTests
{
    [Theory]
    [InlineData(1, 2, 4)]
    [InlineData(2, 3, 2)]
    [InlineData(3, 4, 2)]
    [InlineData(4, 4, 2)]
    [InlineData(5, 4, 2)]
    public void RoundMath(int round, int wanted, int grace)
    {
        Assert.Equal(wanted, LockdownDoseRules.WantedFor(round));
        Assert.Equal(grace, LockdownDoseRules.DoseGraceFor(round));
        Assert.Equal(6, LockdownDoseRules.DoseGraceFor(0));
    }

    [Fact]
    public void PickerPrefersPriorThenStarterThenEscalation()
    {
        string[] starter = { "flash", "bubbles" };
        string[] escalation = { "video" };
        var first = LockdownDoseRules.PickConscripts(1, new[] { "video" }, starter, escalation, Array.Empty<string>(), new Random(1));
        Assert.Equal("video", first[0]);
        Assert.Contains(first[1], starter); // the earlier tier 1 pick precedes the starter pool
        var second = LockdownDoseRules.PickConscripts(2, new[] { "bubbles" }, starter, escalation, Array.Empty<string>(), new Random(1));
        Assert.Equal(new[] { "bubbles", "flash", "video" }, second);
        Assert.DoesNotContain("video", LockdownDoseRules.PickConscripts(1, Array.Empty<string>(), starter, escalation, Array.Empty<string>(), new Random(1)));
    }

    [Fact]
    public void OffWallCensusCountsQuizAndOnlyLiveTakeover()
    {
        var s = new AppSettings { AudioOnlySession = false, PopQuizEnabled = true, AutonomyModeEnabled = false, AutonomyConsentGiven = false };
        Assert.True(LockdownDoseRules.CountsAsOffWallDose(s, false));
        Assert.DoesNotContain(LockdownDoseRules.Catalog, f => f.Key == "popquiz");
        s.PopQuizEnabled = false;
        s.AutonomyModeEnabled = true;
        s.AutonomyConsentGiven = true;
        s.CornerGifOverlays.Clear();
        Assert.False(LockdownDoseRules.CountsAsOffWallDose(s, false));
        Assert.True(LockdownDoseRules.CountsAsOffWallDose(s, true));
        s.AutonomyConsentGiven = false;
        Assert.False(LockdownDoseRules.CountsAsOffWallDose(s, true));
    }
}
