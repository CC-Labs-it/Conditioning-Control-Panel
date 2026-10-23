// Adapted from WPF LauncherWindow.Backdrop.cs, .Fx.cs, .TierBadge.cs and RefreshStats.
// All loops share the launcher's existing clock; transforms are stepped, never Animation.RunAsync targets.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    private sealed class Shift
    {
        public readonly TranslateTransform Transform = new();
        public Tween X, Y;
        public double Depth;
    }
    private sealed class ArtMotion
    {
        public Image Art = null!;
        public readonly ScaleTransform Scale = new(1, 1);
        public double From = 1, To = 1.07, T, Duration = 14;
        public bool Hovered, Outward;
    }
    private readonly List<ArtMotion> _artMotion = new();
    private readonly Shift[] _shifts = { new() { Depth = 18 }, new() { Depth = 14 }, new() { Depth = 8 },
        new() { Depth = -12 }, new() { Depth = -8.4 } };
    private readonly ScaleTransform _wordmarkScale = new(1, 1), _tierBigScale = new(1, 1);
    private readonly RotateTransform _tierTilt = new();
    private double _ambientTime, _artApplyIn, _wordmarkApplyIn, _wobbleIn = 8, _wobbleT = -1, _badgePopT = -1;
    private DateTime _lastParallax = DateTime.MinValue;
    private bool _dotRunning;
    private double _dotTime;
    private Tween _levelTween, _sparkleTween, _xpTween;
    private double _shownLevel, _shownSparkles, _shownXpWidth = -1;
    private static readonly double[] WobbleTimes = { 0, 0.15, 0.35, 0.55, 0.75, 1 }, WobbleAngles = { 0, -9, 7.2, -4.05, 1.8, 0 };

    private void HookMotion()
    {
        GlowLayer.RenderTransform = _shifts[0].Transform;
        PoolLayer.RenderTransform = _shifts[1].Transform;
        Ambient.RenderTransform = _shifts[2].Transform;
        SpiralLayer.RenderTransform = new TransformGroup { Children = { SpiralTurn, _shifts[3].Transform } };
        SpiralSmall.RenderTransform = new TransformGroup { Children = { SpiralSmallTurn, _shifts[4].Transform } };
        WordmarkImage.RenderTransform = _wordmarkScale;
        WordmarkImage.RenderTransformOrigin = RelativePoint.Center;
        TierBadge.RenderTransform = _tierTilt;
        TierBadge.RenderTransformOrigin = RelativePoint.Center;
        TierBadgeBig.RenderTransform = _tierBigScale;
        TierBadgeBig.RenderTransformOrigin = RelativePoint.Center;
        PanelArtPlate.ClipToBounds = true;
        DecorateArt(PanelArt);
        PointerMoved += (_, e) =>
        {
            if (!_fxActive || !TiltAllowed || MainShellWindow.LockdownActive) return;
            var now = LauncherSfx.Clock.GetUtcNow().UtcDateTime;
            if ((now - _lastParallax).TotalMilliseconds < 33 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
            _lastParallax = now;
            var at = e.GetPosition(this);
            Parallax(Math.Clamp(at.X / Bounds.Width * 2 - 1, -1, 1), Math.Clamp(at.Y / Bounds.Height * 2 - 1, -1, 1));
        };
        PointerExited += (_, _) => Parallax(0, 0);
    }

    private void Parallax(double x, double y)
    {
        foreach (var s in _shifts)
        {
            s.X.Go(s.Transform.X, x * s.Depth, TiltAllowed ? 0.26 : 0);
            s.Y.Go(s.Transform.Y, y * s.Depth, TiltAllowed ? 0.26 : 0);
        }
        EnsureFxClock();
    }

    private void DecorateArt(Image image)
    {
        var art = new ArtMotion { Art = image, Outward = (_artMotion.Count & 1) == 0 };
        image.RenderTransform = art.Scale;
        image.RenderTransformOrigin = RelativePoint.Center;
        _artMotion.Add(art);
        image.PointerEntered += (_, _) => art.Hovered = true;
        image.PointerExited += (_, _) =>
        {
            art.Hovered = false;
            art.From = art.Scale.ScaleX;
            art.To = art.Outward ? 1.07 : 1;
            if (Math.Abs(art.From - art.To) < 0.002) art.To = art.Outward ? 1 : 1.07;
            art.T = 0;
            art.Duration = 14 * Math.Abs(art.To - art.From) / 0.07;
            EnsureFxClock();
        };
        HoverPop.SetIsEnabled(image, true);
    }

    private void TweenStats(int level, int sparkles, double width)
    {
        if (level != _shownLevel)
        {
            _levelTween.Go(_shownLevel, level, Env.AllowTransitions ? (_shownLevel == 0 ? 0.9 : 0.5) : 0);
            _shownLevel = level;
        }
        if (sparkles != _shownSparkles)
        {
            _sparkleTween.Go(_shownSparkles, sparkles, Env.AllowTransitions ? (_shownSparkles == 0 ? 1.1 : 0.5) : 0);
            _shownSparkles = sparkles;
        }
        if (width >= 0 && Math.Abs(width - _shownXpWidth) >= 0.5)
        {
            _xpTween.Go(_shownXpWidth < 0 ? 0 : _shownXpWidth, width, Env.AllowTransitions ? (_shownXpWidth < 0 ? 1 : 0.5) : 0);
            _shownXpWidth = width;
        }
        PaintStats(0);
        EnsureFxClock();
    }

    private void PaintStats(double dt)
    {
        StatLevel.Text = _levelTween.Step(dt).ToString("0");
        StatSparkles.Text = _sparkleTween.Step(dt).ToString("N0");
        XpFill.Width = Math.Max(0, _xpTween.Step(dt));
    }

    private bool StepMotion(double dt)
    {
        bool busy = _levelTween.On || _sparkleTween.On || _xpTween.On;
        if (busy) PaintStats(dt);
        foreach (var s in _shifts)
        {
            busy |= s.X.On || s.Y.On;
            s.Transform.X = s.X.Step(dt);
            s.Transform.Y = s.Y.Step(dt);
        }
        bool ambient = _fxActive && Env.AllowAmbientLoops;
        if (ambient)
        {
            busy = true;
            _ambientTime += dt;
            if ((_wordmarkApplyIn -= dt) <= 0)
            {
                _wordmarkApplyIn = 0.1;
                _wordmarkScale.ScaleX = _wordmarkScale.ScaleY = 1 + 0.04 * Swing(_ambientTime, 40);
            }
            bool running = CoreEngine.IsRunning;
            if (running != _dotRunning) { _dotRunning = running; _dotTime = 0; }
            _dotTime += dt;
            RunningDot.Opacity = running ? 0.55 + 0.45 * Swing(_dotTime, 2.6) : 0.35;
            if ((_wobbleIn -= dt) <= 0)
            {
                _wobbleIn = 8;
                if (TierBadge.IsVisible && !TierBadgePopup.IsOpen) _wobbleT = 0;
            }
            if (!PerfLow)
            {
                _artApplyIn -= dt;
                foreach (var a in _artMotion)
                {
                    if (a.Hovered) continue;
                    a.T = Math.Min(a.Duration, a.T + dt);
                    if (_artApplyIn <= 0) a.Scale.ScaleX = a.Scale.ScaleY = a.From + (a.To - a.From) *
                        (1 - Math.Cos(Math.PI * a.T / Math.Max(0.001, a.Duration))) / 2;
                    if (a.T >= a.Duration) { a.From = a.To; a.To = a.To > 1.035 ? 1 : 1.07; a.T = 0; a.Duration = 14; }
                }
                if (_artApplyIn <= 0) _artApplyIn = 1.0 / 12;
            }
        }
        else RunningDot.Opacity = CoreEngine.IsRunning ? 1 : 0.35;
        if (_wobbleT >= 0)
        {
            busy = true;
            _wobbleT += dt;
            double u = Math.Min(1, _wobbleT / 0.7);
            for (int i = 1; i < WobbleTimes.Length; i++)
                if (u <= WobbleTimes[i])
                {
                    double v = (u - WobbleTimes[i - 1]) / (WobbleTimes[i] - WobbleTimes[i - 1]);
                    if (i == WobbleTimes.Length - 1) v = Math.Sin(v * Math.PI / 2);
                    _tierTilt.Angle = WobbleAngles[i - 1] + (WobbleAngles[i] - WobbleAngles[i - 1]) * v;
                    break;
                }
            if (u >= 1) _wobbleT = -1;
        }
        if (_badgePopT >= 0)
        {
            busy = true;
            _badgePopT += dt;
            double x = 1 - Math.Min(1, _badgePopT / 0.26);
            _tierBigScale.ScaleX = _tierBigScale.ScaleY = 0.35 + 0.65 * (1 - (x * x * x - x * 0.45 * Math.Sin(Math.PI * x)));
            if (x <= 0) _badgePopT = -1;
        }
        return busy;
    }

    private static double Swing(double t, double seconds) => (1 - Math.Cos(Math.PI * t / seconds)) / 2;

    private void ParkMotion()
    {
        foreach (var s in _shifts) { s.X.Go(0, 0, 0); s.Y.Go(0, 0, 0); s.Transform.X = s.Transform.Y = 0; }
        _wordmarkScale.ScaleX = _wordmarkScale.ScaleY = 1;
        _ambientTime = 0;
        _tierTilt.Angle = 0;
        _wobbleT = _badgePopT = -1;
        TierBadgePopup.IsOpen = false;
    }
}
