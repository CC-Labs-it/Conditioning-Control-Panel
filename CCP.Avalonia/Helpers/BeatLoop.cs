using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ConditioningControlPanel.Avalonia.Controls.Fx;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// IMPORTED from trunk (CCP.Avalonia/Helpers/BeatLoop.cs, 66ca21fd3; trimmed to what is used).
    /// An endless ambient loop on the window's shared 30 fps beat (<see cref="FrameClock"/>). An
    /// infinite Avalonia Animation ticks at render rate and keeps the whole window composing 60
    /// frames a second (WPF caps these loops at 30: MainWindow.ProfileFx.cs AmbientFrameRate); and
    /// <c>Animation.RunAsync(aTransform)</c> throws InvalidCastException outright. <c>step</c> gets
    /// the seconds since Start and writes plain properties (a Transform's Angle or Scale).
    /// </summary>
    internal sealed class BeatLoop
    {
        /// <summary>Where elapsed time comes from; tests step it (P08).</summary>
        internal static TimeProvider Time = TimeProvider.System;

        // Running loops, so a test can tick them after stepping Time. A running FrameClock is
        // rooted by its frame source anyway, so this adds no lifetime.
        private static readonly HashSet<BeatLoop> Live = new();

        private readonly FrameClock _clock;
        private readonly Action<double> _step;
        private long _started;

        public BeatLoop(Visual owner, Action<double> step)
        {
            _step = step;
            _clock = new FrameClock(owner) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
            _clock.Tick += (_, _) => Step();
        }

        public bool IsRunning => _clock.IsEnabled;

        public void Start()
        {
            if (IsRunning) return;
            _started = Time.GetTimestamp();
            _step(0);
            _clock.Start();
            Live.Add(this);
        }

        public void Stop()
        {
            _clock.Stop();
            Live.Remove(this);
        }

        private void Step() => _step(Time.GetElapsedTime(_started).TotalSeconds);

        /// <summary>One beat for every running loop (tests, after stepping <see cref="Time"/>).</summary>
        internal static void StepAll()
        {
            foreach (var loop in Live.ToArray()) loop.Step();
        }

        /// <summary>0 -> 1 -> 0 over 2 x <paramref name="period"/> seconds: Alternate + SineEaseInOut.</summary>
        public static double Breath(double t, double period) => (1 - Math.Cos(Math.PI * t / period)) / 2;
    }
}
