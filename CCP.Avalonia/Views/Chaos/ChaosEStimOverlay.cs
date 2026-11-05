// PORTED from ConditioningControlPanel/Chaos/ChaosEStimOverlay.cs (ChaosEStimOverlay + ChaosEStimGlow).
// The maths (bolt path, strike timeline, halo follow and pulse) is Core's ChaosEStimMath; this file only draws.
//
// ponytail: Strike/Arm callers wait for charged-pop conduction and e_stim toy effects.
// The bubble field and toy buttons exist, but their e-stim effects do not. Device output is untouched.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Chaos;
using Serilog;
using M = ConditioningControlPanel.Services.Chaos.ChaosEStimMath;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// E-Stim's lightning: jagged tether bolts arcing bubble-to-bubble for a quarter second, on one
    /// click-through stage window (WPF ChaosEStimOverlay). Pre-created at run start, hidden
    /// between strikes, closed only by <see cref="CloseActive"/> (run end, panic). Refuses where there is
    /// no compositor: without per-pixel alpha the "window" would be an opaque screen-sized block.
    /// </summary>
    internal sealed class ChaosEStimOverlay : Window
    {
        private static readonly Color GlowColor = Color.FromRgb(0x9C, 0x5C, 0xFF);   // violet haze
        private static readonly Color CoreColor = Color.FromRgb(0xBF, 0xEC, 0xFF);   // electric white-blue

        internal static bool SkipPlatformChecksForTests;
        internal static TimeProvider Clock = TimeProvider.System;
        private static ChaosEStimOverlay? _active;
        internal static ChaosEStimOverlay? Active => _active;

        /// <summary>The charge is live: light the cursor halo.</summary>
        public static void Arm() => ChaosEStimGlow.Arm();

        /// <summary>Charges spent (or cancelled): drop the cursor halo.</summary>
        public static void Disarm() => ChaosEStimGlow.Disarm();

        /// <summary>The platform refused a click-through window this run: stop rebuilding one per strike.</summary>
        internal static bool Refused;
        internal static Func<bool> CanShow = () => X11Overlay.IsAvailable && X11Overlay.IsCompositing;

        internal static void OnUi(Action a)
        {
            if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a);
        }

        internal static Action<Window> RaiseWindow = w => X11Overlay.Raise(w);

        /// <summary>WPF ChaosEStimOverlay.cs:46,329: realize both windows once, then idle hidden.</summary>
        public static void EnsureCreated() => OnUi(() =>
        {
            ChaosEStimGlow.EnsureCreated();
            try { Create(); }
            catch (Exception ex) { Log.Debug("ChaosEStim.EnsureCreated: {E}", ex.Message); }
        });

        private static ChaosEStimOverlay? Create()
        {
            if (_active != null) return _active;
            if (Refused || !CanShow()) return null;
            var w = new ChaosEStimOverlay(sample: false);
            if (!w.Prepare()) { Refused = true; w.Close(); return null; }
            w.Show();
            w.Hide();
            return _active = w;
        }

        // ponytail: the gameplay-layer re-stack caller waits for chaos mandatory-video integration.
        /// <summary>WPF ChaosEStimOverlay.cs:100,397: re-stack live windows without unhiding them.</summary>
        public static void RaiseActive()
        {
            if (_active is { IsVisible: true } w) RaiseWindow(w);
            ChaosEStimGlow.RaiseActive();
        }

        /// <summary>Fire one strike along <paramref name="boltsPx"/> (physical-pixel segment endpoints,
        /// in strike order): flicker for the hot window, fade, hide. Safe from any thread.</summary>
        public static void Strike(IReadOnlyList<(PixelPoint From, PixelPoint To)> boltsPx)
        {
            if (boltsPx == null || boltsPx.Count == 0) return;
            OnUi(() =>
            {
                try
                {
                    var w = Create();
                    if (w == null) return;
                    if (!w.IsVisible) w.Show();
                    RaiseWindow(w);   // un-hiding doesn't re-stack (WPF RaiseAboveVideo)
                    w.BeginStrike(boltsPx);
                }
                catch (Exception ex) { Log.Debug("ChaosEStim.Strike: {E}", ex.Message); }
            });
        }

        /// <summary>Teardown (run end, panic): the only place these windows die. Closes the halo too.</summary>
        public static void CloseActive()
        {
            Refused = false;   // a refusal is remembered for one run, not forever
            ChaosEStimGlow.CloseActive();
            OnUi(() =>
            {
                var w = _active;
                _active = null;
                if (w == null) return;
                try { w._tick.Stop(); w.Close(); } catch (Exception ex) { Log.Debug("ChaosEStim close: {E}", ex.Message); }
            });
        }

        private readonly Canvas _canvas = new() { IsHitTestVisible = false };
        private readonly FrameClock _tick;
        private readonly Random _rng = new();
        private readonly List<(Point A, Point B)> _segments = new();   // window-local DIPs
        private readonly List<(Polyline Glow, Polyline Core)> _lines = new();
        private long _start;
        private int _step;
        private double _scale = 1;
        private double _alpha;

        /// <summary>--render-all: one sample strike so the PNG shows the bolt, flash and colours.</summary>
        public ChaosEStimOverlay() : this(sample: true) { }

        private ChaosEStimOverlay(bool sample)
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
            Width = 640;
            Height = 400;
            Content = _canvas;
            _tick = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(16) };
            _tick.Tick += (_, _) => Step();
            if (sample) _canvas.Opacity = M.StrikeOpacity;   // the render proof has no compositor alpha
            if (sample) Draw(new[] { (new Point(80, 300), new Point(300, 120)), (new Point(300, 120), new Point(560, 220)) });
        }

        /// <summary>WPF StageBounds (every monitor when DualMonitorEnabled, else the primary) +
        /// WS_EX_TRANSPARENT/NOACTIVATE, before Show. Mixed-DPI desktops use the primary's scaling.</summary>
        private bool Prepare()
        {
            var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
            if (screen == null) return SkipPlatformChecksForTests;
            _scale = screen.Scaling > 0 ? screen.Scaling : 1;
            var bounds = screen.Bounds;
            if (CoreSettings.Current.DualMonitorEnabled)
                foreach (var s in Screens!.All) bounds = bounds.Union(s.Bounds);
            if (SkipPlatformChecksForTests) return true;
            if (!X11Overlay.SetClickThrough(this, true) | !X11Overlay.SetOverrideRedirect(this, bounds))
            {
                Log.Warning("ChaosEStim: the platform refused a click-through topmost overlay, no lightning");
                return false;
            }
            return true;
        }

        private void BeginStrike(IReadOnlyList<(PixelPoint From, PixelPoint To)> boltsPx)
        {
            Point Local(PixelPoint px)
            {
                var (x, y) = M.ToLocal(px.X, px.Y, Position.X, Position.Y, _scale);
                return new Point(x, y);
            }
            _tick.Stop();
            Draw(boltsPx.Select(b => (Local(b.From), Local(b.To))).ToList());
            _start = Clock.GetTimestamp();
            _tick.Start();
        }

        /// <summary>Whole-window alpha through the compositor (X11Overlay.SetOpacity): fading a
        /// screen-sized visual Opacity re-renders it every frame, which was measured stalling the UI.</summary>
        private void SetAlpha(double a)
        {
            _alpha = a;
            X11Overlay.SetOpacity(this, a);
        }

        private void Draw(IReadOnlyList<(Point A, Point B)> bolts)
        {
            SetAlpha(M.StrikeOpacity);
            _canvas.Children.Clear();
            _segments.Clear();
            _lines.Clear();
            _step = 0;
            var flash = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(130, 0xFF, 0xFF, 0xFF), 0.0),
                    new GradientStop(Color.FromArgb(70, CoreColor.R, CoreColor.G, CoreColor.B), 0.45),
                    new GradientStop(Color.FromArgb(0, GlowColor.R, GlowColor.G, GlowColor.B), 1.0),
                },
            };
            foreach (var (a, b) in bolts)
            {
                _segments.Add((a, b));
                var glow = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(80, GlowColor.R, GlowColor.G, GlowColor.B)), StrokeThickness = 5.5, StrokeJoin = PenLineJoin.Round };
                var core = new Polyline { Stroke = new SolidColorBrush(Color.FromArgb(190, CoreColor.R, CoreColor.G, CoreColor.B)), StrokeThickness = 1.7, StrokeJoin = PenLineJoin.Round };
                _lines.Add((glow, core));
                _canvas.Children.Add(glow);
                _canvas.Children.Add(core);
                // Impact flash at the receiving end (the anchor's own flash is its pop burst).
                var dot = new Ellipse { Width = M.FlashSize, Height = M.FlashSize, Fill = flash };
                Canvas.SetLeft(dot, b.X - M.FlashSize / 2);
                Canvas.SetTop(dot, b.Y - M.FlashSize / 2);
                _canvas.Children.Add(dot);
            }
            Jitter();
        }

        private void Jitter()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                var (a, b) = _segments[i];
                var pts = M.BoltPath(a.X, a.Y, b.X, b.Y, _rng.NextDouble).Select(p => new Point(p.X, p.Y)).ToList();
                _lines[i].Glow.Points = pts;
                _lines[i].Core.Points = pts;
            }
        }

        /// <summary>One frame: re-jitter on each new flicker step, follow the opacity timeline, hide when
        /// it ends. Elapsed is real time (P40), so a stalled frame snaps to where the strike should be.</summary>
        internal void Step()
        {
            double ms = Clock.GetElapsedTime(_start).TotalMilliseconds;
            int step = M.FlickerStep(ms);
            if (step > _step) { _step = step; Jitter(); }
            var op = M.StrikeOpacityAt(ms);
            if (op is double o) { SetAlpha(o); return; }
            _tick.Stop();
            _canvas.Children.Clear();
            try { Hide(); } catch (Exception ex) { Log.Debug("ChaosEStim hide: {E}", ex.Message); }
        }

        internal bool IsTicking => _tick.IsEnabled;
        internal int BoltCount => _segments.Count;
        internal double StrikeAlpha => _alpha;
    }

    /// <summary>
    /// E-Stim's "charged" telegraph: a small violet halo riding the cursor while charges wait (WPF
    /// ChaosEStimGlow). Its own tiny window, so nothing screen-sized stays mapped for a long armed spell.
    /// Follows the desktop pointer through <see cref="X11Pointer"/> on a frame clock that runs only while armed.
    /// </summary>
    internal sealed class ChaosEStimGlow : Window
    {
        private static ChaosEStimGlow? _active;
        internal static ChaosEStimGlow? Active => _active;
        internal static Func<(PixelPoint At, bool Pressed, bool Escape)?> ReadPointer = X11Pointer.Read;

        internal static void EnsureCreated()
        {
            try { Create(); }
            catch (Exception ex) { Log.Debug("ChaosEStimGlow.EnsureCreated: {E}", ex.Message); }
        }

        private static ChaosEStimGlow? Create()
        {
            if (_active != null) return _active;
            if (ChaosEStimOverlay.Refused || !ChaosEStimOverlay.CanShow()) return null;
            var w = new ChaosEStimGlow();
            if (!w.Prepare()) { ChaosEStimOverlay.Refused = true; w.Close(); return null; }
            // Realize without flashing the halo at the desktop origin.
            w.Opacity = 0;
            w.Show();
            w.Hide();
            w.Opacity = 1;
            return _active = w;
        }

        internal static void RaiseActive()
        {
            if (_active is { IsVisible: true } w) ChaosEStimOverlay.RaiseWindow(w);
        }

        /// <summary>Charges armed: show the halo and follow the cursor. Safe from any thread.</summary>
        public static void Arm() => ChaosEStimOverlay.OnUi(() =>
        {
            try
            {
                var w = Create();
                if (w == null) return;
                w._start = ChaosEStimOverlay.Clock.GetTimestamp();
                w.Step();   // snap to the cursor before the first frame
                if (!w.IsVisible) w.Show();
                ChaosEStimOverlay.RaiseWindow(w);
                w._follow.Start();
            }
            catch (Exception ex) { Log.Debug("ChaosEStimGlow.Arm: {E}", ex.Message); }
        });

        /// <summary>Charges spent or cancelled: stop following and hide (window stays alive).</summary>
        public static void Disarm() => ChaosEStimOverlay.OnUi(() =>
        {
            if (_active == null) return;
            _active._follow.Stop();
            try { _active.Hide(); } catch (Exception ex) { Log.Debug("ChaosEStimGlow hide: {E}", ex.Message); }
        });

        internal static void CloseActive() => ChaosEStimOverlay.OnUi(() =>
        {
            var w = _active;
            _active = null;
            if (w == null) return;
            try { w._follow.Stop(); w.Close(); } catch (Exception ex) { Log.Debug("ChaosEStimGlow close: {E}", ex.Message); }
        });

        internal bool IsTracking => _follow.IsEnabled;
        private readonly FrameClock _follow;
        private readonly ScaleTransform _pulse = new(1, 1);
        private long _start;

        public ChaosEStimGlow()
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
            Width = M.HaloWindow;
            Height = M.HaloWindow;
            // Soft violet halo with an electric core hint - quieter than the Rabbit Caller whistle.
            Content = new Ellipse
            {
                Width = M.HaloSize,
                Height = M.HaloSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = _pulse,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(0, 0xBF, 0xEC, 0xFF), 0.10),
                        new GradientStop(Color.FromArgb(120, 0x9C, 0x5C, 0xFF), 0.45),
                        new GradientStop(Color.FromArgb(0, 0x9C, 0x5C, 0xFF), 1.0),
                    },
                },
            };
            _follow = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(16) };   // WPF FOLLOW_MS
            _follow.Tick += (_, _) => Step();
        }

        private bool Prepare()
        {
            if (ChaosEStimOverlay.SkipPlatformChecksForTests) return true;
            var at = ReadPointer()?.At ?? default;
            int size = (int)Math.Ceiling(M.HaloWindow * DesktopScaling);
            if (!X11Overlay.SetClickThrough(this, true) | !X11Overlay.SetOverrideRedirect(this, new PixelRect(at.X, at.Y, size, size)))
            {
                Log.Warning("ChaosEStimGlow: the platform refused a click-through topmost overlay, no halo");
                return false;
            }
            return true;
        }

        /// <summary>One frame: centre on the pointer and tremble (WPF FollowTick + the 160 ms pulse).</summary>
        internal void Step()
        {
            double s = M.HaloScaleAt(ChaosEStimOverlay.Clock.GetElapsedTime(_start).TotalMilliseconds);
            _pulse.ScaleX = s;
            _pulse.ScaleY = s;
            if (ReadPointer() is not { } p) return;
            var (x, y) = M.HaloOrigin(p.At.X, p.At.Y, DesktopScaling);
            Position = new PixelPoint(x, y);
        }
    }
}
