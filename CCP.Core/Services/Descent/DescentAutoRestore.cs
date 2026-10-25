using System;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Descent;

/// <summary>WPF's silent restore ledger. The head posts this after sync and owns saving/notifications.</summary>
public static class DescentAutoRestore
{
    public static int? Drip(AppSettings s, DateTime localNow)
    {
        var today = localNow.ToString("yyyy-MM-dd");
        if (!s.DescentMigrationCompleted || s.DescentPendingStageCeremonies is not { Count: > 0 } queue
            || s.DescentLastStageDripDate == today) return null;
        var stage = queue[0];
        s.DescentPendingStageCeremonies = queue.Skip(1).ToList();
        s.DescentLastStageDripDate = today;
        return stage;
    }

    public static bool Apply(AppSettings s, JObject reply, DescentBlock? block, DateTime now)
    {
        if (s.DescentMigrationCompleted || DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice)
            || reply["descent_migration"] is not JObject offer || offer.Value<bool?>("required") != true) return false;
        var o = new DescentMigrationOffer {
            TotalXpEarned = offer.Value<double?>("total_xp_earned") ?? 0,
            RestoreBasisXp = offer.Value<double?>("restore_basis_xp") ?? 0,
            DevotionDays = offer.Value<int?>("devotion_days") ?? 0 };
        var result = DescentMigration.Resolve(DescentMigrationChoices.Restore, o);
        s.DescentPreMigrationLevel = s.PlayerLevel;
        s.DescentPreMigrationLifetimeXp = result.LifetimeXp;
        s.DescentEpoch = DescentEpochs.AccountDescent;
        s.PlayerLevel = result.Level;
        s.PlayerXP = result.XpIntoLevel;
        s.HighestLevelEver = Math.Max(s.HighestLevelEver, result.Level);
        s.DescentCycleXpBonus = DescentCycleXp.CycleXpBonus;
        var stage = block?.Stage;
        var reached = stage?.Thresholds is { Count: > 0 } thresholds
            ? thresholds.Count(t => o.DevotionDays >= t) : stage?.N ?? 0;
        s.DescentPendingStageCeremonies = Enumerable.Range(1, Math.Max(0, reached)).ToList();
        s.DescentVeteranArchive = true;
        s.DescentAnchorUtc = now;
        s.DescentLastStageDripDate = null;
        ProfileAdopt.ClearXpWatermark(s, "descent silent restore");
        s.DescentMigrationOffered = false;
        s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore; // arm only after the ledger
        return true;
    }
}
