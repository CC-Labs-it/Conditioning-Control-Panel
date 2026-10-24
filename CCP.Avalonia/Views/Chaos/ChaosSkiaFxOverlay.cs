using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using IFrameSource = ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Compositor;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Chaos/ChaosSkiaFxOverlay.cs (its legacy keep-alive window path):
    /// one stage-wide, click-through, topmost window drawing the chaos sparkle field with additive bloom -
    /// rabbit trails, pop bursts, E-Stim bolts, ripple shockwaves and the Rabbit-Caller cursor glow.
    ///
    /// <para>The sim and the painter are the Core <see cref="ChaosFxLayer"/> (the WPF compositor path's
    /// layer, moved to Core), so counts, colours, bloom and timing are WPF's by construction. This window
    /// only places it, drives it and draws it: Update rides the window's animation frame (WPF
    /// CompositionTarget.Rendering) and only while the field is live; a frame records the layer into an
    /// immutable SKPicture on the UI thread that the render thread replays (P82).</para>
    ///
    /// <para>Kept from WPF: created on the first FX call, shown on activity, the frame loop parks after
    /// 3 idle frames, the window hides after <see cref="HideGrace"/> of idleness and closes only at
    /// teardown (<see cref="CloseActive"/>: run end, panic, exit). Refused - never created - unless the
    /// X11 overlay is composited and accepts click-through: an opaque or click-eating stage-wide window
    /// would lock the desktop.</para>
    /// </summary>
    internal sealed class ChaosSkiaFxOverlay : Window
    {
        /// <summary>WPF ChaosSkiaFxOverlay.Enabled: the persisted A/B switch (default on).</summary>
        public static bool Enabled => CoreSettings.Current?.ChaosSkiaFxEnabled ?? true;

        /// <summary>Tests: ignore the X11 click-through/override-redirect results (headless has no XID).</summary>
        internal static bool SkipPlatformChecksForTests;
        /// <summary>The compositing probe (tests flip it to prove the refusal).</summary>
        internal static Func<bool> Composited = () => X11Overlay.IsAvailable && X11Overlay.IsCompositing;
        /// <summary>Tests step frames (P08); the live source is the window's animation frame.</summary>
        internal static IFrameSource? FramesForTests;
        internal static ChaosSkiaFxOverlay? Active { get; private set; }
        private static bool _refused;

        /// <summary>WPF HideGrace: the window stays up this long after the field empties, so the next pop
        /// reuses the presented surface instead of re-mapping a stage-wide window.</summary>
        internal static readonly TimeSpan HideGrace = TimeSpan.FromSeconds(8);

        // ---- public API (physical px, like WPF; any thread, marshalled to the UI thread) ----

        public static void TrailDot(Point centerPx, double lifeSec, bool warm = false, double dirX = 0, double dirY = 0) =>
            OnFx(w => w.Fx.EmitTrail(w.Local(centerPx), lifeSec, warm, dirX, dirY));

        public static void Strike(IReadOnlyList<(Point From, Point To)> boltsPx) =>
            OnFx(w => w.Fx.AddStrike(boltsPx.Select(b => (w.Local(b.From), w.Local(b.To))).ToArray()));

        public static void Burst(Point centerPx, Color color, double scale = 1.0) =>
            OnFx(w => w.Fx.EmitBurst(w.Local(centerPx), new SKColor(color.R, color.G, color.B), (float)scale));

        /// <summary>WPF divides the px radius by the DPI scale: the window draws in DIPs.</summary>
        public static void Ripple(Point centerPx, double radiusPx, double lifeMs, bool strong = false) =>
            OnFx(w => w.Fx.EmitRipple(w.Local(centerPx), radiusPx / w._scale, lifeMs, strong));

        public static void ArmCursorGlow() => OnFx(w => w.Fx.ArmCursor());

        /// <summary>WPF run-start prewarm: realize the idle surface without drawing or starting frames.</summary>
        public static void EnsureCreated() => OnFx(_ => { });

        public static void DisarmCursorGlow() => OnUi(() => Active?.Fx.DisarmCursor());

        public static void MoveCursorGlowToPx(double px, double py) =>
            OnFx(w => { var p = w.Local(new Point(px, py)); w.Fx.MoveCursor(p.X, p.Y); });

        /// <summary>Run teardown and panic (WPF ChaosModeService ForceShutdown/EndRun/CleanupAfterRun).
        /// Idempotent, safe when nothing was ever created.</summary>
        public static void CloseActive() => OnUi(() =>
        {
            var w = Active;
            Active = null;
            if (w == null) return;
            w.Fx.Clear();
            w.StopRendering();
            w._hideTimer.Stop();
            try { w.Close(); } catch (Exception ex) { Log.Debug("ChaosSkiaFx close: {E}", ex.Message); }
        });

        private static void OnUi(Action act)
        {
            if (Dispatcher.UIThread.CheckAccess()) act();
            else Dispatcher.UIThread.Post(act);
        }

        private static void OnFx(Action<ChaosSkiaFxOverlay> act)
        {
            if (!Enabled) return;
            OnUi(() =>
            {
                try
                {
                    if (Active == null && Create() is { } w) Active = w;
                    if (Active == null) return;
                    act(Active);
                    if (Active.IsVisible) X11Overlay.Raise(Active);   // WPF StartActivity: ChaosWindowZ.RaiseAboveVideo on every effect
                }
                catch (Exception ex) { Log.Debug("ChaosSkiaFx: {E}", ex.Message); }
            });
        }

        /// <summary>The window, or null when the platform cannot host a transparent click-through overlay.</summary>
        private static ChaosSkiaFxOverlay? Create()
        {
            if (_refused || !Composited())
            {
                Log.Debug("ChaosSkiaFx: no composited click-through overlay on this platform, so no chaos sparkle field");
                return null;
            }
            var w = new ChaosSkiaFxOverlay(live: true);
            if (!X11Overlay.SetClickThrough(w, true) | !X11Overlay.SetOverrideRedirect(w, w._stagePx) && !SkipPlatformChecksForTests)
            {
                Log.Warning("ChaosSkiaFx: the platform refused a click-through topmost overlay, so no chaos sparkle field");
                _refused = true;
                try { w.Close(); } catch { }
                return null;
            }
            return w;
        }

        // ---- instance ----

        internal readonly ChaosFxLayer Fx;
        private readonly DispatcherTimer _hideTimer;
        private readonly Action<TimeSpan> _onFrame;
        private IFrameSource? _frames;
        private TimeSpan _last = TimeSpan.MinValue;
        private int _idleFrames;
        private double _scale = 1;
        private PixelRect _stagePx;

        /// <summary>True while the frame loop is subscribed (tests).</summary>
        internal bool IsRendering => _frames != null;
        internal bool HidePending => _hideTimer.IsEnabled;

        /// <summary>Render-all sample: a burst, a strong ripple, a bolt and the cursor glow, mid-flight.</summary>
        internal ChaosSkiaFxOverlay() : this(live: false)
        {
            Width = 640;
            Height = 400;
            Fx.EmitBurst(new SKPoint(200, 200), new SKColor(0xFF, 0x4D, 0xC4), 1.4f);
            Fx.EmitRipple(new SKPoint(420, 200), 160, 700, strong: true);
            Fx.AddStrike(new[] { (new SKPoint(80, 60), new SKPoint(300, 120)) });
            Fx.EmitTrail(new SKPoint(520, 320), 0.5, warm: true, 1, -0.3);
            Fx.ArmCursor();
            Fx.MoveCursor(560, 90);
            Fx.Update(TimeSpan.FromSeconds(0.08));
        }

        private ChaosSkiaFxOverlay(bool live)
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            Fx = new ChaosFxLayer(OnWake, rng: live ? null : new Random(7));
            Content = new FxSurface(Fx);
            _onFrame = OnFrame;
            _hideTimer = new DispatcherTimer { Interval = HideGrace };
            _hideTimer.Tick += (_, _) => HideTick();
            if (live) PlaceOnStage();
        }

        /// <summary>WPF ChaosWindowZ.StageBounds: the virtual desktop when dual-monitor is on, else the primary.</summary>
        private void PlaceOnStage()
        {
            var all = Screens.All;
            var primary = Screens.Primary ?? all.FirstOrDefault();
            if (primary == null) return;
            _scale = primary.Scaling > 0 ? primary.Scaling : 1;
            var b = primary.Bounds;
            if (CoreSettings.Current?.DualMonitorEnabled ?? true)
                foreach (var s in all) b = b.Union(s.Bounds);
            _stagePx = b;   // geometry is applied by SetOverrideRedirect (X11Overlay: placing first lets KWin replay a stale size)
        }

        /// <summary>WPF Local(): physical px to this window's DIPs.</summary>
        private SKPoint Local(Point px) =>
            new((float)((px.X - _stagePx.X) / _scale), (float)((px.Y - _stagePx.Y) / _scale));

        /// <summary>The layer flips active on its first emit after idling: WPF StartActivity.</summary>
        private void OnWake()
        {
            if (Fx?.IsActive == true && ReferenceEquals(Active, this)) StartActivity();
        }

        private void StartActivity()
        {
            _hideTimer.Stop();
            if (!IsVisible)
            {
                try { Show(); } catch (Exception ex) { Log.Debug("ChaosSkiaFx show: {E}", ex.Message); }
            }
            _idleFrames = 0;
            if (_frames != null) return;
            _frames = FramesForTests ?? TopLevelFrameSource.For(this);
            _last = TimeSpan.MinValue;
            _frames.Subscribe(_onFrame);
        }

        private void StopRendering()
        {
            _frames?.Unsubscribe(_onFrame);
            _frames = null;
        }

        /// <summary>WPF OnRendering: frame delta (first frame 1/60 s, spikes clamped to 0.1 s), step the
        /// field, repaint, and park the loop after 3 idle frames with the hide grace armed.</summary>
        internal void OnFrame(TimeSpan now)
        {
            float dt = 1f / 60f;
            if (_last == TimeSpan.MinValue) _last = now;
            else
            {
                dt = (float)(now - _last).TotalSeconds;
                _last = now;
            }
            if (dt <= 0) return;
            if (dt > 0.1f) dt = 0.1f;

            Fx.Update(TimeSpan.FromSeconds(dt));
            (Content as Control)?.InvalidateVisual();

            if (!Fx.IsActive)
            {
                if (++_idleFrames > 2) { StopRendering(); _hideTimer.Stop(); _hideTimer.Start(); }
            }
            else _idleFrames = 0;
        }

        internal void HideTick()
        {
            _hideTimer.Stop();
            if (!IsRendering) { try { Hide(); } catch { } }
        }

        /// <summary>Records the layer into an immutable picture on the UI thread; the render thread replays it.</summary>
        private sealed class FxSurface : Control
        {
            private readonly ChaosFxLayer _fx;

            public FxSurface(ChaosFxLayer fx) { _fx = fx; IsHitTestVisible = false; }

            public override void Render(DrawingContext context)
            {
                var b = new Rect(Bounds.Size);
                if (b.Width <= 0 || b.Height <= 0) return;
                using var rec = new SKPictureRecorder();
                var canvas = rec.BeginRecording(new SKRect(0, 0, (float)b.Width, (float)b.Height));
                _fx.Render(canvas, new SKRectI(0, 0, (int)b.Width, (int)b.Height), 1, TimeSpan.Zero);
                context.Custom(new PictureOp(rec.EndRecording(), b));
            }
        }

        private sealed class PictureOp : ICustomDrawOperation
        {
            private readonly SKPicture _picture;

            public PictureOp(SKPicture picture, Rect bounds) { _picture = picture; Bounds = bounds; }

            public Rect Bounds { get; }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() => _picture.Dispose();

            public void Render(ImmediateDrawingContext context)
            {
                var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (lease is null) return;
                using var leased = lease.Lease();
                leased.SkCanvas.DrawPicture(_picture);
            }
        }
    }
}
