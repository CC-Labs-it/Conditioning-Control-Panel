using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Chaos/ChaosFieldFxOverlay.cs: the one click-through
    /// stage-sized window the run boons draw on (Size Queen ring, the Ripple cast, Aftermath residue,
    /// Tail-Plug trail sparks, The Bound tethers). Motion lives in Core <see cref="ChaosFieldFxModel"/>;
    /// this draws what it samples on the window's shared <see cref="FrameClock"/> and stops the clock
    /// when hidden. Same keep-alive contract as WPF: realized at run start, hidden when idle, closed
    /// only at run teardown/panic (<see cref="CloseActive"/>).
    ///
    /// <para>Refuses (draws nothing) unless X11 click-through and a compositor are there: an opaque or
    /// click-eating stage-sized topmost window would lock the desktop.</para>
    ///
    /// <para>ponytail: nothing calls Ripple/SnapRipple/Residue/TrailDot/SetTether yet - the bubble field
    /// (WPF BubbleService.TickFieldHazards) is not on this head (ChaosRunHost note).</para>
    /// </summary>
    internal sealed class ChaosFieldFxOverlay : Window
    {
        private static ChaosFieldFxOverlay? _active;
        private static bool _refused;

        /// <summary>Tests: skip the X11/compositor checks (headless has neither).</summary>
        internal static bool SkipPlatformChecksForTests;
        /// <summary>X11 with a compositor and no earlier refusal (tests swap it).</summary>
        internal static Func<bool> PlatformReady = () =>
            SkipPlatformChecksForTests || (!_refused && X11Overlay.IsAvailable && X11Overlay.IsCompositing);
        /// <summary>Tests: the clock the model is sampled on, in ms.</summary>
        internal static Func<double> NowMs = () => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

        internal static ChaosFieldFxOverlay? Active => _active;

        /// <summary>Realize once at run start; idle hidden with the clock parked.</summary>
        public static void EnsureCreated() => OnUi(_ => { }, draw: false);

        public static void RaiseActive()
        {
            if (_active is { IsVisible: true, Topmost: true } && !SkipPlatformChecksForTests)
                X11Overlay.Raise(_active);
        }

        public static void Ripple(Point centerPx, double radiusPx, double lifeMs) =>
            OnUi(w => w.Model.Ripple(w.Lx(centerPx.X), w.Ly(centerPx.Y), radiusPx / w._scale, lifeMs, NowMs()));

        public static void SnapRipple(Point centerPx, double radiusPx, double lifeMs) =>
            OnUi(w => w.Model.SnapRipple(w.Lx(centerPx.X), w.Ly(centerPx.Y), radiusPx / w._scale, lifeMs, NowMs()));

        public static void Residue(Point centerPx, double radiusPx, double lifeMs) =>
            OnUi(w => w.Model.Residue(w.Lx(centerPx.X), w.Ly(centerPx.Y), radiusPx / w._scale, lifeMs, NowMs(),
                gentle: Services.Safety.GentleVisuals.On,
                gentleFadeSeconds: Services.Safety.GentleVisuals.FadeSeconds));

        public static void TrailDot(Point centerPx, double lifeSec, bool warm = false) =>
            OnUi(w => w.Model.TrailDot(w.Lx(centerPx.X), w.Ly(centerPx.Y), lifeSec, warm, NowMs()));

        public static void SetTether(int key, Point aPx, Point bPx) =>
            OnUi(w => w.Model.SetTether(key, w.Lx(aPx.X), w.Ly(aPx.Y), w.Lx(bPx.X), w.Ly(bPx.Y)));

        /// <summary>WPF ClearTether: never opens the window just to drop a thread.</summary>
        public static void ClearTether(int key)
        {
            if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => ClearTether(key));
                return;
            }
            if (_active == null) return;
            _active.Model.ClearTether(key);
            _active.HideIfIdle();
        }

        /// <summary>Run end / panic: the only place the window dies. Safe when idle.</summary>
        public static void CloseActive()
        {
            if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(CloseActive);
                return;
            }
            _refused = false;
            var w = _active;
            _active = null;
            if (w == null) return;
            w.Clock.Stop();
            w.Model.Clear();
            try { w.Close(); } catch (Exception ex) { Log.Debug("ChaosFieldFx close: {E}", ex.Message); }
        }

        private static void OnUi(Action<ChaosFieldFxOverlay> act, bool draw = true)
        {
            // WPF BeginInvoke'd every call onto the UI thread; callers may be off it.
            if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() => OnUi(act, draw));
                return;
            }
            try
            {
                if (_active == null)
                {
                    if (!PlatformReady()) return;   // no composited click-through overlay here: draw nothing
                    var w = new ChaosFieldFxOverlay(sample: false);
                    if (!w.Place()) return;
                    _active = w;
                    w.Show();
                    w.Hide();
                }
                if (!draw) return;
                if (!_active.IsVisible)
                {
                    _active.Show();
                    RaiseActive();
                }
                act(_active);
                if (!_active.Clock.IsEnabled) _active.Clock.Start();
            }
            catch (Exception ex) { Log.Debug("ChaosFieldFx: {E}", ex.Message); }
        }

        internal readonly ChaosFieldFxModel Model = new();
        internal readonly FrameClock Clock;
        private readonly FieldCanvas _canvas;
        private PixelPoint _origin;
        private double _scale = 1;

        /// <summary>Render proof: a frozen mid-life sample of every element.</summary>
        public ChaosFieldFxOverlay() : this(sample: true) { }

        private ChaosFieldFxOverlay(bool sample)
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = CoreSettings.Current.ChaosPinOnTop;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;   // no geometry here: SetOverrideRedirect places it (X11Overlay notes)
            _canvas = new FieldCanvas();
            Content = _canvas;
            Clock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(1000.0 / 60) };
            Clock.Tick += (_, _) => Step(NowMs());
            if (sample)
            {
                Width = 900;
                Height = 600;
                Model.Ripple(200, 300, 160, 1000, 0);
                Model.SnapRipple(520, 300, 220, 1000, 0);
                Model.Residue(780, 150, 90, 1000, 0, new Random(7));
                Model.SetTether(1, 600, 520, 840, 450);
                for (int i = 0; i < 12; i++) Model.TrailDot(60 + i * 40, 520 - i * 12, 1.2, i % 3 == 0, i * 30);
                Step(450);
            }
        }

        /// <summary>WPF StageBounds: the primary screen, or every screen with DualMonitorEnabled.
        /// Click-through and override-redirect go on before Show, as SpiralOverlay does.</summary>
        private bool Place()
        {
            var primary = Screens?.Primary ?? (Screens?.All is { Count: > 0 } all ? all[0] : null);
            var bounds = primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
            if (CoreSettings.Current.DualMonitorEnabled && Screens?.All is { } screens)
                foreach (var s in screens) bounds = bounds.Union(s.Bounds);
            _scale = primary?.Scaling is > 0 and var sc ? sc : 1;
            _origin = bounds.Position;
            if (SkipPlatformChecksForTests)
            {
                Position = bounds.Position;
                Width = bounds.Width / _scale;
                Height = bounds.Height / _scale;
                return true;
            }
            // Override-redirect before any geometry, or KWin can replay a stale size (X11Overlay.SetOverrideRedirect).
            if (!X11Overlay.SetClickThrough(this, true) | !X11Overlay.SetOverrideRedirect(this, bounds))
            {
                Log.Warning("ChaosFieldFx: the platform refused a click-through overlay, so no field FX are drawn");
                _refused = true;
                try { Close(); } catch { }
                return false;
            }
            return true;
        }

        private double Lx(double px) => (px - _origin.X) / _scale;
        private double Ly(double px) => (px - _origin.Y) / _scale;

        /// <summary>One frame: sample, repaint, and hide (clock stops) once nothing is left to draw.</summary>
        internal void Step(double now)
        {
            Model.Frame(now, _canvas.Prims);
            _canvas.InvalidateVisual();
            if (_active == this) HideIfIdle(now);
        }

        /// <summary>Nothing drawn: the clock parks (WPF storyboards had simply ended). The window stays
        /// up while the Tail-Plug boon is on (WPF's hide rule) and the next TrailDot restarts the clock.</summary>
        private void HideIfIdle(double? now = null)
        {
            double t = now ?? NowMs();
            if (!Model.IsIdle(t, 0)) return;
            Clock.Stop();
            double trail = ChaosRunHost.IsDescending ? ChaosRunHost.State?.RabbitTrailSec ?? 0 : 0;
            if (trail > 0) return;
            try { Hide(); } catch (Exception ex) { Log.Debug("ChaosFieldFx hide: {E}", ex.Message); }
        }

        /// <summary>Draws the sampled primitives (WPF: Ellipse/Line elements on a Canvas).</summary>
        internal sealed class FieldCanvas : Control
        {
            internal readonly List<FieldFxPrim> Prims = new();
            private static IBrush? _trailCool, _trailWarm, _residue;

            private static IBrush Spark(FieldFxColor edge) =>
                Radial(ChaosFieldFxModel.TrailCore, edge with { A = 120 }, edge with { A = 0 });

            /// <summary>WPF's three-stop radial (0, 0.55, 1), centred.</summary>
            private static IBrush Radial(FieldFxColor a, FieldFxColor b, FieldFxColor c) => new RadialGradientBrush
            {
                GradientStops = { new GradientStop(C(a), 0.0), new GradientStop(C(b), 0.55), new GradientStop(C(c), 1.0) },
            }.ToImmutable();

            private static Color C(FieldFxColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

            private static readonly Dictionary<FieldFxColor, IImmutableSolidColorBrush> Solid = new();
            private static IImmutableSolidColorBrush S(FieldFxColor c) =>
                Solid.TryGetValue(c, out var b) ? b : Solid[c] = new ImmutableSolidColorBrush(C(c));

            public FieldCanvas() => IsHitTestVisible = false;

            public override void Render(DrawingContext ctx)
            {
                _trailCool ??= Spark(ChaosFieldFxModel.TrailColor);
                _trailWarm ??= Spark(ChaosFieldFxModel.WarmTrailColor);
                _residue ??= Radial(ChaosFieldFxModel.ResidueCore, ChaosFieldFxModel.ResidueColor with { A = 90 },
                    ChaosFieldFxModel.ResidueColor with { A = 0 });
                foreach (var p in Prims)
                {
                    if (p.Opacity <= 0) continue;
                    using (ctx.PushOpacity(p.Opacity))
                    {
                        var c = new Point(p.X, p.Y);
                        switch (p.Kind)
                        {
                            case FieldFxKind.Ring:
                                if (p.Radius > 0)
                                    ctx.DrawEllipse(null, new ImmutablePen(S(p.Color), p.Thickness), c, p.Radius, p.Radius);
                                break;
                            case FieldFxKind.Shard:
                                ctx.DrawLine(new ImmutablePen(S(p.Color), p.Thickness, lineCap: PenLineCap.Round), c, new Point(p.X2, p.Y2));
                                break;
                            case FieldFxKind.Tether:
                                ctx.DrawLine(new ImmutablePen(S(p.Color), p.Thickness,
                                    new ImmutableDashStyle(new double[] { 4, 3 }, 0), PenLineCap.Round), c, new Point(p.X2, p.Y2));
                                break;
                            case FieldFxKind.Residue:
                                ctx.DrawEllipse(_residue, null, c, p.Radius, p.Radius);
                                break;
                            case FieldFxKind.TrailDot:
                                ctx.DrawEllipse(p.Warm ? _trailWarm : _trailCool, null, c, p.Radius, p.Radius);
                                break;
                        }
                    }
                }
            }
        }
    }
}
