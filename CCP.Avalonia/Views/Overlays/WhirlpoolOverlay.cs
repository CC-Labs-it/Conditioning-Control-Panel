using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The Whirlpool on this head: the spiral's Super look (WPF <c>Services/Compositor/WhirlpoolLayer</c>).
    /// The rules and the picture are Core's (<see cref="SuperStageHost"/> steps the pool with the
    /// restless, stay-centred and click rules, <see cref="WhirlpoolLook"/> picks the Loom spirals it
    /// wears, <see cref="WhirlpoolDraw"/> draws it); this class is the stage's pool surface switch, a
    /// 30 fps clock, and a <see cref="WhirlpoolCanvas"/> laid over each spiral window's picture.
    ///
    /// <para><b>Rides the spiral windows</b> (click-through, per-pixel transparency, the spiral's
    /// monitor target), so the pool can never show where the spiral could not. While it runs as a
    /// real unlock the base spiral steps aside behind <see cref="SpiralOverlay.Veil"/> (WPF
    /// <c>SpiralLayer.SetStepAside</c>), and the spiral windows are raised over the tint windows,
    /// so the pool reads through the fog (WPF <c>CompositorLayers.Whirlpool</c> 72 over the tint's 70).</para>
    ///
    /// <para><b>Threads.</b> The stage steps on the UI thread, the pool is drawn on the render
    /// thread; both take <see cref="SteamOverlay.Gate"/>, like the fog.</para>
    /// </summary>
    internal static class WhirlpoolOverlay
    {
        /// <summary>The stage's pool surface is on.</summary>
        internal static bool On { get; private set; }

        /// <summary>The Gentle visuals fade in, 0..1 (1 while Gentle is off). Off cuts, as on WPF.</summary>
        internal static double Alpha { get; private set; } = 1;

        /// <summary>The Loom spirals the pool wears. UI thread, under the gate.</summary>
        internal static readonly WhirlpoolLook Look = new(new Random());

        /// <summary>One draw for every monitor (the Loom picture is worked out once a frame). Under the gate.</summary>
        internal static readonly WhirlpoolDraw Painter = new();

        private static DispatcherTimer? _timer;
        private static long _last;
        private static Window[] _raisedOver = Array.Empty<Window>();

        internal static bool IsTicking => _timer != null;

        /// <summary>Called once at startup, after <see cref="SteamOverlay.Seed"/> (which seeds the shared seams). Idempotent, no latch.</summary>
        internal static void Seed() => SuperStageHost.PoolSurface = Set;

        /// <summary>The stage's pool surface switch. UI thread. Idempotent: it also dresses spiral
        /// windows opened since the last call and lifts them over tint windows that came later.</summary>
        internal static void Set(bool on)
        {
            bool was = On;
            On = on;
            foreach (var w in SpiralOverlay.Shown) w.SetPool(on);
            SpiralOverlay.PushStepAside();
            if (on)
            {
                if (!was)
                {
                    Alpha = GentleVisuals.On ? 0 : 1;
                    _raisedOver = Array.Empty<Window>();
                }
                RaiseOverFog();
                Start();
            }
            else if (!SuperStageHost.EngineUp())
            {
                // Panic or the engine stop: gone at once, nothing left to fade back.
                Halt();
                SpiralOverlay.SnapVeil();
            }
            else Start();   // the base spiral comes back from behind its veil
        }

        internal static void Start()
        {
            if (_timer != null) return;
            _last = 0;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, Tick);
            _timer.Start();
        }

        private static void Halt()
        {
            _timer?.Stop();
            _timer = null;
        }

        /// <summary>One frame. Internal for tests (headless has no running timer).</summary>
        internal static void Tick(object? sender, EventArgs e)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                double dt = _last == 0 ? 0 : Math.Min((now - _last) / (double)Stopwatch.Frequency, 0.1);
                _last = now;
                Frame(dt);
            }
            catch (Exception ex) { Log.Debug("Whirlpool: tick failed: {E}", ex.Message); }
        }

        /// <summary>One frame of <paramref name="dt"/> seconds: step the stage (unless the fog's
        /// clock already does: one step a frame, as on WPF), the look, the fade and the base's veil.</summary>
        internal static void Frame(double dt)
        {
            var stage = SuperStageHost.Stage;
            bool live = On && stage.PoolOn;
            bool due = false;
            if (live)
            {
                var set = CoreSettings.Current;
                lock (SteamOverlay.Gate)
                {
                    if (!SteamOverlay.On) SuperStageHost.Step();
                    var pool = stage.Pool;
                    Look.Step(dt, pool.Size > WhirlpoolLook.NothingSize, GentleVisuals.On, set.WhirlpoolSpiral,
                        set.WhirlpoolNewEvery, WhirlpoolLook.FindSaved);
                    due = Look.Due(dt, pool.Size);
                }
                if (Alpha < 1)
                {
                    Alpha = GentleVisuals.On ? Math.Min(1, Alpha + dt / GentleVisuals.FadeSeconds) : 1;
                    due = true;
                }
            }
            SpiralOverlay.StepVeil(dt);
            if (due) foreach (var w in SpiralOverlay.Shown) w.InvalidatePool();
            if (!live && SpiralOverlay.VeilSettled) Halt();
        }

        /// <summary>Lift the spiral windows over the tint windows once per new arrangement of either.</summary>
        private static void RaiseOverFog()
        {
            if (PinkFilterOverlay.Shown.Count == 0 || SpiralOverlay.Shown.Count == 0) return;
            var key = PinkFilterOverlay.Shown.Cast<Window>().Concat(SpiralOverlay.Shown).ToArray();
            if (key.SequenceEqual(_raisedOver)) return;
            _raisedOver = key;
            RaisePool();
        }

        /// <summary>The pool goes over the fog: <see cref="PinkFilterOverlay.SyncOrder"/> calls this after it re-sorts.</summary>
        internal static void RaisePool()
        {
            if (!On) return;
            foreach (var w in SpiralOverlay.Shown)
            {
                try { PinkFilterOverlay.RaiseWindow(w); }
                catch (Exception ex) { Log.Debug("Whirlpool: raise failed: {E}", ex.Message); }
            }
        }
    }

    /// <summary>One monitor's pool, over the spiral window's picture.</summary>
    internal sealed class WhirlpoolCanvas : Control
    {
        private readonly PixelRect _px;
        private readonly double _scale;
        private readonly Whirlpool? _sample;

        /// <param name="px">The monitor in virtual-desktop device pixels (what the cursor is read in).</param>
        /// <param name="scale">Device pixels per DIP on that monitor.</param>
        public WhirlpoolCanvas(PixelRect px, double scale)
        {
            _px = px;
            _scale = scale > 0 ? scale : 1;
            IsHitTestVisible = false;
        }

        /// <summary>--render-all only (RenderProof builds every view with no arguments): one
        /// 1280x720 monitor at scale 1 with its own pool, rested 10 s mid-screen (the stage is off
        /// there). Internal, so no production caller ships the sample.</summary>
        internal WhirlpoolCanvas() : this(new PixelRect(0, 0, 1280, 720), 1)
        {
            _sample = new Whirlpool();
            for (int i = 0; i < 300; i++) _sample.Step(1 / 30.0, 640, 360, 0, 1, 720);
        }

        public override void Render(DrawingContext context)
            => context.Custom(new Op(this, new Rect(Bounds.Size)));

        /// <summary>Render thread, under the gate. The canvas arrives in this control's DIPs; the
        /// pool lives in desktop pixels, so it is drawn in those and scaled back. True when it drew.</summary>
        internal bool Draw(SKCanvas canvas)
        {
            lock (SteamOverlay.Gate)
            {
                var rect = new System.Drawing.Rectangle(_px.X, _px.Y, _px.Width, _px.Height);
                SuperStageHost.NoteScreen(rect, _scale);
                var stage = SuperStageHost.Stage;
                var pool = _sample ?? stage.Pool;
                if ((_sample == null && !stage.PoolOn) || pool.Size <= WhirlpoolLook.NothingSize) return false;
                if (!WhirlpoolLook.Reaches(pool, rect.Left, rect.Top, rect.Right, rect.Bottom)) return false;
                var look = WhirlpoolOverlay.Look;
                canvas.Save();
                try
                {
                    canvas.Scale((float)(1 / _scale));
                    canvas.Translate(-rect.X, -rect.Y);
                    WhirlpoolOverlay.Painter.Draw(canvas, pool, WhirlpoolLook.Strength(CoreSettings.Current.SpiralOpacity),
                        look.Morph.From, look.Morph.To, look.Morph.Mix, look.Phase, WhirlpoolOverlay.Alpha);
                }
                finally { canvas.Restore(); }
                return true;
            }
        }

        private sealed class Op : ICustomDrawOperation
        {
            private readonly WhirlpoolCanvas _owner;
            public Op(WhirlpoolCanvas owner, Rect bounds) { _owner = owner; Bounds = bounds; }
            public Rect Bounds { get; }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (lease == null) return;
                using var leased = lease.Lease();
                try { _owner.Draw(leased.SkCanvas); }
                catch (Exception ex) { Log.Debug("Whirlpool: draw failed: {E}", ex.Message); }
            }
        }
    }
}
