using System;

namespace ConditioningControlPanel.Models
{
    /// <summary>
    /// Pink Rush state rules, moved from the WPF head's SkillTreeService (#region Pink Rush and
    /// GetTotalXpMultiplier) so both heads trigger, time and pay it the same way. The timers and the
    /// started/ended events stay head-side.
    /// </summary>
    public static class PinkRushRules
    {
        public const string SkillId = "pink_rush";

        /// <summary>The check timer's period (SkillTreeService _pinkRushCheckTimer).</summary>
        public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

        /// <summary>How long a rush lasts (StartPinkRush: 60 seconds of 3x XP).</summary>
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

        /// <summary>PinkRushCheckTimer_Tick: owned, not already running, and a 50% roll.</summary>
        public static bool ShouldStart(AppSettings s, double roll) =>
            SkillTreeRules.HasSkill(s, SkillId) && !s.PinkRushActive && roll < 0.50;

        public static void Begin(AppSettings s, DateTime now)
        {
            s.PinkRushActive = true;
            s.PinkRushEndTime = now + Window;
        }

        /// <summary>PinkRushTimer_Tick: the window has run out.</summary>
        public static bool IsDue(AppSettings s, DateTime now) =>
            s.PinkRushEndTime.HasValue && now >= s.PinkRushEndTime.Value;

        public static void Clear(AppSettings s)
        {
            s.PinkRushActive = false;
            s.PinkRushEndTime = null;
        }

        // ScreenWash, WPF b86fa2aa6: constant-alpha compositor fade, 30 Hz for 500 ms.
        public const int WashFadeMs = 500;
        public const int WashStepMs = 33;
        public const double WashPeak = 100.0 / 255.0 * 0.6;

        public static byte WashAlphaAt(double elapsedMs, double fadeMs, double peak)
        {
            if (double.IsNaN(elapsedMs) || double.IsNaN(peak) || fadeMs <= 0) return 0;
            var k = 1.0 - Math.Clamp(elapsedMs / fadeMs, 0, 1);
            return (byte)Math.Round(255 * Math.Clamp(peak, 0, 1) * k);
        }

        /// <summary>The multiplicative term of GetTotalXpMultiplier: 3x while a rush runs and the skill is owned.</summary>
        public static double XpFactor(AppSettings s) =>
            s.PinkRushActive && SkillTreeRules.HasSkill(s, SkillId) ? 3.0 : 1.0;
    }
}
