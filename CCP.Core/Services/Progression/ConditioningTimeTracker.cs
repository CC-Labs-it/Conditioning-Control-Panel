using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF's conditioning-time tracker (MainWindow.UiUpdates.cs:746-842) without the timer: the head
    /// calls <see cref="Start"/> at engine start, <see cref="Tick"/> once a second while it runs and
    /// <see cref="Stop"/> at engine stop. It counts engine-running time only - WPF has no idle or
    /// session-pause rule (a paused session keeps the engine, and so the tracker, running); every
    /// engine stop (Stop button, panic, tray, exit) ends it. Real tick gaps (capped at 15 seconds) write whole minutes, Stop
    /// writes whatever wall-clock time the minute writes have not covered yet.
    /// <para>The only writer of <see cref="AppSettings.TotalConditioningMinutes"/> during a run: WPF's
    /// AchievementService 1/60-minute tick was removed for double counting (AchievementService.cs:211).</para>
    /// </summary>
    public sealed class ConditioningTimeTracker
    {
        private readonly TimeProvider _clock;
        private DateTimeOffset _start;
        private double _baseline;
        private double _pendingSeconds;
        private DateTimeOffset _lastTick;

        public ConditioningTimeTracker(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

        public bool IsTracking { get; private set; }

        /// <summary>WPF StartConditioningTimeTracker: no-op when already tracking.</summary>
        public void Start(AppSettings s)
        {
            if (IsTracking) return;
            _start = _clock.GetUtcNow();
            _baseline = s.TotalConditioningMinutes;
            _pendingSeconds = 0;
            _lastTick = _start;
            IsTracking = true;
        }

        /// <summary>Credit real time between ticks, as WPF ConditioningClock does.</summary>
        public void Tick(AppSettings s)
        {
            if (!IsTracking) return;
            var now = _clock.GetUtcNow();
            var due = Progression.CoreConditioningClock.Tick(ref _pendingSeconds, (now - _lastTick).TotalSeconds);
            _lastTick = now;
            if (due > 0) AddConditioningTime(s, due);
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

        /// <summary>WPF SkillTreeService.AddConditioningTime (:659-669). ponytail: WPF also mirrors into the
        /// season bucket (SeasonRecapService.AddConditioningMinutes); this head has no season-key service yet.</summary>
        public static void AddConditioningTime(AppSettings s, double minutes)
        {
            if (!(minutes > 0)) return;
            s.TotalConditioningMinutes += minutes;
            CoreSettings.Save();
        }
    }
}
