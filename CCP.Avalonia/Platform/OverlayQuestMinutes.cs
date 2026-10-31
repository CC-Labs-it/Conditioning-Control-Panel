using System;
using System.Diagnostics;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// WPF AchievementService.TrackTimeBasedProgress :226-275: while an overlay is on screen, a 1 s
    /// tick credits the minutes since the last tick (achievement totals, then App.Quests.TrackPinkFilterMinutes /
    /// TrackSpiralMinutes). Keeps this head's existing strict 6 s RunningTimeCredit ceiling.
    /// The tick runs only while the overlay shows; hiding resets the stamp and continuous run.
    /// </summary>
    internal sealed class OverlayQuestMinutes
    {
        private readonly Action<double> _track;
        private readonly Action? _hidden;
        private readonly RunningTimeCredit _credit = new(TimeSpan.FromSeconds(6) - TimeSpan.FromTicks(1));   // WPF: elapsed < 0.1 min, so exactly 6 s counts nothing
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private DispatcherTimer? _tick;

        /// <summary>Monotonic clock; a test steps it (P08).</summary>
        internal Func<TimeSpan>? Clock;
        internal bool TickRunning => _tick?.IsEnabled == true;

        /// <param name="hidden">Runs when the overlay goes off screen (WPF :252 resets the continuous spiral).</param>
        public OverlayQuestMinutes(Action<double> track, Action? hidden = null) { _track = track; _hidden = hidden; }

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
            if (!showing) { _tick?.Stop(); _hidden?.Invoke(); return; }
            _tick ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Sample(true));
            _tick.Start();
        }
    }
}
