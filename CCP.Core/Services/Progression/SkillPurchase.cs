using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Buying an enhancement: WPF SkillTreeService.PurchaseSkillAsync (guards, ApplySkillEffects) over
/// ProfileSyncService.PurchaseSkillAsync(skillId, afterSync) (server-authoritative purchase, one sync-and-retry on a
/// balance refusal (#1300), adopted refusal balance, Prestige + season spend). Not here: the 401 token recovery
/// (WPF HandleUnauthorizedAsync) - a 401 goes straight to the session-expired message.
/// </summary>
internal static class SkillPurchase
{
    /// <param name="syncBeforeRetry">WPF SyncBeforeRetryAsync: true only when a sync reached the server.</param>
    internal static async Task<(bool Success, string? Error)> PurchaseAsync(V2AuthService auth, string skillId,
        AchievementEngine? achievements, Func<Task<bool>> syncBeforeRetry)
    {
        var settings = CoreSettings.Current;
        if (!SkillTreeRules.CanPurchaseSkill(settings, skillId)) return (false, Loc.Get("skill_err_cannot_purchase"));
        var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
        if (skill == null) return (false, Loc.Get("skill_err_unknown"));
        if (string.IsNullOrEmpty(settings.UnifiedId)) return (false, Loc.Get("skill_err_login_required"));

        var (success, error) = await ServerPurchaseAsync(auth, settings, skill, achievements, syncBeforeRetry, afterSync: false);
        if (!success) return (false, error);

        // WPF ApplySkillEffects (the pink_rush trigger timer is the Pink Rush feature's, not the purchase's).
        if (skillId == "good_girl_streak")
        {
            settings.StreakShieldsRemaining = 1;
            settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
        }
        else if (skillId == "oopsie_insurance")
        {
            settings.StreakFixCharges++;
            CoreSettings.Save();
        }
        Log.Information("Skill purchased via server: {SkillId} for {Cost} points, {Remaining} remaining", skillId, skill.Cost, settings.SkillPoints);
        return (true, null);
    }

    private static async Task<(bool, string?)> ServerPurchaseAsync(V2AuthService auth, AppSettings settings, SkillDefinition skill,
        AchievementEngine? achievements, Func<Task<bool>> syncBeforeRetry, bool afterSync)
    {
        try
        {
            var (status, merged, result) = await auth.PurchaseSkillAsync(settings.UnifiedId!, skill.Id, settings.SkillPoints);
            if (merged) return (false, Loc.Get("account_merged_retry_hint"));
            if ((int)status is < 200 or > 299)
            {
                if (status == HttpStatusCode.Unauthorized)
                {
                    Log.Warning("Skill purchase failed: auth token invalid/missing");
                    // WPF's text, minus the leading gear emoji (icon guard); the door name is the rail's own loc key.
                    return (false, $"Your session has expired. Open {Loc.Get("nav_door_settings")} → Account and sign in again to purchase skills.");
                }
                var msg = result?.Error ?? $"Server error: {status}";
                Log.Warning("Skill purchase failed: {Error}", msg);
                return (false, msg);
            }
            if (result == null) return (false, "Invalid server response");

            if (!result.Success)
            {
                Log.Warning("Skill purchase rejected: {Error}, server says {Points} points", result.Error, result.SkillPoints);
                // First balance refusal: sync once so the server credits what it has not seen, then ask again (#1300).
                // Only a refusal that survives the sync lowers the wallet (#1268 #1269).
                var step = SparklePoints.AfterBalanceRefusal(settings.SkillPoints, result.SkillPoints, skill.Cost, afterSync);
                if (step == SparklePoints.RefusalStep.SyncAndRetry)
                    return await syncBeforeRetry()
                        ? await ServerPurchaseAsync(auth, settings, skill, achievements, syncBeforeRetry, afterSync: true)
                        : (false, result.Error ?? "Purchase failed");
                if (SparklePoints.AdoptAfterRefusal(settings.SkillPoints, result.SkillPoints, skill.Cost) is { } adopted)
                {
                    settings.SkillPoints = adopted;
                    CoreSettings.Save();
                }
                return (false, result.Error ?? "Purchase failed");
            }

            ApplyPurchaseResult(settings, result.SkillPoints, result.UnlockedSkills);
            // Prestige: count the spend, then adopt the server total when it is ahead (reconcile only raises).
            achievements?.TrackSkillPointsSpent(skill.Cost);
            if (string.IsNullOrEmpty(settings.SeasonStatsSeason)) settings.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(settings);
            settings.SeasonPointsSpent += skill.Cost;
            if (result.LifetimePointsSpent is { } lifetime) achievements?.ReconcileLifetimePointsSpent(lifetime);
            CoreSettings.Save();
            return (true, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Skill purchase request failed");
            return (false, "Connection failed. Please check your internet connection.");
        }
    }

    /// <summary>WPF ProfileSyncService.ApplyPurchaseResult: the server's balance replaces local, skills are unioned.</summary>
    internal static void ApplyPurchaseResult(AppSettings settings, int? serverSkillPoints, List<string>? serverSkills)
    {
        if (serverSkillPoints.HasValue) settings.SkillPoints = serverSkillPoints.Value;
        if (serverSkills != null)
            settings.UnlockedSkills = new HashSet<string>(settings.UnlockedSkills ?? new List<string>()).Union(serverSkills).ToList();
    }

    /// <summary>WPF ProfileSyncService.SyncBeforeRetryAsync over <see cref="SyncPush"/>: a sync that lands while we wait
    /// counts; inside the cooldown, wait out the rest of it once.</summary>
    internal static async Task<bool> SyncBeforeRetryAsync(SyncPush? sync)
    {
        if (sync == null) return false;
        var landed = sync.LastSyncTime;
        if (await sync.PushAsync("purchase-retry", waitForGate: true) || sync.LastSyncTime != landed) return true;
        var left = sync.LastSyncTime is { } last ? SyncPush.Cooldown - (sync.UtcNow() - last) : TimeSpan.Zero;
        if (left <= TimeSpan.Zero || left > SyncPush.Cooldown) return false;
        await Task.Delay(left + TimeSpan.FromMilliseconds(250));
        return await sync.PushAsync("purchase-retry");
    }
}
