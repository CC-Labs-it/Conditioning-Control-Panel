// Adapted from WPF MainWindow.BannerFx.cs and ChromeFx.cs at the pinned base.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private BeatLoop? _bannerFxClock;
    private double _bannerFxTime;
    private double _bannerBeatAt = -100;
    private double _bannerSheenAt = -100;
    private DateTimeOffset? _lastBannerSheenUtc;
    private double _bannerSparklesAt = -100;
    private double _bannerNextSparkles = 12;
    private double _bannerGlowBlur = double.NaN;   // first frame always paints the theme colour
    private TextBlock? _bannerOutgoing, _bannerIncoming;
    private bool _bannerRolled, _bannerFxClosed, _bannerFxStarting;
    private int _bannerSparkleSeed = Environment.TickCount;
    private readonly System.Collections.Generic.List<(Control Sprite, double From, double To, double Delay, double Spin)> _bannerStars = new();
    private bool BannerFxActive => !_bannerFxClosed && IsActive && WindowState != WindowState.Minimized;
    private bool BannerAmbient => BannerFxActive && AmbientFxCanvas.Env.AllowAmbientLoops;

    private void InitializeBannerFx()
    {
        _bannerFxClock = new BeatLoop(this, StepBannerFx);
        Activated += (_, _) => RefreshBannerFx();
        Deactivated += (_, _) => RefreshBannerFx();
        PropertyChanged += (_, e) => { if (e.Property == WindowStateProperty) RefreshBannerFx(); };
        AmbientFxCanvas.Env.MotionGateChanged += RefreshBannerFx;
        Closed += (_, _) =>
        {
            _bannerFxClosed = true;
            AmbientFxCanvas.Env.MotionGateChanged -= RefreshBannerFx;
            RefreshBannerFx();
        };
        RefreshBannerFx();
    }

    private void RefreshBannerFx()
    {
        if (!BannerFxActive || !AmbientFxCanvas.Env.AllowTransitions)
        {
            _bannerFxClock?.Stop();
            if (!AmbientFxCanvas.Env.AllowTransitions)
                foreach (var beat in _bannerBeats) EnsureFadeTransition(beat);
            ResetBannerFaces();
            ClearBannerStars();
            if (Named<Border>("BannerSheen") is { } sheen) sheen.Opacity = 0;
            if (Named<Border>("BannerFlashRing") is { } ring) ring.Opacity = 0;
            if (Named<Border>("BannerGlow") is { } glow) glow.Opacity = .35;
            _bannerBeatAt = _bannerSheenAt = _bannerSparklesAt = -100;
        }
        else if (BannerAmbient) StartBannerClock();
    }

    private void StartBannerClock()
    {
        if (_bannerFxClock is not { IsRunning: false }) return;
        // BeatLoop restarts elapsed time at zero; keep all tracks on that same epoch.
        _bannerFxTime = 0;
        _bannerBeatAt = _bannerSheenAt = _bannerSparklesAt = -100;
        _bannerNextSparkles = 12;
        _bannerIncoming = _bannerOutgoing = null;
        _bannerFxStarting = true;
        try { _bannerFxClock.Start(); }
        finally { _bannerFxStarting = false; }
    }

    private static (ScaleTransform Scale, TranslateTransform Slide) BannerFace(TextBlock beat)
    {
        if (beat.RenderTransform is TransformGroup g && g.Children.Count == 2
            && g.Children[0] is ScaleTransform s && g.Children[1] is TranslateTransform t) return (s, t);
        var scale = new ScaleTransform(1, 1);
        var slide = new TranslateTransform();
        beat.RenderTransform = new TransformGroup { Children = { scale, slide } };
        beat.RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        return (scale, slide);
    }

    private void ResetBannerFaces()
    {
        foreach (var beat in _bannerBeats)
        {
            var (scale, slide) = BannerFace(beat);
            scale.ScaleX = scale.ScaleY = 1;
            slide.Y = 0;
        }
    }

    private void OnBannerBeatChanged(TextBlock outgoing, TextBlock incoming)
    {
        ResetBannerFaces();
        if (!BannerFxActive || !AmbientFxCanvas.Env.AllowTransitions) return;
        StartBannerClock();
        _bannerOutgoing = outgoing;
        _bannerIncoming = incoming;
        _bannerRolled = CoreSettings.Current.MotionLevel == MotionLevel.Full;
        _bannerBeatAt = _bannerFxTime;
        SweepBannerSheen();
        if (ReferenceEquals(incoming, BannerPrimary))
        {
            RunBannerSparkles();
            SweepBannerSheen(true);
            _bannerNextSparkles = _bannerFxTime + 12;
        }
        StepBannerFx(_bannerFxTime);
    }

    internal void SweepBannerSheen(bool force = false)
    {
        if (!BannerFxActive || !AmbientFxCanvas.Env.AllowTransitions) return;
        StartBannerClock();
        var now = BeatLoop.Time.GetUtcNow();
        if (!force && _lastBannerSheenUtc is { } last && (now - last).TotalSeconds < 20) return;
        _lastBannerSheenUtc = now;
        _bannerSheenAt = _bannerFxTime;
    }

    private void StepBannerFx(double seconds)
    {
        _bannerFxTime = seconds;
        if (BannerAmbient && seconds >= _bannerNextSparkles)
        {
            _bannerNextSparkles = seconds + 12;
            if (_bannerCurrentIndex < _bannerBeats.Length && ReferenceEquals(_bannerBeats[_bannerCurrentIndex], BannerPrimary))
                RunBannerSparkles();
        }
        var elapsed = seconds - _bannerBeatAt;
        if (_bannerIncoming is { } incoming && _bannerOutgoing is { } outgoing)
        {
            var (scale, slide) = BannerFace(incoming);
            var (outScale, outSlide) = BannerFace(outgoing);
            var pop = BannerBack(Math.Clamp(elapsed / .26, 0, 1), .6);
            scale.ScaleX = .96 + .04 * pop;
            if (_bannerRolled && elapsed < .5)
            {
                var p = Math.Clamp(elapsed / .5, 0, 1);
                var settle = BannerBack(p, .3);
                slide.Y = 13 * (1 - settle);
                scale.ScaleY = .3 + .7 * settle;
                outSlide.Y = -13 * p * p;
                outScale.ScaleY = 1 - .7 * p * p;
            }
            else
            {
                slide.Y = outSlide.Y = 0;
                scale.ScaleY = _bannerRolled ? 1 : .96 + .04 * pop;
                outScale.ScaleY = 1;
            }
        }
        var flash = elapsed <= .07 ? .95 * Math.Clamp(elapsed / .07, 0, 1)
            : .95 * Math.Pow(1 - Math.Clamp((elapsed - .07) / .33, 0, 1), 2);
        if (Named<Border>("BannerFlashRing") is { } ring) ring.Opacity = Math.Clamp(flash, 0, 1);
        if (Named<Border>("BannerGlow") is { } glow)
        {
            glow.Opacity = BannerAmbient ? .25 + .35 * BeatLoop.Breath(seconds, 3.2) : .35;
            var flare = elapsed <= .09 ? Math.Clamp(elapsed / .09, 0, 1)
                : Math.Pow(1 - Math.Clamp((elapsed - .09) / .31, 0, 1), 2);
            // Only during the 0.4 s flare: a new BoxShadows every ambient frame is pure allocation.
            var blur = 12 + 10 * flare;
            if (blur != _bannerGlowBlur)
            {
                _bannerGlowBlur = blur;
                glow.BoxShadow = new BoxShadows(new BoxShadow { Blur = blur, Color = Pr4aGlowColor });
            }
        }
        var sheenTime = (seconds - _bannerSheenAt) / .75;
        if (Named<Border>("BannerSheen") is { } sheen && Named<Grid>("BannerSheenHost") is { } host)
        {
            sheen.Opacity = Math.Clamp(BannerTrack(sheenTime, (0, 0), (.2, .22), (.75, .22), (1, 0)), 0, 1);
            if (sheen.RenderTransform is TransformGroup g && g.Children[1] is TranslateTransform slide)
                slide.X = -110 + (host.Bounds.Width + 137.5) * (1 - Math.Cos(Math.PI * Math.Clamp(sheenTime, 0, 1))) / 2;
        }
        foreach (var star in _bannerStars)
        {
            var p = (seconds - _bannerSparklesAt - star.Delay) / 1.25;
            star.Sprite.Opacity = Math.Clamp(BannerTrack(p, (0, 0), (.12, 1), (.8, 1), (1, 0)), 0, 1);
            if (star.Sprite.RenderTransform is not TransformGroup g) continue;
            var size = BannerTrack(p, (0, 0), (.15, 1), (.4, .5), (.65, 1.1), (1, 0));
            ((ScaleTransform)g.Children[0]).ScaleX = ((ScaleTransform)g.Children[0]).ScaleY = size;
            ((RotateTransform)g.Children[1]).Angle = star.Spin * Math.Clamp(p, 0, 1);
            ((TranslateTransform)g.Children[2]).X = star.From + (star.To - star.From) * (1 - Math.Cos(Math.PI * Math.Clamp(p, 0, 1))) / 2;
        }
        if (seconds - _bannerSparklesAt >= 1.79) ClearBannerStars();
        if (!_bannerFxStarting && !BannerAmbient && elapsed >= .5 && sheenTime >= 1 && _bannerStars.Count == 0) _bannerFxClock?.Stop();
    }

    private static double BannerBack(double p, double amplitude)
    {
        var q = 1 - p;
        return 1 - (q * q * q - q * amplitude * Math.Sin(Math.PI * q));
    }

    private static double BannerTrack(double t, params (double At, double V)[] keys) => BeatLoop.Keys(t, keys);

    private void ClearBannerStars()
    {
        Named<Canvas>("BannerSparkleLayer")?.Children.Clear();
        _bannerStars.Clear();
    }

    private void RunBannerSparkles()
    {
        if (Named<Canvas>("BannerSparkleLayer") is not { } layer || BannerPrimary is not { } text
            || layer.Bounds.Height <= 0 || text.Bounds.Width <= 4) return;
        ClearBannerStars();
        var tint = Pr4aGlowColor;
        _bannerSparklesAt = _bannerFxTime;
        var rng = new Random(_bannerSparkleSeed++);
        var left = text.TranslatePoint(new Point(), layer)?.X ?? 0;
        for (int i = 0; i < 7; i++)
        {
            var size = 11 + rng.NextDouble() * 7;
            var y = layer.Bounds.Height / 2 + (rng.NextDouble() * 2 - 1) * Math.Min(7, layer.Bounds.Height * .3);
            var from = left - 6 + rng.NextDouble() * text.Bounds.Width * .12;
            var to = left + text.Bounds.Width * (.82 + rng.NextDouble() * .2);
            var spin = (rng.NextDouble() < .5 ? -1 : 1) * (90 + rng.NextDouble() * 120);
            // WPF's four-point star is decorative geometry, not an icon.
            var sprite = new Grid { Width = size, Height = size, Opacity = 0, IsHitTestVisible = false };
            sprite.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Fill = new RadialGradientBrush
            {
                GradientStops = { new GradientStop(Color.FromArgb(200, tint.R, tint.G, tint.B), 0),
                    new GradientStop(Colors.Transparent, 1) }
            } });
            sprite.Children.Add(new global::Avalonia.Controls.Shapes.Path { Fill = Brushes.White,
                Data = Geometry.Parse("M 0.5,0 C .55,.4 .6,.45 1,.5 C .6,.55 .55,.6 .5,1 C .45,.6 .4,.55 0,.5 C .4,.45 .45,.4 .5,0 Z"),
                Stretch = Stretch.Fill, Width = size * .72, Height = size * .72,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center });
            sprite.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new RotateTransform(), new TranslateTransform() } };
            Canvas.SetTop(sprite, y - size / 2);
            layer.Children.Add(sprite);
            _bannerStars.Add((sprite, from - size / 2, to - size / 2, i * .09, spin));
        }
    }
}
