// The pure half of WPF AvatarTube/AvatarTubeWindow.Speech.cs:2558-2798 (ShowGreeting's away bucket,
// BuildAbsenceGreeting's templates, FormatGreetingName and the streak-milestone latch). The lines are the
// Templates are localized; the head owns the lapse eligibility and stamp.

using System;

namespace ConditioningControlPanel.Services.Companion
{
    public static class AbsenceGreeting
    {
        /// <summary>WPF Speech.cs:2610 StreakMilestoneDays. Ascending.</summary>
        public static readonly int[] StreakMilestoneDays = { 7, 14, 30, 60, 100, 365 };

        /// <summary>WPF Speech.cs:2617 GreetingAwayBucket: the AppOpened bark's away_bucket.</summary>
        public static string AwayBucket(DateTime? lastSeenUtc, DateTime nowUtc)
        {
            if (lastSeenUtc == null) return "first";
            var elapsed = Elapsed(lastSeenUtc.Value, nowUtc);
            if (elapsed < TimeSpan.FromHours(6)) return "soon";
            if (elapsed < TimeSpan.FromHours(18)) return "back";
            if (elapsed < TimeSpan.FromDays(3)) return "while";
            return "long";
        }

        /// <summary>WPF Speech.cs:2669 BuildAbsenceGreeting for the regular return: null on first run.</summary>
        public static string? Build(DateTime? lastSeenUtc, DateTime nowUtc, string? name, Random random)
        {
            if (lastSeenUtc == null) return null;
            var elapsed = Elapsed(lastSeenUtc.Value, nowUtc);
            int bucket = elapsed < TimeSpan.FromHours(6) ? 1 : elapsed < TimeSpan.FromHours(18) ? 2
                : elapsed < TimeSpan.FromDays(3) ? 3 : elapsed < TimeSpan.FromDays(7) ? 4
                : elapsed < TimeSpan.FromDays(30) ? 5 : 6;
            var key = $"avatar_greeting_{bucket}_{random.Next(1, 4)}";
            return FormatName(ConditioningControlPanel.Localization.Loc.Get(key), name);
        }

        /// <summary>WPF TakeLapseGreeting: never infer a closure without a fresh server answer.</summary>
        public static Startup.Conversion.LapseReading? Lapse(Models.AppSettings settings, DateTime now, bool confirmed,
            bool deadGrant, Func<int, bool> liveCovers)
        {
            if (!confirmed || (EntitlementTierRule.ClosedTierNow(settings, now) == null && deadGrant)) return null;
            var reading = Startup.Conversion.ConversionRules.DecideLapseFor(settings, now);
            return reading.Row == Startup.Conversion.LapseRow.Closed && !liveCovers(reading.Tier)
                && Startup.Conversion.ConversionRules.Owed(settings.InboxStamps, Startup.Conversion.ConversionRules.KeyLapseGreeting, reading.Stamp)
                ? reading : null;
        }

        /// <summary>WPF Speech.cs:2787 FormatGreetingName.</summary>
        public static string FormatName(string template, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name)) return template.Replace("{name}", name.Trim());
            return template.Replace("、{name}", "").Replace("，{name}", "").Replace(", {name}", "").Replace(" {name}", "").Replace("{name}", "").Replace("  ", " ").Trim();
        }

        /// <summary>WPF Speech.cs:2638 CheckStreakMilestoneGreeting's latch: the new latch value (null = leave it)
        /// and whether a newly reached milestone is announced. A zero streak is transient at startup and never moves it.</summary>
        public static (int? Latch, bool Announce) StreakMilestone(int streak, int announced)
        {
            if (streak <= 0) return (null, false);
            int reached = 0;
            foreach (var m in StreakMilestoneDays)
                if (m <= streak) reached = m;
            if (reached == announced) return (null, false);
            return (reached, reached > announced && reached > 0);
        }

        private static TimeSpan Elapsed(DateTime lastSeenUtc, DateTime nowUtc)
        {
            var elapsed = nowUtc - lastSeenUtc;
            return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        }
    }
}
