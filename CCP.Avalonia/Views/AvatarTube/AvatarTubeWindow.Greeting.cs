// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs:2558-2665 (ShowGreeting,
// CheckStreakMilestoneGreeting) and its 2 s timer (AvatarTubeWindow.xaml.cs:343-353); the pure half is Core
// AbsenceGreeting. Category phrases use their selected audio override before mod event audio.

using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup.Conversion;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Serilog;

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
                var greeting = lastSeen is { } seen && now - seen >= TimeSpan.FromDays(3)
                    ? TakeLapseGreeting(now) ?? AbsenceGreeting.Build(lastSeen, now, CoreAccount.DisplayName, _random)
                    : AbsenceGreeting.Build(lastSeen, now, CoreAccount.DisplayName, _random);
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

        // WPF Speech.cs:2746: only a server-confirmed closed tier, once per closure.
        internal string? TakeLapseGreeting(DateTime now)
        {
            try
            {
                var s = CoreSettings.Current;
                var reading = AbsenceGreeting.Lapse(s, now, EntitlementRefresh.ServerAnsweredThisRun,
                    AccountSeed.Patreon?.GrantLooksDead == true, MainShellWindow.LiveCoversLapse);
                if (reading is not { } lapse) return null;
                const string key = ConversionRules.KeyLapseGreeting;
                var tier = lapse.Tier >= 2 ? "Prime" : "Basic";
                var line = Loc.Get("avatar_lapse_" + _random.Next(1, 4)).Replace("{tier}", tier);
                var stamp = lapse.Stamp;
                Dispatcher.UIThread.Post(() =>
                {
                    if (ConversionRules.Stamp(CoreSettings.Current.InboxStamps, key, stamp)) CoreSettings.Save();
                });
                return AbsenceGreeting.FormatName(line, CoreAccount.DisplayName);
            }
            catch (Exception ex) { Log.Debug(ex, "Lapse greeting skipped"); return null; }
        }

        internal static string? CategoryAudio(string category, string text)
        {
            var settings = CoreSettings.Current;
            int index = Array.IndexOf(CoreMods.GetPhrases(category) ?? Array.Empty<string>(), text);
            var id = index >= 0 ? $"{category}:{index}" : settings.CustomCompanionPhrases?.FirstOrDefault(p => p.Category == category && p.Text == text)?.Id;
            if (id == null) return null;
            string? Existing(string? file) => !string.IsNullOrEmpty(file) && File.Exists(Path.Combine(Models.CompanionPhrase.DefaultAudioFolder, file))
                ? Path.Combine(Models.CompanionPhrase.DefaultAudioFolder, file) : null;
            return (settings.PhraseAudioOverrides.TryGetValue(id, out var audio) ? Existing(audio) : null)
                ?? Existing(settings.CustomCompanionPhrases?.FirstOrDefault(p => p.Id == id)?.AudioFileName);
        }

        /// <summary>WPF GiggleFromCategory (Speech.cs:2279): an enabled phrase with user or mod audio.</summary>
        internal void GiggleFromCategory(string category)
        {
            var enabled = AwarenessReactionPhrases.Enabled(category);
            if (enabled.Length == 0) return;
            var text = enabled[_random.Next(enabled.Length)];
            Giggle(text, phraseAudioPath: CategoryAudio(category, text) ?? CompanionEventAudio.Resolve(text));
        }
    }
}
