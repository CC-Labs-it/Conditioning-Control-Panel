using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.BannerFx.cs / ChromeFx.cs. One shared frame source drives both the
    // finite cues and ambient breath; no compositor animation or Effect lives on this pill.
    private FrameClock? _bannerFxClock;
    private readonly System.Diagnostics.Stopwatch _bannerFxTime = System.Diagnostics.Stopwatch.StartNew();
    private double _bannerBeatAt = -100, _bannerSheenAt = -100, _bannerSparkleAt = -100;
    private TextBlock? _bannerOutgoing, _bannerIncoming;
    private bool _bannerFxClosed;
    private bool BannerAmbient => IsActive && WindowState != WindowState.Minimized && AmbientFxCanvas.Env.AllowAmbientLoops;
    private bool BannerCues => IsActive && WindowState != WindowState.Minimized && AmbientFxCanvas.Env.AllowTransitions;

    private void InitializeBannerFx()
    {
        _bannerFxClock = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
        _bannerFxClock.Tick += (_, _) => StepBannerFx(_bannerFxTime.Elapsed.TotalSeconds);
        Activated += (_, _) => EvaluateBannerFx();
        Deactivated += (_, _) => EvaluateBannerFx();
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) EvaluateBannerFx(); };
        AmbientFxCanvas.Env.MotionGateChanged += EvaluateBannerFx;
        Closed += (_, _) =>
        {
            _bannerFxClosed = true;
            _bannerFxClock.Stop();
            AmbientFxCanvas.Env.MotionGateChanged -= EvaluateBannerFx;
            ParkBannerFx();
        };
        EvaluateBannerFx();
    }

    private void EvaluateBannerFx()
    {
        if (_bannerFxClosed) return;
        if (!BannerCues) ParkBannerFx();
        if (BannerAmbient) _bannerFxClock?.Start();
        else if (!BannerCues) _bannerFxClock?.Stop();
        else StepBannerFx(_bannerFxTime.Elapsed.TotalSeconds);
    }

    private static void ResetBannerTransform(TextBlock? beat)
    {
        if (beat != null) beat.RenderTransform = null;
    }

    internal void StopBannerFxForPanic()
    {
        _bannerFxClock?.Stop();
        ParkBannerFx();
    }

    private void ParkBannerFx()
    {
        ResetBannerTransform(_bannerOutgoing);
        ResetBannerTransform(_bannerIncoming);
        _bannerBeatAt = _bannerSheenAt = _bannerSparkleAt = -100;
        if (Named<Border>("BannerFlashRing") is { } ring) ring.Opacity = 0;
        if (Named<Border>("BannerHalo") is { } halo)
        {
            halo.Opacity = .35;
            SetHaloShadow(halo, 12);
        }
        if (Named<Border>("BannerSheen") is { } sheen) sheen.Opacity = 0;
        Named<Canvas>("BannerSparkleLayer")?.Children.Clear();
    }

    internal void SweepBannerSheen(bool force = false)
    {
        var now = _bannerFxTime.Elapsed.TotalSeconds;
        if (!BannerCues || (!force && now - _bannerSheenAt < 20)) return;
        _bannerSheenAt = now;
        _bannerFxClock?.Start();
    }

    private void OnBannerBeatChanged(TextBlock outgoing, TextBlock incoming)
    {
        ResetBannerTransform(_bannerOutgoing);
        ResetBannerTransform(_bannerIncoming);
        _bannerOutgoing = outgoing;
        _bannerIncoming = incoming;
        // WPF's drum is Full only, even when unfocused. Pop/flash/sparkles require focus.
        _bannerBeatAt = CoreSettings.Current.MotionLevel == MotionLevel.Full || BannerCues
            ? _bannerFxTime.Elapsed.TotalSeconds : -100;
        SweepBannerSheen();
        if (BannerCues && ReferenceEquals(incoming, BannerPrimary))
        {
            RunBannerSparkles(_bannerFxTime.Elapsed.TotalSeconds);
            SweepBannerSheen(force: true);
        }
        if (_bannerBeatAt > -100) _bannerFxClock?.Start();
    }

    private void RunBannerSparkles(double now)
    {
        var layer = Named<Canvas>("BannerSparkleLayer");
        var text = BannerPrimary;
        if (layer == null || text == null || text.Bounds.Width <= 4 || layer.Bounds.Height <= 0) return;
        layer.Children.Clear();
        _bannerSparkleAt = now;
        var rng = new Random(_bannerSparkleSeed++);
        var left = text.TranslatePoint(default, layer)?.X ?? 0;
        for (int i = 0; i < 7; i++)
        {
            double size = 11 + rng.NextDouble() * 7;
            double lane = (rng.NextDouble() * 2 - 1) * Math.Min(7, layer.Bounds.Height * .3);
            double from = left - 6 + rng.NextDouble() * text.Bounds.Width * .12;
            double to = left + text.Bounds.Width * (.82 + rng.NextDouble() * .2);
            double spin = (rng.NextDouble() < .5 ? -1 : 1) * (90 + rng.NextDouble() * 120);
            // Decorative four-point stars, matching WPF's sprite geometry; never an icon slot.
            var star = new Path { Width = size, Height = size, Fill = Brushes.White,
                Stretch = Stretch.Fill, IsHitTestVisible = false, Opacity = 0,
                Data = Geometry.Parse("M .5,0 C .55,.4 .6,.45 1,.5 C .6,.55 .55,.6 .5,1 C .45,.6 .4,.55 0,.5 C .4,.45 .45,.4 .5,0 Z"),
                Tag = (from, to, spin, i * .09) };
            // ponytail: native source-over glow, matching the existing head FX renderer.
            var sprite = new Grid { Width = size, Height = size, Tag = star.Tag, IsHitTestVisible = false, Opacity = 0 };
            var hue = AmbientFxCanvas.Env.GlowColor;
            var glow = new RadialGradientBrush { GradientStops = {
                new GradientStop(Color.FromArgb(200, hue.R, hue.G, hue.B), 0),
                new GradientStop(Color.FromArgb(90, hue.R, hue.G, hue.B), .35),
                new GradientStop(Colors.Transparent, 1) } };
            sprite.Children.Add(new Ellipse { Fill = glow });
            star.Width = star.Height = size * .72;
            star.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
            star.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
            star.Opacity = 1;
            sprite.Children.Add(star);
            Canvas.SetTop(sprite, layer.Bounds.Height / 2 + lane - size / 2);
            layer.Children.Add(sprite);
        }
    }
    private int _bannerSparkleSeed = Environment.TickCount;
    private (double Blur, Color Color) _bannerHaloShadow;

    // P07: a new BoxShadows only when the flare or glow hue actually changes, not every breath tick.
    private void SetHaloShadow(Border halo, double blur)
    {
        var key = (blur, AmbientFxCanvas.Env.GlowColor);
        if (key == _bannerHaloShadow && halo.BoxShadow.Count > 0) return;
        _bannerHaloShadow = key;
        halo.BoxShadow = new BoxShadows(new BoxShadow { Blur = blur, Color = key.Item2 });
    }

    // Explicit time is also the test seam: assert actual transforms/opacity and sprite cleanup.
    internal void StepBannerFx(double now)
    {
        double age = now - _bannerBeatAt;
        bool full = CoreSettings.Current.MotionLevel == MotionLevel.Full;
        bool cues = BannerCues;
        static double Back(double t, double a) { t -= 1; return 1 + (a + 1) * t * t * t + a * t * t; }
        if (age >= 0 && age < .5 && (full || cues))
        {
            double t = Math.Clamp(age / .5, 0, 1), settle = Back(t, .3);
            double pop = cues && age < .26 ? .96 + .04 * Back(age / .26, .6) : 1;
            void Face(TextBlock? beat, double sy, double y, double sx)
            {
                if (beat == null) return;
                beat.RenderTransformOrigin = RelativePoint.Center;
                beat.RenderTransform = new TransformGroup { Children = { new ScaleTransform(sx, sy), new TranslateTransform(0, y) } };
            }
            Face(_bannerOutgoing, full ? 1 - .7 * t * t : 1, full ? -13 * t * t : 0, 1);
            Face(_bannerIncoming, full ? .3 + .7 * settle : pop, full ? 13 * (1 - settle) : 0, pop);
        }
        else { ResetBannerTransform(_bannerOutgoing); ResetBannerTransform(_bannerIncoming); }
        double flash = cues && age >= 0 && age < .4 ? (age < .07 ? .95 * age / .07 : .95 * Math.Pow(1 - (age - .07) / .33, 2)) : 0;
        if (Named<Border>("BannerFlashRing") is { } ring) ring.Opacity = Math.Clamp(flash, 0, 1);
        if (Named<Border>("BannerHalo") is { } halo)
        {
            halo.Opacity = BannerAmbient ? .25 + .35 * (.5 - .5 * Math.Cos(now * Math.PI / 3.2)) : .35;
            double flare = cues && age >= 0 && age < .4
                ? age < .09 ? age / .09 : Math.Pow(1 - (age - .09) / .31, 2) : 0;
            SetHaloShadow(halo, 12 + 10 * flare);
        }
        double sweep = now - _bannerSheenAt;
        if (Named<Border>("BannerSheen") is { } sheen)
        {
            double t = Math.Clamp(sweep / .75, 0, 1);
            sheen.Opacity = cues && sweep >= 0 && sweep < .75
                ? .22 * (t < .2 ? t / .2 : t < .75 ? 1 : (1 - t) / .25) : 0;
            // The XAML's own slide (WPF BannerSheenSlide), moved only while the band is visible.
            if (sheen.Opacity > 0 && sheen.RenderTransform is TransformGroup { Children: [_, TranslateTransform slide] })
            {
                var width = Named<Grid>("BannerSheenHost")?.Bounds.Width ?? 0;
                slide.X = -110 + (width + 137.5) * (.5 - .5 * Math.Cos(t * Math.PI));
            }
        }
        bool support = _bannerCurrentIndex < _bannerBeats.Length && ReferenceEquals(_bannerBeats[_bannerCurrentIndex], BannerPrimary);
        if (BannerAmbient && support && now - _bannerSparkleAt >= 12) RunBannerSparkles(now);
        var layer = Named<Canvas>("BannerSparkleLayer");
        if (layer != null)
        {
            if (!cues || now - _bannerSparkleAt >= 1.79) layer.Children.Clear();
            foreach (var child in layer.Children)
            {
                var (from, to, spin, delay) = ((double, double, double, double))child.Tag!;
                double t = Math.Clamp((now - _bannerSparkleAt - delay) / 1.25, 0, 1);
                double scale = t < .15 ? t / .15 : t < .4 ? 1 - .5 * (t - .15) / .25 : t < .65 ? .5 + .6 * (t - .4) / .25 : 1.1 * (1 - (t - .65) / .35);
                child.Opacity = Math.Clamp(t < .12 ? t / .12 : t < .8 ? 1 : (1 - t) / .2, 0, 1);
                child.RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale),
                    new RotateTransform(spin * t), new TranslateTransform(from + (to - from) * (.5 - .5 * Math.Cos(t * Math.PI)) - child.Width / 2, 0) } };
            }
        }
        if (!BannerAmbient && age >= .5 && sweep >= .75 && (layer == null || layer.Children.Count == 0)) _bannerFxClock?.Stop();
    }
}
