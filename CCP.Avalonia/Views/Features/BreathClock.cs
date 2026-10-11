using System;
using Avalonia;
using ConditioningControlPanel.Avalonia.Controls.Fx;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// IMPORTED from trunk (Views/Features/CardGlow.cs). A tile breath on the window's shared 30 fps
    /// beat: an Avalonia Animation ticks at render rate, so one breathing tile kept the whole window
    /// composing 60 frames a second; on the <see cref="FrameClock"/> it ticks with the other effects.
    /// Same curve as the old Animation (Alternate + SineEaseInOut over the period):
    /// min + (max - min) * (1 - cos(pi t / period)) / 2.
    /// </summary>
    internal sealed class BreathClock
    {
        internal static TimeProvider Time = TimeProvider.System;

        private readonly FrameClock _clock;
        private readonly double _period;
        private long _started;
        private (Visual Target, double Min, double Max)[] _targets = Array.Empty<(Visual, double, double)>();

        public BreathClock(Visual owner, double periodSeconds) : this(new FrameClock(owner), periodSeconds) { }

        /// <summary>A breath on an explicit frame source (tests).</summary>
        internal BreathClock(IFrameSource source, double periodSeconds) : this(new FrameClock(source), periodSeconds) { }

        private BreathClock(FrameClock clock, double periodSeconds)
        {
            _period = periodSeconds;
            _clock = clock;
            _clock.Interval = TimeSpan.FromSeconds(1.0 / 30);
            _clock.Tick += (_, _) => Step();
        }

        public bool IsRunning => _clock.IsEnabled;

        public void Start(params (Visual Target, double Min, double Max)[] targets)
        {
            _targets = targets;
            _started = Time.GetTimestamp();
            Step();
            _clock.Start();
        }

        public void Stop()
        {
            _clock.Stop();
            _targets = Array.Empty<(Visual, double, double)>();
        }

        private void Step()
        {
            double k = (1 - Math.Cos(Math.PI * Time.GetElapsedTime(_started).TotalSeconds / _period)) / 2;
            foreach (var (t, min, max) in _targets) t.Opacity = min + (max - min) * k;
        }
    }
}
