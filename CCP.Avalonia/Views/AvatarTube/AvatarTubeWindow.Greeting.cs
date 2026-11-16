// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs:2558-2665 (ShowGreeting,
// CheckStreakMilestoneGreeting) and its 2 s timer (AvatarTubeWindow.xaml.cs:343-353); the pure half is Core
// AbsenceGreeting. Not ported: the lapse line (Speech.cs:2746 TakeLapseGreeting) and per-phrase audio
// overrides (Speech.cs:2291-2297); the mod event-audio fallback is.

using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        // WPF Speech.cs:2558: the warm welcome fires once per launch; a re-created tube says a StartupGreeting.
        internal static bool AbsenceGreetingShownThisLaunch;
        private DispatcherTimer? _greetingTimer;
        private bool _greetingScheduled;
        internal bool GreetingPending => _greetingTimer?.IsEnabled == true;

        private void ScheduleGreeting()
        {
            if (_greetingScheduled) return;   // Opened again after a Hide/Show never greets twice
            _greetingScheduled = true;
            _greetingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _greetingTimer.Tick += (_, _) => { _greetingTimer?.Stop(); _greetingTimer = null; ShowGreeting(); };
            _greetingTimer.Start();
        }

        internal void ShowGreeting()
        {
            if (AbsenceGreetingShownThisLaunch) { GiggleFromCategory("StartupGreeting"); return; }
            AbsenceGreetingShownThisLaunch = true;
            var s = CoreSettings.Current;
            var lastSeen = s.LastSeenUtc;
            var now = DateTime.UtcNow;
            // Written on open, local only (WPF Speech.cs:2575-2584); CloudSettingsBackup excludes it too.
            s.LastSeenUtc = now;
            CoreSettings.Save(suppressCloudBackup: true);
            var bark = Platform.BarkHead.Engine;
            bool spoke = bark?.Raise("AppOpened", c => c.Set("away_bucket", AbsenceGreeting.AwayBucket(lastSeen, now))) ?? false;
            if (!spoke)
            {
                var greeting = AbsenceGreeting.Build(lastSeen, now, CoreAccount.DisplayName, _random);
                if (greeting == null) GiggleFromCategory("StartupGreeting");
                else Giggle(greeting);
            }
            if (bark == null) return;
            var (latch, announce) = AbsenceGreeting.StreakMilestone(s.CurrentStreak, s.LastAnnouncedStreakMilestone);
            if (latch is not { } reached) return;
            s.LastAnnouncedStreakMilestone = reached;
            CoreSettings.Save(suppressCloudBackup: true);
            if (announce) bark.Raise("StreakMilestone", c => c.Set("streak_days", (double)reached), guaranteed: true);
        }

        /// <summary>WPF GiggleFromCategory (Speech.cs:2279): an enabled phrase of the category, with the mod's
        /// event audio when it ships one.</summary>
        internal void GiggleFromCategory(string category)
        {
            var enabled = AwarenessReactionPhrases.Enabled(category);
            if (enabled.Length == 0) return;
            var text = enabled[_random.Next(enabled.Length)];
            Giggle(text, phraseAudioPath: CompanionEventAudio.Resolve(text));
        }
    }
}
