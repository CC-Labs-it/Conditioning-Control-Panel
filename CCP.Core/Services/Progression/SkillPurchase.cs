using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// A server-authoritative enhancement purchase, for heads that do not own WPF's ProfileSyncService:
/// WPF SkillTreeService.PurchaseSkillAsync (SkillTreeService.cs:80, guards + ApplySkillEffects) around
/// ProfileSyncService.PurchaseSkillAsync (ProfileSyncService.cs:3980, balance refusal sync-and-retry, adopt the
/// server's balance and skills, Prestige + season spend). WPF keeps its own copy (frozen); the rules it shares
/// (SparklePoints, SkillTreeRules, EnhancementAnalytics) are the same Core code.
/// </summary>
internal static class SkillPurchase
{
    /// <param name="syncBeforeRetry">WPF SyncBeforeRetryAsync: one real profile sync before a balance refusal is
    /// asked again (#1300). True only when a sync reached the server.</param>
    /// <param name="save">Persists settings (WPF App.Settings.Save).</param>
    public static async Task<(bool Success, string? Error)> PurchaseAsync(string skillId, AppSettings settings, V2AuthService v2,
        AchievementEngine? achievements, Func<Task<bool>> syncBeforeRetry, Action save)
    {
        if (!SkillTreeRules.CanPurchaseSkill(settings, skillId)) return (false, Loc.Get("skill_err_cannot_purchase"));
        var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
        if (skill == null) return (false, Loc.Get("skill_err_unknown"));
        if (string.IsNullOrEmpty(settings.UnifiedId)) return (false, Loc.Get("skill_err_login_required"));

        var (success, error) = await PostAsync(skill, settings, v2, achievements, syncBeforeRetry, save, afterSync: false);
        if (!success) return (false, error);
        ApplySkillEffects(skillId, settings, save);
        Log.Information("Skill purchased via server: {SkillId} for {Cost} points, {Remaining} remaining", skillId, skill.Cost, settings.SkillPoints);
        return (true, null);
    }

    private static async Task<(bool, string?)> PostAsync(SkillDefinition skill, AppSettings settings, V2AuthService v2,
        AchievementEngine? achievements, Func<Task<bool>> syncBeforeRetry, Action save, bool afterSync)
    {
        var unifiedId = settings.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId))
            return (false, "Purchasing enhancements requires a cloud account. Please log in first.");
        var (result, error) = await v2.PurchaseSkillAsync(unifiedId, skill.Id, settings.SkillPoints);
        if (result == null) return (false, error);

        if (!result.Success)
        {
            // Local points stay on a refusal, except a refusal for balance: sync once and ask again, and only a
            // refusal that survives the sync adopts the server's number (#1268 #1269 #1300).
            Log.Warning("Skill purchase rejected: {Error}, server says {Points} points", result.Error, result.SkillPoints);
            var step = SparklePoints.AfterBalanceRefusal(settings.SkillPoints, result.SkillPoints, skill.Cost, afterSync);
            if (step == SparklePoints.RefusalStep.SyncAndRetry)
                return await syncBeforeRetry()
                    ? await PostAsync(skill, settings, v2, achievements, syncBeforeRetry, save, afterSync: true)
                    : (false, result.Error ?? "Purchase failed");
            if (SparklePoints.AdoptAfterRefusal(settings.SkillPoints, result.SkillPoints, skill.Cost) is { } adopted)
            {
                settings.SkillPoints = adopted;
                save();
            }
            return (false, result.Error ?? "Purchase failed");
        }

        ApplyPurchaseResult(settings, result.SkillPoints, result.UnlockedSkills);
        // Prestige: count the spend, then adopt the server total when it is ahead; the season spend bucket too.
        achievements?.TrackSkillPointsSpent(skill.Cost);
        if (skill.Cost > 0)
        {
            if (string.IsNullOrEmpty(settings.SeasonStatsSeason))
                settings.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(settings, DateTime.UtcNow);
            settings.SeasonPointsSpent += skill.Cost;
        }
        if (result.LifetimePointsSpent.HasValue) achievements?.ReconcileLifetimePointsSpent(result.LifetimePointsSpent.Value);
        save();
        return (true, null);
    }

    /// <summary>WPF ProfileSyncService.ApplyPurchaseResult: the server's balance, skills unioned so none are lost.</summary>
    public static void ApplyPurchaseResult(AppSettings settings, int? serverSkillPoints, List<string>? serverSkills)
    {
        if (serverSkillPoints.HasValue) settings.SkillPoints = serverSkillPoints.Value;
        if (serverSkills != null)
        {
            var merged = new HashSet<string>(settings.UnlockedSkills ?? new List<string>());
            foreach (var s in serverSkills) merged.Add(s);
            settings.UnlockedSkills = merged.ToList();
        }
    }

    /// <summary>WPF SkillTreeService.ApplySkillEffects, settings half. The Pink Rush check timer is the head's.</summary>
    private static void ApplySkillEffects(string skillId, AppSettings settings, Action save)
    {
        switch (skillId)
        {
            case "good_girl_streak":
                settings.StreakShieldsRemaining = 1;
                settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
                break;
            case "oopsie_insurance":
                settings.StreakFixCharges++;
                save();
                break;
        }
    }
}
