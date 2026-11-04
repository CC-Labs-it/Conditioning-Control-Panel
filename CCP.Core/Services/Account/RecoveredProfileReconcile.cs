using System;
using ConditioningControlPanel.Models;
using static ConditioningControlPanel.Services.V2AuthService;

namespace ConditioningControlPanel.Services
{
    // WPF ProfileSyncService.ReconcileRestoredProfileAsync: compare the backup's season,
    // preserve skills, and adopt the server's progression when the backup crossed a rollover.
    internal static class RecoveredProfileReconcile
    {
        internal static void Apply(AppSettings settings, V2User user, string? restoredSeason, DateTime nowUtc)
        {
            ProfileAdopt.ApplyCurveEpoch(settings, user.CurveEpoch);
            var localXp = ProfileAdopt.TotalXp(settings);
            if (ProfileAdopt.ShouldAdoptServerSeason(user.CurrentSeason, restoredSeason ?? settings.CurrentSeason)
                && localXp > user.Xp)
            {
                settings.PlayerLevel = user.Level;
                settings.PlayerXP = XpCurve.GetCurrentLevelXP(user.Level, user.Xp, ProfileAdopt.Epoch(settings));
                settings.HighestLevelEver = user.HighestLevelEver;
                settings.CurrentSeason = user.CurrentSeason;
                settings.SeasonResetPending = true;
                ProfileAdopt.ClearXpWatermark(settings, "restore season rollover");
            }
            else if (user.Xp > localXp)
                ProfileAdopt.ApplyUserData(settings, user, settings.AuthToken, nowUtc);
        }
    }
}
