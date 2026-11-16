// The pure half of WPF AvatarTube/AvatarTubeWindow.Speech.cs:2558-2798 (ShowGreeting's away bucket,
// BuildAbsenceGreeting's templates, FormatGreetingName and the streak-milestone latch). The lines are the
// WPF literals verbatim. The lapse line (Speech.cs:2746 TakeLapseGreeting) stays in the WPF head.

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

        /// <summary>WPF Speech.cs:2669 BuildAbsenceGreeting without the lapse line: null on first run.</summary>
        public static string? Build(DateTime? lastSeenUtc, DateTime nowUtc, string? name, Random random)
        {
            if (lastSeenUtc == null) return null;
            var elapsed = Elapsed(lastSeenUtc.Value, nowUtc);
            string[] templates =
                elapsed < TimeSpan.FromHours(6) ? new[] { "Back already? Hehe~ 💕", "Ooh, you're back so soon! ✨", "Couldn't stay away, {name}? 😘" }
                : elapsed < TimeSpan.FromHours(18) ? new[] { "Welcome back, {name}! 💖", "Yay, you're here again! ✨", "Hi again, cutie~ 💕" }
                : elapsed < TimeSpan.FromDays(3) ? new[] { "There you are! So good to see you~ 💕", "You're back, {name}! ✨", "Hehe, hi again! 💖" }
                : elapsed < TimeSpan.FromDays(7) ? new[] { "Yay, you came back! 💕", "So happy you're here, {name}! ✨", "Hi hi! Let's have some fun~ 💖" }
                : elapsed < TimeSpan.FromDays(30) ? new[] { "Look who's back! 💕", "So good to see you again, {name}! ✨", "Welcome back, gorgeous~ 💖" }
                : new[] { "It's been ages — welcome back! 💕✨", "Yaaay, you're back, {name}! So happy to see you! 💖", "Welcome back! Let's pick up right where we left off~ ✨" };
            return FormatName(templates[random.Next(templates.Length)], name);
        }

        /// <summary>WPF Speech.cs:2787 FormatGreetingName.</summary>
        public static string FormatName(string template, string? name)
        {
            if (!string.IsNullOrWhiteSpace(name)) return template.Replace("{name}", name.Trim());
            return template.Replace(", {name}", "").Replace(" {name}", "").Replace("{name}", "").Replace("  ", " ").Trim();
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
