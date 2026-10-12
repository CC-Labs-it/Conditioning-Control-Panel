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
        private readonly Visual _owner;
        private readonly Action<double> _step;
        private long _started;

        public BeatLoop(Visual owner, Action<double> step)
        {
            _owner = owner;
            _step = step;
            _clock = new FrameClock(owner) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
            _clock.Tick += (_, _) => { if (!Parked) Step(); };
        }

        /// <summary>A beat writes nothing while its owner is hidden or its window minimised, so the
        /// window has nothing to compose (WPF renders nothing for a minimised window either). Time
        /// keeps running, so the loop resumes in phase.</summary>
        internal bool Parked =>
            !_owner.IsEffectivelyVisible
            || global::Avalonia.Controls.TopLevel.GetTopLevel(_owner) is global::Avalonia.Controls.Window { WindowState: global::Avalonia.Controls.WindowState.Minimized };

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

        /// <summary>A WPF keyframe track (DoubleAnimationUsingKeyFrames) at <paramref name="t"/>: keys sorted by
        /// time, held flat before the first and after the last; each segment Linear, or SineEase InOut
        /// when <paramref name="sine"/> (EasingDoubleKeyFrame).</summary>
        public static double Keys(double t, (double At, double V)[] keys, bool sine = false)
        {
            if (t <= keys[0].At) return keys[0].V;
            for (int i = 1; i < keys.Length; i++)
            {
                var (a, b) = (keys[i - 1], keys[i]);
                if (t > b.At) continue;
                double p = b.At > a.At ? (t - a.At) / (b.At - a.At) : 1;
                if (sine) p = (1 - Math.Cos(Math.PI * p)) / 2;
                return a.V + (b.V - a.V) * p;
            }
            return keys[^1].V;
        }
    }
}
