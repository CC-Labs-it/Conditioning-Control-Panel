// WPF MainWindow.Leash.cs:440-450 and LeashFx.cs:68-73,88-114.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Platform;

internal sealed class LeashTug
{
    private BeatLoop? _loop;
    private Control? _target;
    private ITransform? _prior;
    private TransformGroup? _group;
    private readonly RotateTransform _turn = new();
    private readonly TranslateTransform _move = new();
    private RelativePoint _origin;
    private double _amount;

    internal bool Play(Window? panel, Window? launcher, MotionLevel motion)
    {
        var window = Showing(panel) ? panel : Showing(launcher) ? launcher : null;
        if (motion == MotionLevel.Off || window?.Content is not Control target || !target.IsEffectivelyVisible)
            return false;
        Stop();
        _target = target;
        _prior = target.RenderTransform;
        _origin = target.RenderTransformOrigin;
        _group = new TransformGroup();
        if (_prior is Transform transform) _group.Children.Add(transform);
        else if (_prior != null) _group.Children.Add(new MatrixTransform(_prior.Value));
        _group.Children.Add(_turn);
        _group.Children.Add(_move);
        target.RenderTransformOrigin = RelativePoint.Center;
        target.RenderTransform = _group;
        _amount = (motion == MotionLevel.Reduced ? 0.5 : 1) * 0.8;
        // One-shot on the window's shared beat (AmbientBeatLoopsTests.NoNewFrameTimerLoop); a hide ends it at rest.
        _loop = new BeatLoop(target, t => Step(t * 1000)) { OnParked = Stop };
        _loop.Start();
        return true;
    }

    private static bool Showing(Window? window) => window is { IsVisible: true, WindowState: not WindowState.Minimized };

    internal void Step(double ms)
    {
        if (_target == null) return;
        if (ms >= 520) { Stop(); return; }
        double u = Math.Clamp(ms / 520, 0, 1);
        double x, angle;
        if (u <= 0.3)
        {
            var t = u / 0.3;
            x = -12 * (1 - (1 - t) * (1 - t));
            angle = -2.2 * t;
        }
        else if (u <= 0.6)
        {
            var t = (u - 0.3) / 0.3;
            x = -12 + 17 * (t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2);
            angle = -2.2 + 3.3 * t;
        }
        else
        {
            var t = (u - 0.6) / 0.4;
            x = 5 * (1 - Thud(t));
            angle = 1.1 * (1 - t);
        }
        _move.X = x * _amount;
        _turn.Angle = angle * _amount;
    }

    // WPF LeashFx.cs:16-46 BezierEase, cubic-bezier(.2, 1.5, .4, 1). Not SplineEasing: Avalonia's
    // KeySpline clamps Y to [0, 1], which drops the overshoot past rest.
    private static double Thud(double x)
    {
        double t = x = Math.Clamp(x, 0, 1);
        for (int i = 0; i < 8; i++)
        {
            double cx = Bez(t, 0.2, 0.4) - x;
            double d = 3 * 0.2 * (1 - t) * (1 - t) + 6 * (0.4 - 0.2) * t * (1 - t) + 3 * (1 - 0.4) * t * t;
            if (Math.Abs(cx) < 1e-5 || Math.Abs(d) < 1e-6) break;
            t = Math.Clamp(t - cx / d, 0, 1);
        }
        return Bez(t, 1.5, 1.0);
    }

    private static double Bez(double t, double a, double b) => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;

    internal void Stop()
    {
        _loop?.Stop();
        _loop = null;
        if (_target != null && ReferenceEquals(_target.RenderTransform, _group))
        {
            _target.RenderTransform = _prior;
            _target.RenderTransformOrigin = _origin;
        }
        _target = null;
        _group = null;
        _prior = null;
        _move.X = _turn.Angle = 0;
    }
}
