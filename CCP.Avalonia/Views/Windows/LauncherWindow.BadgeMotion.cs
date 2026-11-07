// WPF LauncherWindow.TierBadge.cs:22-24,45-97; LauncherWindow.Fx.cs:105-117.
// Reused from fin-win-launcher; all motion shares the existing parked frame clock.
using System;
using Avalonia;
using Avalonia.Media;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;
namespace ConditioningControlPanel.Avalonia.Views.Windows;
public partial class LauncherWindow
{
    private readonly ScaleTransform _tierBigScale = new(1, 1);
    private readonly RotateTransform _tierTilt = new();
    private double _wobbleIn = 8, _wobbleT = -1, _badgePopT = -1, _dotTime;
    private bool _dotRunning;
    private static readonly double[] WobbleTimes = { 0, 0.15, 0.35, 0.55, 0.75, 1 }, WobbleAngles = { 0, -9, 7.2, -4.05, 1.8, 0 };
    private void HookBadgeMotion()
    {
        TierBadge.RenderTransform = _tierTilt;
        TierBadge.RenderTransformOrigin = RelativePoint.Center;
        TierBadgeBig.RenderTransform = _tierBigScale;
        TierBadgeBig.RenderTransformOrigin = RelativePoint.Center;
    }
    private bool StepBadgeMotion(double dt)
    {
        bool busy = false;
        if (_fxActive && Env.AllowAmbientLoops)
        {
            busy = true;
            bool running = CoreEngine.IsRunning;
            if (running != _dotRunning) { _dotRunning = running; _dotTime = 0; }
            _dotTime += dt;
            RunningDot.Opacity = running ? 0.55 + 0.45 * (1 - Math.Cos(Math.PI * _dotTime / 2.6)) / 2 : 0.35;
            if ((_wobbleIn -= dt) <= 0)
            {
                _wobbleIn = 8;
                if (TierBadge.IsVisible && !TierBadgePopup.IsOpen) _wobbleT = 0;
            }
        }
        else { RunningDot.Opacity = CoreEngine.IsRunning ? 1 : 0.35; _wobbleT = -1; _tierTilt.Angle = 0; }
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

    private void ParkBadgeMotion()
    {
        _tierTilt.Angle = 0;
        _wobbleT = _badgePopT = -1;
        TierBadgePopup.IsOpen = false;
    }
}
