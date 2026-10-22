// PORTED from ConditioningControlPanel/Controls/NavRail/SectionTabStrip.Depth.cs (polish wave 10,
// f6ec645a5) and SectionTabStrip.Fx.cs (wave 7, e28ef20b5): the strip's depth and motion.
// Depth: the tray is a sunken well (floor, top/left inner bands, lit foot), every raised pill throws
// a hue-tinted drop band, the lit pill is pressed IN (the fill sinks and wears the pressed bevel).
// Motion: hover lifts and grows the pill and wiggles its glyph, choosing throws a spark burst, the
// lit pill catches a sheen every 9 s. Numbers: Core DepthLaw / NavStripMotion; brushes: Theme/Depth.xaml.
// Full = all, Reduced = lift only (depth halves its timings), Off = nothing moves (depth still drawn).

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    public sealed partial class SectionTabStrip
    {
        private readonly Border _trayFloor = DepthLayer(), _trayWellTop = DepthLayer(), _trayWellLeft = DepthLayer();
        private readonly Border _trayWellFoot = DepthLayer(new Thickness(0, 0, 0, 1));
        private readonly Border _activePress = new() { CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(1), IsHitTestVisible = false };
        private readonly TranslateTransform _activeSink = new();
        private readonly TranslateTransform _sheenShift = new();
        private Border _sheenHost = null!;
        private AmbientFxCanvas _sparks = null!;
        private DispatcherTimer? _sheenClock;
        private IBrush _pillDrop = Brushes.Transparent;

        internal int BurstsFired { get; private set; }
        internal int SheensRun { get; private set; }
        internal bool? AmbientTierOverride { get; set; }
        internal bool SheenClockRunning => _sheenClock?.IsEnabled == true;
        internal AmbientFxCanvas Sparks => _sparks;
        internal IBrush? TrayFloorBrush => _trayFloor.Background;
        internal IBrush? TrayWellTopBrush => _trayWellTop.Background;
        internal Border ActivePressLayer => _activePress;
        internal double ActiveFillSink => _activeSink.Y;

        private static Border DepthLayer(Thickness? border = null) => new()
        {
            CornerRadius = new CornerRadius(21), IsHitTestVisible = false, BorderThickness = border ?? default,
        };

        /// <summary>WPF TrayHost (SectionTabStrip.xaml:58): the well layers under the track, the spark
        /// and sheen layer inside it (bleeding past it so sparks are not cut at the edge).</summary>
        private Panel BuildDepthTray(Border track)
        {
            double bleed = NavStripMotion.SparkBleed;
            _sheenHost = new Border
            {
                IsVisible = false, IsHitTestVisible = false, ClipToBounds = true,
                Child = new Border
                {
                    Width = NavStripMotion.SheenWidth, HorizontalAlignment = HorizontalAlignment.Left,
                    Opacity = NavStripMotion.SheenOpacity, RenderTransform = _sheenShift,
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                        GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Colors.White, 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
                    },
                },
            };
            _sparks = new AmbientFxCanvas { IsHitTestVisible = false };
            var fx = new Panel
            {
                IsHitTestVisible = false, Margin = new Thickness(-bleed),
                Children = { new Canvas { Margin = new Thickness(bleed), Children = { _sheenHost } }, _sparks },
            };
            ((Panel)track.Child!).Children.Add(fx);
            _activeFill.Child = _activePress;
            _activeFill.RenderTransform = _activeSink;
            return new Panel
            {
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Children = { _trayFloor, _trayWellTop, _trayWellLeft, _trayWellFoot, track },
            };
        }

        // ------------------------------------------------------------------ depth (wave 10)

        private static IBrush? Theme(string key)
        {
            try { return Application.Current?.TryGetResource(key, ThemeVariant.Dark, out var v) == true ? v as IBrush : null; }
            catch { return null; }
        }

        /// <summary>WPF PageDepthPaint.Band: a theme band mapped in pixels, so 9 px stays 9 px on any height.</summary>
        private static IBrush? Band(string key, double px, bool across = false)
        {
            if (Theme(key) is not LinearGradientBrush src) return null;
            var b = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
                EndPoint = across ? new RelativePoint(px, 0, RelativeUnit.Absolute) : new RelativePoint(0, px, RelativeUnit.Absolute),
            };
            foreach (var s in src.GradientStops) b.GradientStops.Add(new GradientStop(s.Color, s.Offset));
            return b;
        }

        /// <summary>WPF PaintDepthPages: the sunken tray, the pill drop band in the hue, the pressed-in fill.</summary>
        private void PaintDepth(Color hue)
        {
            _trayFloor.Background = Theme("DepthWellFloorBrush");
            _trayWellTop.Background = Band("DepthWellTop", DepthLaw.WellPx);
            _trayWellLeft.Background = Band("DepthWellLeft", 6, across: true);
            _trayWellFoot.BorderBrush = Theme("DepthWellFoot");
            _pillDrop = NavStripRules.DropBrush(hue);
            _activePress.Background = Theme("DepthPressedShade");
            _activePress.BorderBrush = Theme("DepthPressedBevel");
            _activeSink.Y = DepthLaw.ActiveSinkPx;
        }

        /// <summary>WPF DepthSettle: the face sits TravelFor down (lit sunk, pressed down; hover is the
        /// ring's lift), the drop band is ShadowFor long. Press goes down in PressMs; release springs
        /// back in ReleaseMs through a 1 px overshoot. Motion Off jumps.</summary>
        private void DepthSettle(PillParts p, bool animate)
        {
            bool active = IsActive(p.Tab.Key);
            double y = DepthLaw.TravelFor(true, p.Pressed, active, hovered: false);
            double drop = DepthLaw.ShadowFor(true, p.Pressed, active, p.Hovered);
            p.Drop.Background = _pillDrop;
            p.Drop.IsVisible = drop > 0;
            p.Drop.Height = drop + 2;
            p.Drop.Margin = new Thickness(NavStripRules.PillDropInset, 0, NavStripRules.PillDropInset, -drop);

            double from = p.FaceShift.Y;
            p.FaceShift.Y = y;
            int ms = DepthLaw.Ms(p.Pressed ? DepthLaw.PressMs : DepthLaw.ReleaseMs, Level);
            if (!animate || ms <= 0 || Math.Abs(from - y) < 0.01) return;
            var anim = new Animation { Duration = TimeSpan.FromMilliseconds(ms), FillMode = FillMode.None };
            anim.Children.Add(Frame(0, TranslateTransform.YProperty, from));
            if (!p.Pressed) anim.Children.Add(Frame(0.6, TranslateTransform.YProperty, y - DepthLaw.ReleaseOvershootPx));
            anim.Children.Add(Frame(1, TranslateTransform.YProperty, y));
            anim.Easing = new QuadraticEaseOut();
            // WPF BeginAnimation replaces the running one: cancel the previous travel first, or a
            // release spring still running fights the sink that a click starts right after it.
            p.FaceRun?.Cancel();
            p.FaceRun = new System.Threading.CancellationTokenSource();
            _ = anim.RunAsync(p.Face, p.FaceRun.Token);
        }

        private void DepthPress(PillParts p, bool down)
        {
            PressSquish(p, down);
            if (p.Pressed == down) return;
            p.Pressed = down;
            DepthSettle(p, animate: true);
        }

        // WPF MotionFx owns the first scale in the ring's transform group; hover keeps a separate
        // scale. Animate its properties directly: Animation.RunAsync cannot target a Transform.
        private void PressSquish(PillParts p, bool down)
        {
            if (Level == MotionLevel.Off) p.PressScale.Transitions = null;
            else
            {
                var duration = TimeSpan.FromMilliseconds(InteractionPressMotion.DurationMs);
                // Build once: one click calls this three times (release, Click, capture loss), and
                // replacing the collection would cancel the running 80 ms release ease.
                p.PressScale.Transitions ??= new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = duration, Easing = new QuadraticEaseOut() },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = duration, Easing = new QuadraticEaseOut() },
                };
            }
            p.PressScale.ScaleX = p.PressScale.ScaleY = InteractionPressMotion.ScaleFor(down);
        }

        private static KeyFrame Frame(double cue, AvaloniaProperty prop, double value) =>
            new() { Cue = new Cue(cue), Setters = { new Setter(prop, value) } };

        /// <summary>Test seam: where a pill's face settles (the base value, under any running travel).</summary>
        internal double PillFaceTravel(string key) =>
            Part(key)?.FaceShift.GetBaseValue(TranslateTransform.YProperty) is { HasValue: true } v ? v.Value : double.NaN;
        internal double PillDropLength(string key) => Part(key)?.Drop is { IsVisible: true } d ? d.Height - 2 : 0;
        internal IBrush? PillDropBrush(string key) => Part(key)?.Drop.Background;
        internal void DepthPressForTests(string key, bool down) { if (Part(key) is { } p) { p.Pressed = down; DepthSettle(p, animate: false); } }

        // ------------------------------------------------------------------ hover (wave 7)

        /// <summary>WPF FxHover: an inactive hovered pill lifts (and grows on Full); the lit pill rides
        /// the fill and keeps still. The glyph wiggles on Full. Off never moves.</summary>
        private void FxHover(PillParts p, bool on, bool animate)
        {
            p.Hovered = on;
            DepthSettle(p, animate);
            var level = Level;
            bool lift = on && level != MotionLevel.Off && !IsActive(p.Tab.Key);
            FxSettle(p, lift ? -NavStripMotion.LiftFor(level) : 0, lift ? NavStripMotion.ScaleFor(level) : 1.0,
                on ? NavStripMotion.HoverInMs : NavStripMotion.HoverOutMs, animate && level != MotionLevel.Off);
            if (on && animate && NavStripMotion.Wiggles(level) && p.Glyph != null)
            {
                double a = NavStripMotion.WiggleDegrees;
                var wiggle = new Animation { Duration = TimeSpan.FromMilliseconds(NavStripMotion.WiggleMs), Easing = new SineEaseInOut() };
                wiggle.Children.Add(Frame(0, RotateTransform.AngleProperty, 0));
                wiggle.Children.Add(Frame(0.25, RotateTransform.AngleProperty, -a));
                wiggle.Children.Add(Frame(0.65, RotateTransform.AngleProperty, a));
                wiggle.Children.Add(Frame(1, RotateTransform.AngleProperty, 0));
                _ = wiggle.RunAsync(p.Glyph);
            }
        }

        private static void FxSettle(PillParts p, double y, double scale, int ms, bool animate)
        {
            if (animate && ms > 0)
            {
                var d = TimeSpan.FromMilliseconds(ms);
                var ease = new CubicEaseOut();
                p.HoverLift.Transitions ??= new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = d, Easing = ease } };
                p.HoverScale.Transitions ??= new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = d, Easing = ease },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = d, Easing = ease },
                };
                foreach (var t in p.HoverLift.Transitions) if (t is DoubleTransition dt) dt.Duration = d;
                foreach (var t in p.HoverScale.Transitions) if (t is DoubleTransition dt) dt.Duration = d;
            }
            else { p.HoverLift.Transitions = null; p.HoverScale.Transitions = null; }
            p.HoverLift.Y = y;
            p.HoverScale.ScaleX = p.HoverScale.ScaleY = scale;
        }

        internal void FxHoverForTests(string key, bool on) { if (Part(key) is { } p) FxHover(p, on, animate: false); }
        /// <summary>Test seam: where a pill's hover settles (base values, under any running transition).</summary>
        internal (double Lift, double Scale) PillHover(string key) => Part(key) is { } p
            ? (p.HoverLift.GetBaseValue(TranslateTransform.YProperty).GetValueOrDefault(), p.HoverScale.GetBaseValue(ScaleTransform.ScaleXProperty).GetValueOrDefault())
            : (double.NaN, double.NaN);

        // ------------------------------------------------------------------ choose (wave 7)

        /// <summary>WPF FxOnChosen: settle the chosen pill's lift, ring it once and throw sparks in the hue.</summary>
        private void FxOnChosen(NavTab tab)
        {
            try
            {
                var level = Level;
                if (level == MotionLevel.Off || Part(tab.Key) is not { } p) return;
                if (IsActive(tab.Key)) FxSettle(p, 0, 1, NavStripMotion.HoverOutMs, level == MotionLevel.Full);
                NavGlow.Once(p.Pill, _hue, level, "pill-choose");
                var b = p.Pill.Bounds;
                if (b.Width <= 0) return;
                var centre = p.Pill.TranslatePoint(new Point(b.Width / 2, b.Height / 2), _sparks);
                if (centre == null) return;
                _sparks.Burst(centre.Value.X, centre.Value.Y, _hue, NavStripMotion.BurstSparks);
                BurstsFired++;
            }
            catch (Exception ex) { Serilog.Log.Debug("SectionTabStrip.FxOnChosen: {E}", ex.Message); }
        }

        // ------------------------------------------------------------------ idle sheen (wave 7)

        private bool TierAllowsAmbient() =>
            AmbientTierOverride ?? AmbientFxCanvas.Env.AllowAmbientMotion(AmbientFxCanvas.Env.CurrentTier);

        /// <summary>The window gate WPF FxChromeAllows reads per tick: active and not minimised.</summary>
        private bool ChromeAllows() =>
            TopLevel.GetTopLevel(this) is not Window w || (w.IsActive && w.WindowState != WindowState.Minimized);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty) UpdateSheenClock();
        }

        /// <summary>Start or park the 9 s sheen clock: Full, ambient tier, on screen and attached (P01/P65).</summary>
        internal void UpdateSheenClock()
        {
            bool runs = NavStripMotion.SheenRuns(Level, TierAllowsAmbient(), IsVisible && this.IsAttachedToVisualTree(), chromeAllows: true);
            if (!runs) { _sheenClock?.Stop(); return; }
            if (_sheenClock == null)
            {
                _sheenClock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(NavStripMotion.SheenEveryMs) };
                _sheenClock.Tick += (_, _) =>
                {
                    if (!NavStripMotion.SheenRuns(Level, TierAllowsAmbient(), IsVisible && this.IsAttachedToVisualTree(), ChromeAllows()))
                    {
                        if (!IsVisible || !this.IsAttachedToVisualTree() || Level != MotionLevel.Full) _sheenClock?.Stop();
                        return;
                    }
                    SweepSheen();
                };
            }
            if (!_sheenClock.IsEnabled) _sheenClock.Start();
        }

        /// <summary>One sheen across the lit pill. False when there is no laid-out lit pill.</summary>
        internal bool SweepSheen()
        {
            var p = _activePill == null ? null : Part(_activePill);
            if (p == null || p.Pill.Bounds.Width <= 0 || _sheenHost.Parent is not Visual canvas) return false;
            var at = p.Pill.TranslatePoint(default, canvas);
            if (at == null) return false;
            double w = p.Pill.Bounds.Width, h = p.Pill.Bounds.Height;
            Canvas.SetLeft(_sheenHost, at.Value.X);
            Canvas.SetTop(_sheenHost, at.Value.Y);
            _sheenHost.Width = w;
            _sheenHost.Height = h;
            _sheenHost.CornerRadius = new CornerRadius(h / 2);
            ((Border)_sheenHost.Child!).Height = h;
            _sheenHost.IsVisible = true;
            SheensRun++;
            var sweep = new Animation { Duration = TimeSpan.FromMilliseconds(NavStripMotion.SheenMs), Easing = new SineEaseInOut() };
            sweep.Children.Add(Frame(0, TranslateTransform.XProperty, -NavStripMotion.SheenWidth));
            sweep.Children.Add(Frame(1, TranslateTransform.XProperty, w));
            _ = HideAfter(sweep.RunAsync(_sheenHost.Child!));
            return true;
        }

        private async Task HideAfter(Task sweep)
        {
            try { await sweep; } catch { }
            _sheenHost.IsVisible = false;
        }
    }
}
