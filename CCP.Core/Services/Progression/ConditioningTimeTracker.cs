using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF's conditioning-time tracker (MainWindow.UiUpdates.cs:746-842) without the timer: the head
    /// calls <see cref="Start"/> at engine start, <see cref="Tick"/> once a second while it runs and
    /// <see cref="Stop"/> at engine stop. It counts engine-running time only - WPF has no idle or
    /// session-pause rule (a paused session keeps the engine, and so the tracker, running); every
    /// engine stop (Stop button, panic, tray, exit) ends it. The 60th tick writes one minute, Stop
    /// writes whatever wall-clock time the minute writes have not covered yet.
    /// <para>The only writer of <see cref="AppSettings.TotalConditioningMinutes"/> during a run: WPF's
    /// AchievementService 1/60-minute tick was removed for double counting (AchievementService.cs:211).</para>
    /// </summary>
    public sealed class ConditioningTimeTracker
    {
        private readonly TimeProvider _clock;
        private DateTimeOffset _start;
        private double _baseline;
        private int _seconds;

        public ConditioningTimeTracker(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

        public bool IsTracking { get; private set; }

        /// <summary>WPF StartConditioningTimeTracker: no-op when already tracking.</summary>
        public void Start(AppSettings s)
        {
            if (IsTracking) return;
            _start = _clock.GetUtcNow();
            _baseline = s.TotalConditioningMinutes;
            _seconds = 0;
            IsTracking = true;
        }

        /// <summary>One 1-second tick: every 60th adds a whole minute (WPF :779-783).</summary>
        public void Tick(AppSettings s)
        {
            if (!IsTracking || ++_seconds < 60) return;
            _seconds = 0;
            AddConditioningTime(s, 1.0);
        }

        /// <summary>WPF StopConditioningTimeTracker (:826-836): top the store up to baseline + elapsed.</summary>
        public void Stop(AppSettings s)
        {
            if (!IsTracking) return;
            IsTracking = false;
            var remaining = _baseline + Elapsed - s.TotalConditioningMinutes;
            if (remaining > 0) AddConditioningTime(s, remaining);
        }

        /// <summary>The live figure the stat pill shows (WPF UpdateStatPills :548-559): baseline + elapsed
        /// while tracking, the stored total otherwise.</summary>
        public double LiveMinutes(AppSettings s) => IsTracking ? _baseline + Elapsed : s.TotalConditioningMinutes;

        private double Elapsed => (_clock.GetUtcNow() - _start).TotalMinutes;

        /// <summary>WPF SkillTreeService.AddConditioningTime + SeasonRecapService.AddConditioningMinutes:
        /// one save for both counters, initializing (never auto-rolling) the season bucket.</summary>
        public static void AddConditioningTime(AppSettings s, double minutes)
        {
            if (!(minutes > 0)) return;
            s.TotalConditioningMinutes += minutes;
            if (string.IsNullOrEmpty(s.SeasonStatsSeason))
                s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, DateTime.UtcNow);
            s.SeasonConditioningMinutes += minutes;
            CoreSettings.Save();
        }
    }
}
