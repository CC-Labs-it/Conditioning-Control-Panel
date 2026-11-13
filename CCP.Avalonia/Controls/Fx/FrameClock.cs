using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Motion;

namespace ConditioningControlPanel.Avalonia.Controls.Fx
{
    /// <summary>
    /// Where frame callbacks come from. The live one is <see cref="TopLevelFrameSource"/>
    /// (Avalonia's own animation frame, one per composed frame); tests inject a fake and step it.
    /// The timestamp handed to a subscriber is the frame's render time.
    /// </summary>
    public interface IFrameSource
    {
        void Subscribe(Action<TimeSpan> onFrame);
        void Unsubscribe(Action<TimeSpan> onFrame);
    }

    /// <summary>
    /// IMPORTED from trunk (614a56560, 073afa0aa, a59d831c2; CCP_FRAME_STATS dropped). PORTED from
    /// ConditioningControlPanel/Controls/FrameClock.cs (perf pass, 2026-10-07).
    ///
    /// <para>A frame-locked replacement for a DispatcherTimer driving an effect. WPF ticked from
    /// CompositionTarget.Rendering; the Avalonia twin rides <see cref="TopLevel.RequestAnimationFrame"/>
    /// through one shared <see cref="TopLevelFrameSource"/> per window, so every tick lands right
    /// before a frame is composed, and it skips whole frames to hold <see cref="Interval"/>: at
    /// 30 fps on a 60 Hz screen that is exactly every second frame, where a free-running 33 ms timer
    /// drifts against the refresh and shows as an uneven step (judder) however fast the PC is.</para>
    ///
    /// <para>Same surface as the timer it replaces: Interval, IsEnabled, Start, Stop, Tick. The
    /// window's frame request stops as soon as no clock listens, so a parked effect costs nothing.</para>
    ///
    /// <para>Off a window (owner not attached yet, or no owner given) it falls back to a 16 ms
    /// dispatcher pump: still ticks, never frame-locked. Everything that matters starts its clock
    /// from Loaded, so the fallback is a safety net, not a path.</para>
    /// </summary>
    public sealed class FrameClock
    {
        private readonly FrameGate _gate = new();
        private readonly Visual? _owner;
        private readonly IFrameSource? _injected;
        private IFrameSource? _source;
        private readonly Action<TimeSpan> _onFrame;

        /// <summary>A clock that finds its window from <paramref name="owner"/> at every Start.</summary>
        public FrameClock(Visual owner) : this() => _owner = owner;

        /// <summary>A clock on an explicit frame source (tests, benches).</summary>
        public FrameClock(IFrameSource source) : this() => _injected = source;

        private FrameClock() => _onFrame = OnFrame;

        public TimeSpan Interval
        {
            get => _gate.Interval;
            set => _gate.Interval = value;
        }

        public bool IsEnabled { get; private set; }

        public event EventHandler? Tick;

        /// <summary>The visual this clock was made for (CCP_FRAME_STATS names the running clocks by it).</summary>
        internal Visual? Owner => _owner;

        /// <summary>True when the last Start found a window to lock to (tests, bench).</summary>
        internal bool IsFrameLocked => _source is TopLevelFrameSource;

        public void Start()
        {
            if (IsEnabled) return;
            IsEnabled = true;
            _gate.Reset();
            _source = _injected ?? Resolve();
            _source.Subscribe(_onFrame);
        }

        public void Stop()
        {
            if (!IsEnabled) return;
            IsEnabled = false;
            _source?.Unsubscribe(_onFrame);
            _source = null;
        }

        private IFrameSource Resolve()
        {
            var top = _owner == null ? null : TopLevel.GetTopLevel(_owner);
            // The first frame-locked effect on a window asks which Skia backend draws it (lane perf).
            if (top != null) Platform.SoftwareRender.Probe(top);
            return top != null ? TopLevelFrameSource.For(top) : DispatcherFrameSource.Shared;
        }

        private void OnFrame(TimeSpan now)
        {
            if (!IsEnabled) return;
            if (_source is TopLevelFrameSource src && src.Gated(this)) return;
            bool due = _source is TopLevelFrameSource top ? top.BeatDue(_gate, now) : _gate.Due(now);
            if (!due) return;
            Tick?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// One per window: asks the window for the next animation frame while anybody listens, and
    /// fans the frame out to every subscribed <see cref="FrameClock"/>. The request is one-shot in
    /// Avalonia, so it is re-armed after each frame only while the list is not empty: the last
    /// Unsubscribe lets the window go idle.
    /// </summary>
    public sealed class TopLevelFrameSource : IFrameSource
    {
        private static readonly ConditionalWeakTable<TopLevel, TopLevelFrameSource> Table = new();

        private readonly WeakReference<TopLevel> _top;
        private readonly List<Action<TimeSpan>> _subs = new();
        private Action<TimeSpan>[] _snapshot = Array.Empty<Action<TimeSpan>>();
        private bool _requested;
        private readonly Action<TimeSpan> _callback;

        private TopLevelFrameSource(TopLevel top)
        {
            _top = new WeakReference<TopLevel>(top);
            _callback = OnFrame;
        }

        public static TopLevelFrameSource For(TopLevel top) => Table.GetValue(top, t => new TopLevelFrameSource(t));

        /// <summary>Clocks listening right now (tests).</summary>
        internal int SubscriberCount => _subs.Count;

        /// <summary>The clocks listening right now, as "owner type@interval" (CCP_FRAME_STATS).</summary>
        internal string DescribeSubscribers() => string.Join(", ",
            System.Linq.Enumerable.Select(_snapshot, s => s.Target is FrameClock c
                ? $"{c.Owner?.GetType().Name ?? "?"}{(c.Owner is StyledElement { Name: { } n } ? "#" + n : "")}@{c.Interval.TotalMilliseconds:F0}ms"
                : s.Target?.GetType().Name ?? "?"));

        /// <summary>True while a frame request is outstanding (tests).</summary>
        internal bool IsRequesting => _requested;

        public void Subscribe(Action<TimeSpan> onFrame)
        {
            if (_subs.Contains(onFrame)) return;
            _subs.Add(onFrame);
            _snapshot = _subs.ToArray();
            Request();
        }

        public void Unsubscribe(Action<TimeSpan> onFrame)
        {
            if (_subs.Remove(onFrame)) _snapshot = _subs.ToArray();
        }

        /// <summary>Frames requested by every window's clocks since start (PerfBench idle rate, tests).</summary>
        internal static long FrameRequests;

        private void Request()
        {
            _park?.Stop();
            Wake(false);
            if (_requested || _subs.Count == 0) return;
            if (!_top.TryGetTarget(out var top)) return;
            _requested = true;
            System.Threading.Interlocked.Increment(ref FrameRequests);
            top.RequestAnimationFrame(_callback);
        }

        /// <summary>True when <paramref name="clock"/>'s owner draws nothing on this window: detached or moved off it,
        /// hidden (itself or any ancestor: a hidden tab or page), or the window minimised. Such a clock skips its
        /// ticks and asks for no frames, the way <c>BeatLoop.Parked</c> does for the ambient loops.</summary>
        internal bool Gated(FrameClock clock) =>
            clock.Owner is { } o && _top.TryGetTarget(out var top)
            && (TopLevel.GetTopLevel(o) != top || !o.IsEffectivelyVisible
                || top is Window { WindowState: WindowState.Minimized });

        private bool _asleep;

        // Every listener is gated: nothing to draw, so no frames and no timer. Whatever ungates a clock (a show,
        // an attach, a tab switch) runs a layout pass, and a restore changes WindowState; either wakes the source.
        private void Sleep(TopLevel top)
        {
            if (_asleep) return;
            _asleep = true;
            top.LayoutUpdated += OnWake;
            top.PropertyChanged += OnTopChanged;
        }

        private void OnWake(object? sender, EventArgs e) => Wake(true);

        private void OnTopChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty || e.Property == Visual.IsVisibleProperty) Wake(true);
        }

        private void Wake(bool request)
        {
            if (!_asleep) return;
            _asleep = false;
            if (_top.TryGetTarget(out var top))
            {
                top.LayoutUpdated -= OnWake;
                top.PropertyChanged -= OnTopChanged;
            }
            if (request) Request();
        }

        /// <summary>True while every listener is gated and the source waits for a show or restore (tests).</summary>
        internal bool IsAsleep => _asleep;

        // ---- the shared beat (Home lag, 2026-10-09) -------------------------------------------
        //
        // Every clock used to gate on its own FrameGate, so two 30 fps clocks on one window (the
        // Home fog and the logo dial) could tick on ALTERNATE 60 Hz frames: the compositor then ran
        // 60 times a second and each frame repainted the whole dashboard under the fog. Clocks of
        // the same rate on one window now share one gate, so they tick on the same frames and the
        // window composes 30 frames a second. A clock that starts mid-beat waits for the next one
        // (at most one interval); a clock alone behaves exactly as before.

        private readonly Dictionary<long, (FrameGate Gate, TimeSpan Frame, bool Due)> _beats = new();

        /// <summary>True when the beat for <paramref name="own"/>'s interval is due on this frame.</summary>
        internal bool BeatDue(FrameGate own, TimeSpan now)
        {
            long key = (long)Math.Round(own.Interval.TotalMilliseconds);
            if (!_beats.TryGetValue(key, out var beat))
                beat = (new FrameGate { Interval = own.Interval }, TimeSpan.MinValue, false);
            if (beat.Frame != now)
            {
                beat = (beat.Gate, now, beat.Gate.Due(now));
                if (beat.Due && Platform.SoftwareRender.FrameStats) System.Threading.Interlocked.Increment(ref Platform.SoftwareRender.BeatTicks);
            }
            _beats[key] = beat;
            return beat.Due;
        }

        private void OnFrame(TimeSpan now)
        {
            foreach (var s in _snapshot)
            {
                try { s(now); }
                catch (Exception ex) { Serilog.Log.Debug("FrameClock subscriber: {E}", ex.Message); }
            }
            // Re-armed from inside the callback, the way Avalonia means it: MediaContext queues it for
            // the next pulse and its animation timer (or the compositor) brings that pulse round.
            // Posting the re-arm instead schedules an immediate render op and spins the dispatcher.
            _requested = false;
            var wait = UntilNextBeat(now);
            if (wait <= TimeSpan.Zero) { Request(); return; }
            if (wait == TimeSpan.MaxValue)
            {
                if (_subs.Count > 0 && _top.TryGetTarget(out var top)) Sleep(top);
                return;
            }
            // Nothing is due before the next beat: park instead of asking for every frame in between.
            // Avalonia's X11 loop runs at the FASTEST monitor's refresh (360 Hz on the owner's PC), so 30 fps
            // clocks re-arming each frame kept it at 360 ticks/s for 30 useful ones (real-app-perf 2026-11-11).
            // WPF's DesiredFrameRate throttles its render loop the same way. An idle loop restarts on request,
            // so the frame lands right at the beat.
            if (_park == null)
            {
                _park = new DispatcherTimer(DispatcherPriority.Render);
                _park.Tick += (_, _) => Request();
            }
            _park.Interval = wait;
            _park.Start();
        }

        private DispatcherTimer? _park;

        /// <summary>True while the source waits on a timer for the next beat instead of a frame (tests).</summary>
        internal bool IsParked => _park?.IsEnabled == true;

        /// <summary>The one-shot timer a parked source waits on (tests).</summary>
        internal DispatcherTimer? ParkTimer => _park;

        /// <summary>How long after <paramref name="now"/> the first listener can tick; zero when one may tick on
        /// the very next frame (a raw subscriber, a clock that never ticked, or a beat within 2 ms);
        /// <see cref="TimeSpan.MaxValue"/> when every listener is gated.</summary>
        internal TimeSpan UntilNextBeat(TimeSpan now)
        {
            var due = TimeSpan.MaxValue;
            foreach (var s in _snapshot)
            {
                if (s.Target is not FrameClock c) return TimeSpan.Zero;
                if (!c.IsEnabled || Gated(c)) continue;
                if (!_beats.TryGetValue((long)Math.Round(c.Interval.TotalMilliseconds), out var beat)) return TimeSpan.Zero;
                var next = beat.Gate.NextDue;
                if (next == TimeSpan.MinValue) return TimeSpan.Zero;
                if (next < due) due = next;
            }
            if (due == TimeSpan.MaxValue) return due;
            // +1 ms so a timer that fires a hair early does not bring a frame that is not due yet.
            var wait = due - now + TimeSpan.FromMilliseconds(1);
            return wait > TimeSpan.FromMilliseconds(2) ? wait : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// The off-window fallback: a 16 ms dispatcher pump stamped with a stopwatch. Shared, and
    /// stopped when the last clock leaves, like the window source.
    /// </summary>
    internal sealed class DispatcherFrameSource : IFrameSource
    {
        public static readonly DispatcherFrameSource Shared = new();

        private readonly List<Action<TimeSpan>> _subs = new();
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        private DispatcherTimer? _timer;

        public void Subscribe(Action<TimeSpan> onFrame)
        {
            if (_subs.Contains(onFrame)) return;
            _subs.Add(onFrame);
            if (_timer == null)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
                _timer.Tick += (_, _) =>
                {
                    var now = _sw.Elapsed;
                    foreach (var s in _subs.ToArray())
                    {
                        try { s(now); }
                        catch (Exception ex) { Serilog.Log.Debug("FrameClock fallback subscriber: {E}", ex.Message); }
                    }
                };
            }
            if (!_timer.IsEnabled) _timer.Start();
        }

        public void Unsubscribe(Action<TimeSpan> onFrame)
        {
            _subs.Remove(onFrame);
            if (_subs.Count == 0) _timer?.Stop();
        }
    }
}
