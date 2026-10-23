using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Core.Tests;

// WPF ProfileSyncService.TryAdoptFromServerProgression (:438-490), the 60 s profile poll's
// clean-ledger adopt, now ProfileAdopt.AdoptCleanLedger for the Avalonia head.
public sealed class ProfileAdoptCleanLedgerTests
{
    private static AppSettings Clean(out double total)
    {
        var s = new AppSettings
        {
            UnifiedId = "acct", CurrentSeason = "2026-10", OfflineMode = false,
            PlayerLevel = 10, PlayerXP = 50, DescentEpoch = XpCurve.CurveEpochLegacy,
        };
        total = ProfileAdopt.TotalXp(s);
        (s.LastConfirmedServerXp, s.LastConfirmedServerXpAccount, s.LastConfirmedServerXpSeason) = (total, "acct", "2026-10");
        return s;
    }

    [Fact]
    public void ACleanLedgerAdoptsTheServerLeadAndRecordsTheAgreement()
    {
        var s = Clean(out var total);
        var server = total + 5000;
        var level = XpCurve.DeriveLevelFromLifetimeXp(server, XpCurve.CurveEpochLegacy).Level;

        Assert.True(ProfileAdopt.AdoptCleanLedger(s, level, server, "2026-10", "profile poll"));

        Assert.Equal(level, s.PlayerLevel);
        Assert.Equal(server, ProfileAdopt.TotalXp(s), 3);
        Assert.Equal(server, s.LastConfirmedServerXp, 3);
    }

    [Fact]
    public void ADirtyLedgerOrAnyOtherRefusalLeavesProgressionAlone()
    {
        void Refused(System.Action<AppSettings> setup, int level = 20, double lead = 5000, string? season = "2026-10")
        {
            var s = Clean(out var total);
            setup(s);
            var (l, x) = (s.PlayerLevel, s.PlayerXP);
            Assert.False(ProfileAdopt.AdoptCleanLedger(s, level, total + lead, season, "profile poll"));
            Assert.Equal((l, x), (s.PlayerLevel, s.PlayerXP));
        }

        Refused(s => s.PlayerXP += 10);                         // earned here since the agreed figure
        Refused(s => s.LastConfirmedServerXp = 0);              // clean cannot be proven
        Refused(s => s.OfflineMode = true);
        Refused(s => s.UnifiedId = null);
        Refused(s => s.PendingDescentMigrationChoice = DescentMigrationChoices.Cycle);
        Refused(_ => { }, season: "2026-11");                   // another season's ledger
        Refused(_ => { }, lead: -1);                            // never downward
        Refused(_ => { }, level: 0);
    }
}
