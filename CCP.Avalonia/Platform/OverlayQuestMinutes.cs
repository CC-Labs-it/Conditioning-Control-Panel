using System;
using System.Diagnostics;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF AchievementService.TrackTimeBasedProgress :217-283, the quest half: while an overlay is on
    /// screen, a 1 s tick credits the minutes since the last tick (App.Quests.TrackPinkFilterMinutes /
    /// TrackSpiralMinutes). RunningTimeCredit measures late ticks in full and drops gaps over
    /// three minutes (sleep / suspension), as WPF AchievementService.cs:45-46/:228/:249 (main a4a5aa43b). The tick runs only while the overlay shows (WPF's ticks
    /// while off only reset the stamp).
    /// </summary>
    internal sealed class OverlayQuestMinutes
    {
        private readonly Action<double> _track;
        private readonly RunningTimeCredit _credit = new();
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private DispatcherTimer? _tick;

        /// <summary>Monotonic clock; a test steps it (P08).</summary>
        internal Func<TimeSpan>? Clock;
        internal bool TickRunning => _tick?.IsEnabled == true;

        public OverlayQuestMinutes(Action<double> track) => _track = track;

        /// <summary>One tracking tick.</summary>
        internal void Sample(bool showing)
        {
            var minutes = _credit.Sample(showing, Clock?.Invoke() ?? _watch.Elapsed);
            if (minutes > 0) _track(minutes);
        }

        /// <summary>Call whenever the overlay's windows open or close.</summary>
        internal void Follow(bool showing)
        {
            if (showing && TickRunning) return;   // a repaint of an overlay already counting
            Sample(showing);                       // opens the stamp on show, closes it on hide
            if (!showing) { _tick?.Stop(); return; }
            _tick ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Sample(true));
            _tick.Start();
        }
    }
}
