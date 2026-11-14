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
    /// per-monitor, click-through, topmost windows drawing the chaos sparkle field with additive bloom -
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
        internal static IReadOnlyList<PixelRect>? AvailableMonitorBoundsForTests;
        internal static double? MonitorScaleForTests;
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

        /// <summary>Radius is in physical pixels, as on the WPF compositor path.</summary>
        public static void Ripple(Point centerPx, double radiusPx, double lifeMs, bool strong = false) =>
            OnFx(w => w.Fx.EmitRipple(w.Local(centerPx), radiusPx, lifeMs, strong));

        public static void ArmCursorGlow() => OnFx(w => w.Fx.ArmCursor());

        /// <summary>WPF run-start prewarm: realize the idle surface without drawing or starting frames.</summary>
        public static void EnsureCreated() => OnFx(_ => { });

        public static void DisarmCursorGlow() => OnUi(() => Active?.Fx.DisarmCursor());

        public static void MoveCursorGlowToPx(double px, double py) =>
            OnFx(w => { var p = w.Local(new Point(px, py)); w.Fx.MoveCursor(p.X, p.Y); });

        /// <summary>Run teardown and panic (WPF ChaosModeService ForceShutdown/EndRun/CleanupAfterRun).
        /// Idempotent, safe when nothing was ever created. A platform refusal is remembered until
        /// here, like the other chaos overlays, so one refused run never disables later runs.</summary>
        public static void CloseActive() => OnUi(() =>
        {
            _refused = false;
            var w = Active;
            Active = null;
            _refused = false;   // a new run may use a newly available platform surface
            if (w == null) return;
            w.Screens.Changed -= w.OnScreensChanged;
            w._listeningForScreens = false;
            w.Fx.Clear();
            w.StopRendering();
            w._hideTimer.Stop();
            foreach (var host in w._hosts)
                try { host.Close(); } catch (Exception ex) { Log.Debug("ChaosSkiaFx close: {E}", ex.Message); }
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
                    foreach (var host in Active._hosts)
                        if (host.IsVisible) X11Overlay.Raise(host);
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
            if (!w.ReconcileScreens())
            {
                foreach (var host in w._hosts) try { host.Close(); } catch { }
                if (w._hosts.Count == 0) try { w.Close(); } catch { }
                return null;
            }
            w.Screens.Changed += w.OnScreensChanged;
            w._listeningForScreens = true;
            return w;
        }

        // WPF CompositorEngine.OnDisplaySettingsChanged defers until fresh screen data is available.
        internal void OnScreensChanged(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_listeningForScreens || !ReferenceEquals(Active, this)) return;
                try
                {
                    if (!ReconcileScreens()) CloseActive();
                }
                catch (Exception ex)
                {
                    Log.Warning("ChaosSkiaFx: display change failed: {E}", ex.Message);
                    CloseActive();
                }
            }, DispatcherPriority.Background);
        }

        // Inject the available screens, before the host selection that must cover all of them.
        private IReadOnlyList<PixelRect> AvailableMonitorBounds() =>
            AvailableMonitorBoundsForTests ?? Screens.All.Select(s => s.Bounds).ToArray();

        private bool ReconcileScreens()
        {
            var primary = Screens.Primary ?? Screens.All.FirstOrDefault();
            if (primary == null) return _hosts.Count > 0; // WPF keeps hosts while no screens are reported.
            var screens = AvailableMonitorBounds();
            if (screens.Count == 0) return _hosts.Count > 0;
            var scale = MonitorScaleForTests ?? primary.Scaling;
            _scale = scale > 0 ? scale : 1;
            Fx.OnActivated();
            bool visible = IsVisible;
            for (int i = 0; i < screens.Count; i++)
            {
                var host = i < _hosts.Count ? _hosts[i] :
                    _hosts.Count == 0 ? this : new ChaosSkiaFxOverlay(live: true, shared: Fx);
                if (i == _hosts.Count) _hosts.Add(host);
                host._stagePx = screens[i];
                if ((!X11Overlay.SetClickThrough(host, true) | !X11Overlay.SetOverrideRedirect(host, host._stagePx)) && !SkipPlatformChecksForTests)
                {
                    Log.Warning("ChaosSkiaFx: the platform refused a click-through topmost overlay, so no chaos sparkle field");
                    _refused = true;
                    return false;
                }
                if (visible && !host.IsVisible) host.Show();
                (host.Content as Control)?.InvalidateVisual();
            }
            while (_hosts.Count > screens.Count)
            {
                var host = _hosts[_hosts.Count - 1];
                _hosts.RemoveAt(_hosts.Count - 1);
                try { host.Close(); } catch { }
            }
            return true;
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
        private readonly List<ChaosSkiaFxOverlay> _hosts = new();
        private bool _listeningForScreens;

        /// <summary>True while the frame loop is subscribed (tests).</summary>
        internal bool IsRendering => _frames != null;
        internal bool HidePending => _hideTimer.IsEnabled;
        internal IReadOnlyList<ChaosSkiaFxOverlay> Hosts => _hosts;
        internal bool ListeningForScreens => _listeningForScreens;

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

        private ChaosSkiaFxOverlay(bool live, ChaosFxLayer? shared = null)
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

            Fx = shared ?? new ChaosFxLayer(OnWake, stageScale: live ? () => (float)_scale : null, rng: live ? null : new Random(7));
            Content = new FxSurface(this);
            _onFrame = OnFrame;
            _hideTimer = new DispatcherTimer { Interval = HideGrace };
            _hideTimer.Tick += (_, _) => HideTick();
            if (!live) _hosts.Add(this);
        }

        // Effects arrive in physical virtual-desktop pixels, as on the WPF compositor path.
        private SKPoint Local(Point px) => new((float)px.X, (float)px.Y);

        /// <summary>The layer flips active on its first emit after idling: WPF StartActivity.</summary>
        private void OnWake()
        {
            if (Fx?.IsActive == true && ReferenceEquals(Active, this)) StartActivity();
        }

        private void StartActivity()
        {
            _hideTimer.Stop();
            foreach (var host in _hosts)
                if (!host.IsVisible)
                    try { host.Show(); } catch (Exception ex) { Log.Debug("ChaosSkiaFx show: {E}", ex.Message); }
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
            foreach (var host in _hosts) (host.Content as Control)?.InvalidateVisual();

            if (!Fx.IsActive)
            {
                if (++_idleFrames > 2) { StopRendering(); _hideTimer.Stop(); _hideTimer.Start(); }
            }
            else _idleFrames = 0;
        }

        internal void HideTick()
        {
            _hideTimer.Stop();
            if (!IsRendering) foreach (var host in _hosts) { try { host.Hide(); } catch { } }
        }

        /// <summary>Records the layer into an immutable picture on the UI thread; the render thread replays it.</summary>
        private sealed class FxSurface : Control
        {
            private readonly ChaosSkiaFxOverlay _host;

            public FxSurface(ChaosSkiaFxOverlay host) { _host = host; IsHitTestVisible = false; }

            public override void Render(DrawingContext context)
            {
                var b = new Rect(Bounds.Size);
                if (b.Width <= 0 || b.Height <= 0) return;
                using var rec = new SKPictureRecorder();
                var canvas = rec.BeginRecording(new SKRect(0, 0, (float)b.Width, (float)b.Height));
                RenderMonitor(canvas, _host.Fx, _host._stagePx, b.Size);
                context.Custom(new PictureOp(rec.EndRecording(), b));
            }
        }

        // WPF CompositorEngine translates world pixels to each monitor surface. Scaling by the
        // actual surface size also handles a DPI transition while the native surface catches up.
        internal static void RenderMonitor(SKCanvas canvas, ChaosFxLayer fx, PixelRect screen, Size surface)
        {
            if (screen.Width <= 0 || screen.Height <= 0)
                screen = new PixelRect(0, 0, (int)surface.Width, (int)surface.Height);
            if (screen.Width <= 0 || screen.Height <= 0) return;
            canvas.Save();
            canvas.Scale((float)(surface.Width / screen.Width), (float)(surface.Height / screen.Height));
            canvas.Translate(-screen.X, -screen.Y);
            fx.Render(canvas, new SKRectI(screen.X, screen.Y, screen.Right, screen.Bottom), 1, TimeSpan.Zero);
            canvas.Restore();
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
