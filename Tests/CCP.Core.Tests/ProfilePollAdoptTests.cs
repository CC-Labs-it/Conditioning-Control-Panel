using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ProfilePollAdoptTests
{
    private static AppSettings Clean() => new()
    {
        UnifiedId = "poll-user", CurrentSeason = "2026-10", PlayerLevel = 2, PlayerXP = 100,
        LastConfirmedServerXpAccount = "poll-user", LastConfirmedServerXpSeason = "2026-10",
        LastConfirmedServerXp = XpCurve.GetTotalXP(2, 100, 0), DescentEpoch = 0,
    };

    [Theory]
    [InlineData(null)]
    [InlineData("2026-10")]
    public void SmallLeadOnCleanLedgerAdoptsAndAdvancesWatermark(string? season)
    {
        var s = Clean();
        double total = ProfileAdopt.TotalXp(s) + 25;
        Assert.True(ProfileAdopt.AdoptProfilePoll(s, 2, total, season));
        Assert.Equal(125, s.PlayerXP);
        Assert.Equal(total, ProfileAdopt.ActiveXpWatermark(s));
        Assert.Equal("2026-10", s.CurrentSeason);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("no-account")]
    [InlineData("no-watermark")]
    [InlineData("wrong-account")]
    [InlineData("wrong-watermark-season")]
    [InlineData("wrong-server-season")]
    [InlineData("dirty")]
    [InlineData("migration")]
    [InlineData("invalid-level")]
    [InlineData("zero-xp")]
    [InlineData("nan")]
    [InlineData("lower")]
    [InlineData("equal")]
    [InlineData("tolerance")]
    public void UnsafeOrUnchangedReadLeavesLedgerAndWatermarkUntouched(string gate)
    {
        var s = Clean();
        int level = 2;
        double xp = ProfileAdopt.TotalXp(s) + 25;
        string season = "2026-10";
        switch (gate)
        {
            case "offline": s.OfflineMode = true; break;
            case "no-account": s.UnifiedId = null; break;
            case "no-watermark": s.LastConfirmedServerXp = 0; break;
            case "wrong-account": s.LastConfirmedServerXpAccount = "other"; break;
            case "wrong-watermark-season": s.LastConfirmedServerXpSeason = "2026-09"; break;
            case "wrong-server-season": season = "2026-11"; break;
            case "dirty": s.PlayerXP += 1; break;
            case "migration": s.PendingDescentMigrationChoice = "cycle"; break;
            case "invalid-level": level = 0; break;
            case "zero-xp": xp = 0; break;
            case "nan": xp = double.NaN; break;
            case "lower": xp = ProfileAdopt.TotalXp(s) - 1; break;
            case "equal": xp = ProfileAdopt.TotalXp(s); break;
            case "tolerance": xp = ProfileAdopt.TotalXp(s) + 0.005; break;
        }
        var before = (s.PlayerLevel, s.PlayerXP, s.CurrentSeason, s.LastConfirmedServerXp);
        Assert.False(ProfileAdopt.AdoptProfilePoll(s, level, xp, season));
        Assert.Equal(before, (s.PlayerLevel, s.PlayerXP, s.CurrentSeason, s.LastConfirmedServerXp));
    }
}
