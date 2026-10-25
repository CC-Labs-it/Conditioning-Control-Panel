using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ProfilePollAdoptTests
{
    private static AppSettings Clean() => new()
    {
        UnifiedId = "poll-account", CurrentSeason = "2026-10", PlayerLevel = 4, PlayerXP = 10,
        LastConfirmedServerXpAccount = "poll-account", LastConfirmedServerXpSeason = "2026-10",
        LastConfirmedServerXp = XpCurve.GetTotalXP(4, 10, XpCurve.CurveEpochLegacy),
    };

    [Fact]
    public void AnyPositiveLeadOnACleanLedgerIsAdoptedAndAgreed()
    {
        var s = Clean();
        var target = ProfileAdopt.TotalXp(s) + 1;
        Assert.True(ProfileAdopt.TryAdoptFromProfilePoll(s, 4, target, s.CurrentSeason));
        Assert.Equal(11, s.PlayerXP, 6);
        Assert.Equal(target, s.LastConfirmedServerXp);
        Assert.True(ProfileAdopt.TryAdoptFromProfilePoll(s, 4, target + 1, null));
        Assert.Equal(12, s.PlayerXP, 6);
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("missing-watermark")]
    [InlineData("other-account")]
    [InlineData("other-watermark-season")]
    [InlineData("other-server-season")]
    [InlineData("offline")]
    [InlineData("signed-out")]
    [InlineData("migration")]
    public void UnprovenOrForeignLedgersAreUntouched(string gate)
    {
        var s = Clean();
        switch (gate)
        {
            case "dirty": s.PlayerXP += 1; break;
            case "missing-watermark": s.LastConfirmedServerXp = 0; break;
            case "other-account": s.LastConfirmedServerXpAccount = "other"; break;
            case "other-watermark-season": s.LastConfirmedServerXpSeason = "2026-09"; break;
            case "offline": s.OfflineMode = true; break;
            case "signed-out": s.UnifiedId = null; break;
            case "migration": s.PendingDescentMigrationChoice = DescentMigrationChoices.Cycle; break;
        }
        var before = (s.PlayerLevel, s.PlayerXP, s.LastConfirmedServerXp, s.CurrentSeason);
        Assert.False(ProfileAdopt.TryAdoptFromProfilePoll(s, 5, ProfileAdopt.TotalXp(s) + 10000,
            gate == "other-server-season" ? "2026-11" : s.CurrentSeason));
        Assert.Equal(before, (s.PlayerLevel, s.PlayerXP, s.LastConfirmedServerXp, s.CurrentSeason));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTotalsNeverEnterTheLedger(double xp)
    {
        var s = Clean();
        Assert.False(ProfileAdopt.TryAdoptFromProfilePoll(s, 4, xp, null));
        Assert.Equal(10, s.PlayerXP);
    }

    [Fact]
    public void NeverAdoptsDownwardSidewaysOrAnInvalidLevel()
    {
        var s = Clean();
        var total = ProfileAdopt.TotalXp(s);
        Assert.False(ProfileAdopt.TryAdoptFromProfilePoll(s, 4, total - 1, null));
        Assert.False(ProfileAdopt.TryAdoptFromProfilePoll(s, 4, total + 0.01, null));
        Assert.False(ProfileAdopt.TryAdoptFromProfilePoll(s, 0, total + 1, null));
        Assert.Equal(10, s.PlayerXP);
    }

    [Fact]
    public void PricesTheRemainderOnTheFollowedCurve()
    {
        var s = Clean();
        s.DescentEpoch = XpCurve.CurveEpochDescent;
        s.PlayerLevel = 45;
        s.LastConfirmedServerXp = ProfileAdopt.TotalXp(s);
        var target = XpCurve.GetTotalXP(46, 20, XpCurve.CurveEpochDescent);
        Assert.True(ProfileAdopt.TryAdoptFromProfilePoll(s, 46, target, s.CurrentSeason));
        Assert.Equal(46, s.PlayerLevel);
        Assert.Equal(20, s.PlayerXP, 6);
        Assert.Equal(target, s.LastConfirmedServerXp);
    }
}
