using System;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The engine half of WPF <c>ProgressionService.AddXP</c> / <c>SpendXPOnLevels</c>, for a head that seeds
    /// <see cref="CoreProgression.AddXPProvider"/> with <see cref="Add"/>: the login gate, the XP add and the
    /// level loop with <c>HighestLevelEver</c>.
    /// Chaos awards use the full skill multiplier; other sources currently apply only Pink Rush. Each level
    /// gained credits <see cref="PointsPerLevel"/> Sparkle (WPF SkillTreeService.OnLevelUp, shared with web
    /// XP claims as WPF SpendXPOnLevels is). Companion XP: CompanionCore.AddCompanionXP. Level achievements listen to <see cref="LevelUp"/>; quests listen to <see cref="QuestAwarded"/>
    /// (the Avalonia head feeds it to QuestService.TrackXPEarned, as WPF AddXP:120 does).
    /// </summary>
    public static class ProgressionBank
    {
        /// <summary>The new level, once per level gained (WPF ProgressionService.LevelUp).</summary>
        public static event Action<int>? LevelUp;

        /// <summary>Amount and source after the award (WPF XPAwarded; the sync nudge listens).</summary>
        public static event Action<double, string>? Awarded;
        public static event Action<double, string>? QuestAwarded; // WPF AddXP:120, excludes AddClaimedXP:140-175

        /// <summary>WPF SkillTreeService.PointsPerLevel: Sparkles credited per level gained.</summary>
        public const int PointsPerLevel = 1;

        /// <summary>Sparkles a level-up credited, once per level (WPF SkillTreeService.OnLevelUp:636 ->
        /// AchievementService.TrackSkillPointsEarned, the lifetime "earned" stat).</summary>
        public static event Action<int>? SkillPointsAwarded;

        // WPF Services/Progression/ProgressionService.cs:123-126: display gets adjusted XP and the level-loop result.
        public sealed record XpAward(double Amount, XPSource Source, bool LeveledUp);
        public static event Action<XpAward>? XPAwarded;

        public static void Add(double amount, string source)
        {
            var s = CoreSettings.Current;
            var offline = s.OfflineMode && !string.IsNullOrWhiteSpace(s.OfflineUsername);
            if (!CoreAccount.IsLoggedIn && !offline)
            {
                Log.Debug("XP not awarded - user not logged in and not in offline mode");
                return;
            }
            // ponytail: no idle tracker on this head, so the passive sources WPF suppresses while idle
            // (ProgressionService.cs:70-78) are never banked; bank them again once an idle tracker exists.
            if (source is "Flash" or "Subliminal" or "BouncingText")
            {
                Log.Debug("XP not banked: +{Amount} from passive {Source} (no idle tracker)", amount, source);
                return;
            }
            // WPF ProgressionService.AddXP:90: the lasting Descent bonus for a migrated account (1.0 otherwise).
            // Awarded still carries the base amount: WPF feeds quests the base too (AddXP:120).
            // WPF AddXP:89 applies the skill multiplier once, while quests and companions receive base XP.
            var skillMult = source == "Chaos"
                ? Models.SkillTreeRules.GetTotalXpMultiplier(s, DateTime.Now.Hour, 0.0)
                : Models.PinkRushRules.XpFactor(s);
            var adjusted = amount * skillMult * Services.Descent.DescentCycleXp.XpBonusFor(s);
            s.PlayerXP += adjusted;
            Log.Information("XP awarded: +{Amount} from {Source} (now {Now})", adjusted, source, s.PlayerXP);
            // WPF AddXP:117: the active companion gets the BASE amount, after the gates, before the level loop.
            Services.Companion.CompanionCore.AddCompanionXP(amount, source);
            SpendAndRaise(s, amount, adjusted, source);
        }

        /// <summary>WPF ProgressionService.AddClaimedXP: web XP the server already earned - the login gate and the level
        /// loop, no multipliers, no companion share; one first-claim toast per install.</summary>
        public static void AddClaimed(double amount)
        {
            var s = CoreSettings.Current;
            if (amount <= 0) return;
            if (!CoreAccount.IsLoggedIn && !(s.OfflineMode && !string.IsNullOrWhiteSpace(s.OfflineUsername)))
            {
                Log.Debug("Web XP claim not applied - user not logged in and not in offline mode");
                return;
            }
            s.PlayerXP += amount;
            Log.Information("Web XP claim applied: +{Amount} (now {Now})", amount, s.PlayerXP);
            SpendAndRaise(s, amount, amount, null);   // no quest award: WPF AddClaimedXP:140-175 tracks achievement XP only
            // WPF AnnounceFirstWebXpClaim: spend the seen flag beside the toast on the UI thread.
            // No new surface: use the head's existing passive notification presenter.
            if (CoreProgram.NotifyProvider == null || !CoreDispatch.HasUiThread) return;
            CoreDispatch.Post(() =>
            {
                var live = CoreSettings.Current;
                if (live.SeenFeatureIntros.Contains("web-xp")) return;
                live.SeenFeatureIntros.Add("web-xp");
                CoreSettings.Save();
                CoreProgram.Notify(Localization.Loc.GetF("web_xp_claim_notice", amount.ToString("0")),
                    "Success", TimeSpan.FromSeconds(9));
            });
        }

        private static void SpendAndRaise(Models.AppSettings s, double amount, double adjusted, string? source)
        {
            var epoch = ProfileAdopt.Epoch(s);
            var levels = new System.Collections.Generic.List<(int Level, int Before, int After)>();
            for (var need = XpCurve.GetXPForLevel(s.PlayerLevel, epoch); s.PlayerXP >= need; need = XpCurve.GetXPForLevel(s.PlayerLevel, epoch))
            {
                s.PlayerXP -= need;
                s.PlayerLevel++;
                if (s.PlayerLevel > s.HighestLevelEver) s.HighestLevelEver = s.PlayerLevel;
                // WPF ProgressionService.cs:288 -> SkillTreeService.OnLevelUp:627-648: the level's Sparkle and the
                // season peak (PlayerLevel itself can be wiped at a rollover).
                var before = s.SkillPoints;
                s.SkillPoints += PointsPerLevel;
                s.SeasonPeakLevel = Math.Max(s.SeasonPeakLevel, s.PlayerLevel);
                levels.Add((s.PlayerLevel, before, s.SkillPoints));
                Log.Information("Level up! Now level {Level}; awarded {Points} skill point(s), total {Total}", s.PlayerLevel, PointsPerLevel, s.SkillPoints);
            }
            CoreSettings.Save();
            foreach (var (level, before, after) in levels)
            {
                LevelUp?.Invoke(level);
                try { SkillPointsAwarded?.Invoke(after - before); }
                catch (Exception ex) { Log.Debug("SkillPointsAwarded subscriber failed: {E}", ex.Message); }
                // Only after the save, as WPF: the header wallet's reward cue (presentation only).
                SparklePointRewards.PublishCredit(before, after, SparklePointSource.LevelUp);
            }
            if (source != null) QuestAwarded?.Invoke(amount, source);
            Awarded?.Invoke(amount, source ?? "Other");
            // WPF ProgressionService.cs:120,125-126,240-249: presentation runs last and cannot break an award.
            // A web claim raises XPSource.Other (WPF AddClaimedXP).
            try
            {
                XPAwarded?.Invoke(new XpAward(adjusted,
                    source != null && Enum.TryParse<XPSource>(source, out var parsed) ? parsed : XPSource.Other, levels.Count > 0));
            }
            catch (Exception ex)
            {
                Log.Debug("XPAwarded subscriber failed: {E}", ex.Message);
            }
        }
    }
}
