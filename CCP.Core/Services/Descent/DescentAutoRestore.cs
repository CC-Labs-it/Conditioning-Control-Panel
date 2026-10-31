using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Descent;

/// <summary>Main abac4fa02: the silent offer path. No UI or network side effects.</summary>
public static class DescentAutoRestore
{
    public static bool ApplyOffer(AppSettings s, JObject reply, DescentStage? stage, DateTime now, Action persist)
    {
        if (reply["descent_migration"] is not JObject b || b["required"]?.Type != JTokenType.Boolean
            || !b.Value<bool>("required")) return false;
        return ApplyChoice(s, DescentMigrationChoices.Restore, new DescentMigrationOffer
        {
            TotalXpEarned = b.Value<double?>("total_xp_earned") ?? 0,
            RestoreBasisXp = b.Value<double?>("restore_basis_xp") ?? 0,
            DevotionDays = b.Value<int?>("devotion_days") ?? 0,
        }, stage, now, persist);
    }

    /// <summary>Persist the ledger before arming the submit. An interrupted first save can only replay
    /// the same server basis; an interrupted submit retries with the already rewritten ledger.</summary>
    public static bool ApplyChoice(AppSettings s, string choice, DescentMigrationOffer offer,
        DescentStage? stage, DateTime now, Action persist)
    {
        if (!DescentMigrationChoices.IsValid(choice) || s.DescentMigrationCompleted
            || DescentMigrationChoices.IsValid(s.PendingDescentMigrationChoice)) return false;
        var before = (s.PlayerLevel, s.PlayerXP, s.HighestLevelEver, s.DescentEpoch,
            s.DescentPreMigrationLevel, s.DescentPreMigrationLifetimeXp, s.DescentCycleXpBonus,
            s.DescentCycle, s.DescentPendingStageCeremonies, s.DescentVeteranArchive, s.DescentAnchorUtc,
            s.DescentLastStageDripDate, s.LastConfirmedServerXp, s.LastConfirmedServerXpAccount,
            s.LastConfirmedServerXpSeason, s.DescentMigrationOffered);
        var result = DescentMigration.Resolve(choice, offer);
        s.DescentPreMigrationLevel = s.PlayerLevel;
        s.DescentPreMigrationLifetimeXp = result.LifetimeXp;
        s.DescentEpoch = DescentEpochs.AccountDescent;
        s.PlayerLevel = result.Level;
        s.PlayerXP = result.XpIntoLevel;
        s.HighestLevelEver = Math.Max(s.HighestLevelEver, result.Level);
        s.DescentCycleXpBonus = DescentCycleXp.CycleXpBonus;
        if (choice == DescentMigrationChoices.Cycle)
        {
            s.DescentCycle = Math.Max(1, s.DescentCycle);
            s.DescentPendingStageCeremonies = new List<int>();
        }
        else
        {
            var reached = stage?.Thresholds is { Count: > 0 } thresholds
                ? thresholds.Count(t => offer.DevotionDays >= t) : Math.Max(0, stage?.N ?? 0);
            s.DescentPendingStageCeremonies = Enumerable.Range(1, reached).ToList();
        }
        s.DescentVeteranArchive = true;
        s.DescentAnchorUtc = now;
        s.DescentLastStageDripDate = null;
        ProfileAdopt.ClearXpWatermark(s, "descent migration");
        s.DescentMigrationOffered = false;
        try { persist(); }
        catch
        {
            // The sync catch may save again. It must see the old ledger if the first checkpoint
            // failed, rather than a rewritten ledger with no choice to tell the server why.
            (s.PlayerLevel, s.PlayerXP, s.HighestLevelEver, s.DescentEpoch,
                s.DescentPreMigrationLevel, s.DescentPreMigrationLifetimeXp, s.DescentCycleXpBonus,
                s.DescentCycle, s.DescentPendingStageCeremonies, s.DescentVeteranArchive, s.DescentAnchorUtc,
                s.DescentLastStageDripDate, s.LastConfirmedServerXp, s.LastConfirmedServerXpAccount,
                s.LastConfirmedServerXpSeason, s.DescentMigrationOffered) = before;
            throw;
        }
        s.PendingDescentMigrationChoice = choice;
        persist();
        return true;
    }
}
