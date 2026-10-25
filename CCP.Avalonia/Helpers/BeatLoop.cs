using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.VisualTree;
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
        private bool _running;
        private Action? _unwatch;
        private global::Avalonia.Controls.TopLevel? _top;

        public BeatLoop(Visual owner, Action<double> step, double fps = 30)
        {
            _owner = owner;
            _step = step;
            _clock = new FrameClock(owner) { Interval = TimeSpan.FromSeconds(1.0 / fps) };
            _clock.Tick += (_, _) => { if (Parked) _clock.Stop(); else Step(); };
        }

        /// <summary>While its owner is hidden or its window minimised a loop parks: the clock stops,
        /// so the window neither requests frames nor has anything to compose (WPF renders nothing
        /// for a minimised window either). An IsVisible / WindowState change on the owner or any
        /// ancestor parks or wakes it; time keeps running, so it resumes in phase.</summary>
        internal bool Parked =>
            !_owner.IsEffectivelyVisible
            || global::Avalonia.Controls.TopLevel.GetTopLevel(_owner) is global::Avalonia.Controls.Window { WindowState: global::Avalonia.Controls.WindowState.Minimized };

        /// <summary>True while the beat's clock is actually subscribed (not parked).</summary>
        internal bool IsTicking => _clock.IsEnabled;

        /// <summary>Ticking on its window's frames, not the off-window dispatcher fallback (tests).</summary>
        internal bool OnFrameBeat => _clock.IsEnabled && _clock.IsFrameLocked;

        /// <summary>What the loop parks with (tests).</summary>
        internal Visual Owner => _owner;

        /// <summary>Every started loop (tests).</summary>
        internal static IReadOnlyCollection<BeatLoop> Running => Live;

        /// <summary>Started and not stopped; a parked loop is still running.</summary>
        public bool IsRunning => _running;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _started = Time.GetTimestamp();
            _step(0);
            Live.Add(this);
            Arm();
        }

        private void Arm()
        {
            // Not in a window yet (content of a ScrollViewer whose template is not applied): a clock
            // started now would fall back to the off-window dispatcher pump for good, and the watch
            // below would miss the window. Wait for the attach (lag-fixes-c review).
            if (global::Avalonia.Controls.TopLevel.GetTopLevel(_owner) == null)
            {
                void Attached(object? s, VisualTreeAttachmentEventArgs e) { Unwatch(); if (_running) Arm(); }
                _owner.AttachedToVisualTree += Attached;
                _unwatch = () => _owner.AttachedToVisualTree -= Attached;
                return;
            }
            // Watch the owner and its ancestors (the window included) for IsVisible / WindowState,
            // so a hide or a minimise parks the clock at once and a show wakes it.
            var chain = _owner.GetSelfAndVisualAncestors().ToArray();
            void On(object? s, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == Visual.IsVisibleProperty || e.Property == global::Avalonia.Controls.Window.WindowStateProperty) Sync();
            }
            foreach (var a in chain) a.PropertyChanged += On;
            _unwatch = () => { foreach (var a in chain) a.PropertyChanged -= On; };
            Sync();
        }

        public void Stop()
        {
            _running = false;
            _clock.Stop();
            Unwatch();
            Live.Remove(this);
        }

        private void Sync()
        {
            if (!_running) return;
            var top = global::Avalonia.Controls.TopLevel.GetTopLevel(_owner);
            // A late Activated (headless posts it) can restart a loop on a window already closed.
            if (top is { PlatformImpl: null }) { Stop(); return; }
            // Watched even while parked: a loop that starts parked (hidden tab) is still in Live.
            WatchTop(top);
            if (Parked) _clock.Stop();
            else _clock.Start();
        }

        // A closed window never hides its content, so nothing above parks the loop: its clock kept
        // re-arming frames on the shared compositor and Live kept it, so every closed window with an
        // ambient loop stayed alive with all its Skia surfaces (suite-memory, 2026-10-12). Re-read on
        // every Sync because an owner can start detached and be attached later.
        private void WatchTop(global::Avalonia.Controls.TopLevel? top)
        {
            if (top == _top) return;
            if (_top != null) _top.Closed -= OnTopClosed;
            _top = top;
            if (_top != null) _top.Closed += OnTopClosed;
        }

        private void OnTopClosed(object? sender, EventArgs e) => Stop();

        private void Unwatch()
        {
            _unwatch?.Invoke();
            _unwatch = null;
            WatchTop(null);
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
