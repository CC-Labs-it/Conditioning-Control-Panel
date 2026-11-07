// PORTED from ConditioningControlPanel/Controls/Billboard/BillboardCardHost.Fx.cs + the motion half of
// .Chips.cs: the push between cards and its glint, the words' rise, the button's thud, the badge pop,
// the snooze fold, the toast, the dots' open / tick / pop, and the sparks and motes. Every move is a
// one-shot Avalonia Animation on the control's own clock (tests step it); WPF ran the same moves as
// BeginAnimation DoubleAnimations. Durations scale with the motion level exactly like WPF
// (DepthRules.Ms: Full as authored, Reduced halves, Off none); particles need AllowParticles.
//
// Why not RenderTransform keyframes: Avalonia has no animator for RenderTransform itself ("No
// animator registered for the property RenderTransform"), so the first port's push threw on every
// card change, the catch retired the old card at once and the board never slid (owner, 2026-11-06).
// Animating ScaleTransform / TranslateTransform properties through the control works.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.UI;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard;

internal sealed partial class BillboardCardHost
{
    private readonly Panel _stageRoot = new();
    private readonly Rectangle _glint = new() { IsHitTestVisible = false, Opacity = 0, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left };
    private readonly TranslateTransform _glintMove = new();
    private readonly TranslateTransform _metaMove = new();
    private readonly ScaleTransform _badgeScale = new(1, 1);
    private readonly TranslateTransform _badgeMove = new();
    private readonly ScaleTransform _ctaThudScale = new(1, 1);
    private readonly TranslateTransform _ctaThudMove = new();
    private readonly SparkLayer _sparks = new();
    private readonly Dictionary<Control, CancellationTokenSource> _slideRuns = new();
    private readonly Dictionary<Control, CancellationTokenSource> _fades = new();
    private CancellationTokenSource _wordsRun = new();
    private CancellationTokenSource? _glintRun, _chipRun;
    private int _toastToken;

    // ---- test seams --------------------------------------------------------------------------

    internal global::Avalonia.Controls.Controls Slides => _slides.Children;
    internal Border Stage => _stage;
    internal Control? CurrentSlide => _current;
    internal Rectangle Glint => _glint;
    internal TextBlock TitleText => _title;
    internal Control Meta => _meta;
    internal Border Badge => _badge;
    internal Border PausedPill => _paused;
    internal Border ToastPill => _toast;
    internal int SparkCount => _sparks.Count;

    /// <summary>A slide's push transforms (WPF Slide.Zoom, Slide.Move).</summary>
    internal static (ScaleTransform Zoom, TranslateTransform Move) SlideParts(Control slide)
    {
        var g = (TransformGroup)slide.RenderTransform!;
        return ((ScaleTransform)g.Children[0], (TranslateTransform)g.Children[1]);
    }

    // ---- the push (WPF Fx.cs:44-76) ----------------------------------------------------------

    /// <summary>The new card pushes in from 40% of the width over the old one (450 ms, ease-out
    /// cubic, a touch large), the old one slides 18% left under it, and a glint in the new card's
    /// hue rides the seam. The old card leaves when its slide ends.</summary>
    private async void Push(Control old, Control next)
    {
        double w = Math.Max(1, _stage.Bounds.Width);
        int ms = Math.Max(1, DepthLaw.Ms(DashboardBillboard.PushMs, Env.Level));
        var ease = new CubicEaseOut();
        var (_, oldMove) = SlideParts(old);
        var (nextZoom, nextMove) = SlideParts(next);
        var oldRun = SlideRun(old);
        var nextRun = SlideRun(next);

        double start = w * DashboardBillboard.PushNewStart;
        var outbound = Tween(old, oldMove, TranslateTransform.XProperty, 0, -w * DashboardBillboard.PushOldShift, 0, ms, ease, oldRun.Token);
        _ = Tween(next, nextMove, TranslateTransform.XProperty, start, 0, 0, ms, ease, nextRun.Token);
        _ = Tween(next, nextZoom, ScaleTransform.ScaleXProperty, DashboardBillboard.PushNewScale, 1, 0, ms, ease, nextRun.Token);
        _ = Tween(next, nextZoom, ScaleTransform.ScaleYProperty, DashboardBillboard.PushNewScale, 1, 0, ms, ease, nextRun.Token);

        // The glint: a narrow band in the hue, its bright edge on the seam, fading as it lands.
        var hue = _card != null ? HueOf(_card) : Color.FromRgb(0xff, 0x4f, 0xa8);
        _glint.Fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 0),
                new GradientStop(Color.FromArgb(0x73, hue.R, hue.G, hue.B), 0.75),
                new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 1),
            },
        };
        double band = _glint.Width is > 0 and var gw ? gw : w * 0.07;
        _glintRun?.Cancel();
        _glintRun = new CancellationTokenSource();
        _ = Tween(_glint, _glintMove, TranslateTransform.XProperty, start - band * 0.75, -band * 0.75, 0, ms, ease, _glintRun.Token);
        _ = Tween(_glint, _glint, OpacityProperty, 1, 0, 0, ms, new LinearEasing(), _glintRun.Token);

        try { await outbound; }
        catch (Exception ex) { Log.Debug("Billboard push failed: {E}", ex.Message); }
        // Posted: a close ends the animation inside the detach walk, which must not see the
        // slide list change under it.
        Dispatcher.UIThread.Post(() => { if (!oldRun.IsCancellationRequested && _slides.Children.Contains(old)) Retire(old); });
    }

    private CancellationTokenSource SlideRun(Control slide)
    {
        if (_slideRuns.Remove(slide, out var prior)) { prior.Cancel(); prior.Dispose(); }
        var run = new CancellationTokenSource();
        _slideRuns[slide] = run;
        return run;
    }

    // ---- the words (WPF Fx.cs:80-181) --------------------------------------------------------

    private CancellationToken NewWordsRun()
    {
        _wordsRun.Cancel();
        _wordsRun.Dispose();
        _wordsRun = new CancellationTokenSource();
        return _wordsRun.Token;
    }

    /// <summary>Eyebrow, title and line rise 60 ms apart; the button thuds in after them; the
    /// badge pops.</summary>
    private void LandWords()
    {
        var token = NewWordsRun();
        ResetFold();
        var thud = new ThudEase();
        int rise = DepthLaw.Ms(DashboardBillboard.TextRiseMs, Env.Level);
        int step = DepthLaw.Ms(DashboardBillboard.TextStaggerMs, Env.Level);
        int k = 1;
        foreach (var t in new[] { _eyebrow, _title, _line })
        {
            if (!t.IsVisible) continue;
            int delay = step * k++;
            _ = Tween(t, t, OpacityProperty, 0, 1, delay, rise, thud, token);
            _ = Tween(t, (TranslateTransform)t.RenderTransform!, TranslateTransform.YProperty, DashboardBillboard.TextRisePx, 0, delay, rise, thud, token);
        }

        int thudMs = DepthLaw.Ms(DashboardBillboard.ThudMs, Env.Level);
        int begin = DepthLaw.Ms(DashboardBillboard.ThudDelayMs, Env.Level);
        _ = Tween(_cta, _cta, OpacityProperty, 0, 1, begin, thudMs, thud, token);
        _ = Tween(_cta, _ctaThudScale, ScaleTransform.ScaleXProperty, 0.6, 1, begin, thudMs, thud, token);
        _ = Tween(_cta, _ctaThudScale, ScaleTransform.ScaleYProperty, 0.6, 1, begin, thudMs, thud, token);
        _ = Tween(_cta, _ctaThudMove, TranslateTransform.YProperty, 6, 0, begin, thudMs, thud, token);

        if (_badge.IsVisible)
        {
            int bBegin = DepthLaw.Ms(DashboardBillboard.BadgeDelayMs, Env.Level);
            _ = Tween(_badge, _badge, OpacityProperty, 0, 1, bBegin, thudMs, thud, token);
            _ = Tween(_badge, _badgeScale, ScaleTransform.ScaleXProperty, 0.6, 1, bBegin, thudMs, thud, token);
            _ = Tween(_badge, _badgeScale, ScaleTransform.ScaleYProperty, 0.6, 1, bBegin, thudMs, thud, token);
        }
    }

    /// <summary>When the button's landing flare starts (WPF Fx.cs:115: begin + 55% of the thud), in seconds.</summary>
    private static double CtaLandDelaySeconds =>
        (DepthLaw.Ms(DashboardBillboard.ThudDelayMs, Env.Level) + DepthLaw.Ms(DashboardBillboard.ThudMs, Env.Level) * 0.55) / 1000.0;

    /// <summary>Everything at rest, no animation (Motion Off, the first card, the same card again).</summary>
    private void SettleWords()
    {
        NewWordsRun();
        ResetFold();
        foreach (var t in new[] { _eyebrow, _title, _line })
        {
            t.Opacity = 1;
            ((TranslateTransform)t.RenderTransform!).Y = 0;
        }
        _cta.Opacity = 1;
        _ctaThudScale.ScaleX = _ctaThudScale.ScaleY = 1;
        _ctaThudMove.Y = 0;
        _badgeScale.ScaleX = _badgeScale.ScaleY = 1;
    }

    /// <summary>Snooze: the words and the badge fold away (fade 200 ms, drop 10 px in 250 ms, ease-in).</summary>
    private void Fold()
    {
        if (!Env.AllowTransitions) return;
        var token = NewWordsRun();
        var ease = new QuadraticEaseIn();
        _ = Tween(_meta, _meta, OpacityProperty, 1, 0, 0, 200, ease, token);
        _ = Tween(_meta, _metaMove, TranslateTransform.YProperty, 0, 10, 0, 250, ease, token);
        _ = Tween(_badge, _badge, OpacityProperty, 1, 0, 0, 200, ease, token);
        _ = Tween(_badge, _badgeMove, TranslateTransform.YProperty, 0, 10, 0, 250, ease, token);
    }

    // WPF ResetFold leaves the badge's fold drop in place until the next settle; this resets it too
    // so a snoozed card's successor never wears its badge 10 px low.
    private void ResetFold()
    {
        _meta.Opacity = 1;
        _metaMove.Y = 0;
        _badge.Opacity = 1;
        _badgeMove.Y = 0;
    }

    // ---- the toast and the fades (WPF Fx.cs:249-265, BillboardCardHost.cs:616-625) -----------

    private void Toast(string text)
    {
        _toastText.Text = text;
        var move = (TranslateTransform)_toast.RenderTransform!;
        var run = FadeRun(_toast);
        if (Env.AllowTransitions)
        {
            _ = Tween(_toast, _toast, OpacityProperty, 0, 1, 0, 250, new LinearEasing(), run);
            _ = Tween(_toast, move, TranslateTransform.YProperty, 8, 0, 0, 250, new LinearEasing(), run);
        }
        else
        {
            _toast.Opacity = 1;
            move.Y = 0;
        }
        // WPF DispatcherTimerLite: a newer toast restarts the 1.8 s wait.
        int token = ++_toastToken;
        After(1800, () => { if (token == _toastToken) FadeTo(_toast, 0, 250); });
    }

    /// <summary>WPF FadeTo: from wherever it is to <paramref name="to"/> in <paramref name="ms"/>
    /// (linear), or at once when Motion is Off.</summary>
    private void FadeTo(Control e, double to, int ms)
    {
        double from = e.Opacity;
        var run = FadeRun(e);
        _ = Tween(e, e, OpacityProperty, from, to, 0, Env.AllowTransitions ? ms : 0, new LinearEasing(), run);
    }

    private void StopFade(Control e)
    {
        if (_fades.Remove(e, out var run)) { run.Cancel(); run.Dispose(); }
    }

    private CancellationToken FadeRun(Control e)
    {
        StopFade(e);
        var run = new CancellationTokenSource();
        _fades[e] = run;
        return run.Token;
    }

    // ---- the dots (WPF .Chips.cs:238-259) ----------------------------------------------------

    /// <summary>The current dot's small tick on landing: to 1.12 in 120 ms (quadratic out), back to
    /// 1 by 300 ms on the thud.</summary>
    private void Tick(Control chip)
    {
        if (chip is not Button { Content: Control root } || root.RenderTransform is not ScaleTransform s) return;
        _chipRun?.Cancel();
        _chipRun = new CancellationTokenSource();
        var curve = new TickCurve();
        _ = Tween(root, s, ScaleTransform.ScaleXProperty, 1, 1.12, 0, 300, curve, _chipRun.Token, rest: 1);
        _ = Tween(root, s, ScaleTransform.ScaleYProperty, 1, 1.12, 0, 300, curve, _chipRun.Token, rest: 1);
    }

    /// <summary>Snooze: the current dot pops off the row (scale to 0 and fade, 320 ms ease-in).</summary>
    private void PopCurrentChip()
    {
        if (_currentChip is not Button { Content: Control root } chip || !Env.AllowTransitions) return;
        if (root.RenderTransform is not ScaleTransform s) return;
        _chipRun?.Cancel();
        _chipRun = new CancellationTokenSource();
        var ease = new QuadraticEaseIn();
        int ms = DashboardBillboard.SnoozeFoldMs;
        _ = Tween(root, s, ScaleTransform.ScaleXProperty, s.ScaleX, 0, 0, ms, ease, _chipRun.Token);
        _ = Tween(root, s, ScaleTransform.ScaleYProperty, s.ScaleY, 0, 0, ms, ease, _chipRun.Token);
        _ = Tween(chip, chip, OpacityProperty, chip.Opacity, 0, 0, ms, ease, _chipRun.Token);
    }

    // ---- particles (WPF Fx.cs:269-356) -------------------------------------------------------

    private readonly Random _rng = new();
    private const int MaxParticles = 160;

    /// <summary>Sparks in the card's hue flying out of a point, falling under a little gravity.</summary>
    private void Burst(Point at, Color hue, int count, double speed)
    {
        if (!Env.AllowFeedbackParticles) return;   // press, snooze and CTA hover: the user's own action (fix3 pop 1b)
        for (int i = 0; i < count && _sparks.Count < MaxParticles; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = (70 + _rng.NextDouble() * 230) * speed;
            _sparks.Add(new Spark
            {
                X = at.X, Y = at.Y,
                Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v - 70,
                G = 420, Life = 0, Max = 0.45 + _rng.NextDouble() * 0.45,
                R = 1.6 + _rng.NextDouble() * 2.4, Hue = hue,
            });
        }
        _sparks.Kick();
    }

    /// <summary>Arrival motes: a few soft dots drifting up out of the words.</summary>
    private void Motes(DeckCard card)
    {
        if (!Env.AllowParticles || _meta.Bounds.Width <= 0) return;
        var hue = HueOf(card);
        var origin = _meta.TranslatePoint(default, _stageRoot) ?? default;
        double bw = _meta.Bounds.Width, bh = _meta.Bounds.Height;
        for (int i = 0; i < 14 && _sparks.Count < MaxParticles; i++)
        {
            _sparks.Add(new Spark
            {
                X = origin.X + _rng.NextDouble() * bw,
                Y = origin.Y + bh - _rng.NextDouble() * bh * 0.5,
                Vx = (_rng.NextDouble() - 0.5) * 26, Vy = -(28 + _rng.NextDouble() * 60),
                G = 0, Life = -_rng.NextDouble() * 0.35, Max = 0.9 + _rng.NextDouble() * 0.7,
                R = 1 + _rng.NextDouble() * 1.7, Hue = hue,
            });
        }
        _sparks.Kick();
    }

    /// <summary>The button's hover glints (WPF Cta.cs:186-190): six sparks off its top-right corner.</summary>
    private void CtaGlints()
    {
        if (_card == null || _cta.Bounds.Width <= 0) return;
        var corner = _cta.TranslatePoint(new Point(_cta.Bounds.Width - 8, 6), _stageRoot);
        if (corner is { } p) Burst(p, Lighter(HueOf(_card)), 6, 0.32);
    }

    private static Color Lighter(Color c) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * 0.5), (byte)(c.G + (255 - c.G) * 0.5), (byte)(c.B + (255 - c.B) * 0.5));

    internal struct Spark
    {
        public double X, Y, Vx, Vy, G, Life, Max, R;
        public Color Hue;
    }

    /// <summary>WPF FxSurface + FrameClock: the sparks, painted on the shared 30 fps beat that runs
    /// only while a particle lives.</summary>
    private sealed class SparkLayer : Control
    {
        private readonly List<Spark> _live = new();
        private BeatLoop? _beat;
        private double _last;

        public SparkLayer() => IsHitTestVisible = false;

        public int Count => _live.Count;

        public void Add(Spark s) => _live.Add(s);

        public void Kick()
        {
            if (_live.Count == 0 || _beat != null) return;
            _last = 0;
            _beat = new BeatLoop(this, Step);
            _beat.Start();
        }

        public void Clear()
        {
            _live.Clear();
            _beat?.Stop();
            _beat = null;
            InvalidateVisual();
        }

        private void Step(double t)
        {
            double dt = Math.Clamp(t - _last, 0, 0.05);
            _last = t;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var p = _live[i];
                p.Life += dt;
                if (p.Life >= p.Max) { _live.RemoveAt(i); continue; }
                if (p.Life >= 0)
                {
                    p.Vy += p.G * dt;
                    p.Vx *= 0.985;
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                }
                _live[i] = p;
            }
            InvalidateVisual();
            if (_live.Count == 0) { _beat?.Stop(); _beat = null; }
        }

        public override void Render(DrawingContext context)
        {
            foreach (var p in _live)
            {
                if (p.Life < 0) continue;
                double k = Math.Clamp(1 - p.Life / p.Max, 0, 1);
                double r = p.R * (0.6 + 0.4 * k);
                context.DrawEllipse(new SolidColorBrush(p.Hue, k), null, new Point(p.X, p.Y), r, r);
            }
        }
    }

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>WPF DepthRules.ShadowColor: deep ink pulled toward the hue, at ShadowAlpha.</summary>
    private static Color ShadowOf(Color hue)
    {
        uint c = DepthLaw.ShadowColor(0xFF000000u | (uint)hue.R << 16 | (uint)hue.G << 8 | hue.B);
        return Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
    }

    /// <summary>How a delayed step is scheduled: a one-shot dispatcher timer, as WPF After. Tests
    /// swap it (the headless dispatcher never fires timers) to see and run the delays.</summary>
    internal static Action<TimeSpan, Action> Schedule = (after, run) => DispatcherTimer.RunOnce(run, after);

    /// <summary>WPF After: a one-shot dispatcher timer (or now, for 0 ms).</summary>
    private static void After(int ms, Action run)
    {
        void Safe()
        {
            try { run(); }
            catch (Exception ex) { Log.Debug("Billboard delayed step failed: {E}", ex.Message); }
        }
        if (ms <= 0) { Safe(); return; }
        Schedule(TimeSpan.FromMilliseconds(ms), Safe);
    }

    /// <summary>WPF Animate(target, prop, from, to, begin, ms, ease) with FillBehavior.HoldEnd: the
    /// property's own value becomes <paramref name="rest"/> (default <paramref name="to"/>) at once,
    /// and a one-shot animation on <paramref name="host"/>'s clock plays from -> to after the delay,
    /// holding <paramref name="from"/> while it waits. <paramref name="owner"/> is the host itself or
    /// a Scale/TranslateTransform inside its RenderTransform. Cancelling snaps to the rest value.</summary>
    internal static Task Tween(Animatable host, AvaloniaObject owner, AvaloniaProperty<double> prop, double from, double to,
        int delayMs, int ms, Easing ease, CancellationToken token, double? rest = null)
    {
        owner.SetValue(prop, rest ?? to);
        if (ms <= 0 || token.IsCancellationRequested) return Task.CompletedTask;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(ms),
            Delay = TimeSpan.FromMilliseconds(Math.Max(0, delayMs)),
            Easing = ease,
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(prop, from) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(prop, to) } },
            },
        };
        try { return animation.RunAsync(host, token); }
        catch (Exception ex)
        {
            Log.Debug("Billboard tween {Prop} failed: {E}", prop.Name, ex.Message);
            return Task.CompletedTask;
        }
    }

    /// <summary>The house thud, cubic-bezier(.2,1.5,.4,1) (WPF CubicBezierEase.Thud).</summary>
    internal sealed class ThudEase : Easing
    {
        public override double Ease(double progress) => BillboardTiming.Thud(progress);
    }

    /// <summary>WPF Tick's two key frames on one clock: 0 -> 1 over the first 120 of 300 ms
    /// (quadratic out), then back to 0 on the thud.</summary>
    internal sealed class TickCurve : Easing
    {
        public override double Ease(double p)
        {
            const double split = 120.0 / 300.0;
            if (p <= split) { double q = p / split; return 1 - (1 - q) * (1 - q); }
            return 1 - BillboardTiming.Thud((p - split) / (1 - split));
        }
    }
}
