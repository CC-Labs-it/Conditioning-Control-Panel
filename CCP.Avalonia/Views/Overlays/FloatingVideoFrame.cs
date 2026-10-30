using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Video.Floating;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// Window coordinates and pointer samples are device pixels; content sizes are DIPs.
internal sealed class FloatingVideoFrame
{
    private readonly Window _window;
    private readonly FloatingVideoMotion _motion;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double _last;
    internal double MotionSpeed { get => _motion.Speed; set => _motion.Speed = value; }

    internal FloatingVideoFrame(Window window)
    {
        _window = window;
        var b = window.Screens.ScreenFromWindow(window)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        _motion = new(window.Position.X, window.Position.Y, window.Width * window.RenderScaling,
            window.Height * window.RenderScaling, b.X, b.Y, b.Width, b.Height);
        MotionSpeed = CoreSettings.Current.MandatoryVideoFloatingSpeedPercent / 100.0;
        _motion.MonitorAt = (x, y) =>
        {
            var area = window.Screens.All.OrderBy(s => Distance(s.WorkingArea, x, y)).FirstOrDefault()?.WorkingArea;
            return area is { } a ? (a.X, a.Y, a.Width, a.Height) : null;
        };
        window.PointerPressed += Press;
        window.PointerMoved += Move;
        window.PointerReleased += Release;
        window.PointerCaptureLost += (_, _) => { if (_motion.Held) _motion.Drop(); };
        window.Screens.Changed += DisplaysChanged;
        window.Closed += (_, _) => { _timer.Stop(); _motion.Drop(); window.Screens.Changed -= DisplaysChanged; };
        _timer.Tick += (_, _) => Tick();
        RefreshDesktop();
        _timer.Start();
    }

    private static double Distance(PixelRect r, double x, double y)
    {
        var dx = Math.Max(r.X - x, Math.Max(0, x - r.Right));
        var dy = Math.Max(r.Y - y, Math.Max(0, y - r.Bottom));
        return dx * dx + dy * dy;
    }

    private void RefreshDesktop()
    {
        var areas = _window.Screens.All.Select(s => s.WorkingArea).ToArray();
        if (areas.Length == 0) return;
        var x = areas.Min(a => a.X); var y = areas.Min(a => a.Y);
        _motion.SetDesktop(x, y, areas.Max(a => a.Right) - x, areas.Max(a => a.Bottom) - y);
    }

    private void DisplaysChanged(object? sender, EventArgs e)
    {
        RefreshDesktop();
        _motion.Drop();
        _motion.Rehome();
        Place();
    }

    private PixelPoint Pointer(PointerEventArgs e) => _window.PointToScreen(e.GetPosition(_window));
    private void Press(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed) return;
        _motion.Resync(_window.Position.X, _window.Position.Y,
            _window.Bounds.Width * _window.RenderScaling, _window.Bounds.Height * _window.RenderScaling, rehome: true);
        var p = Pointer(e);
        _motion.Press(p.X, p.Y, _clock.Elapsed.TotalMilliseconds);
        e.Pointer.Capture(_window);
    }

    private void Move(object? sender, PointerEventArgs e)
    {
        if (!_motion.Held) return;
        var p = Pointer(e);
        _motion.Move(p.X, p.Y, _clock.Elapsed.TotalMilliseconds);
        _motion.Step(0.001);
        Place();
    }

    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (!_motion.Held || e.InitialPressMouseButton != MouseButton.Left) return;
        _motion.Release(_clock.Elapsed.TotalMilliseconds, CoreSettings.Current.MotionLevel);
        e.Pointer.Capture(null);
        Place();
    }

    internal void Tick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        MotionSpeed = CoreSettings.Current.MandatoryVideoFloatingSpeedPercent / 100.0;
        _motion.Resize(_window.Bounds.Width * _window.RenderScaling, _window.Bounds.Height * _window.RenderScaling);
        if (!_motion.Active)
            _motion.Resync(_window.Position.X, _window.Position.Y, _motion.W, _motion.H, rehome: true);
        _motion.Step(Math.Clamp(now - _last, 0, 0.05));
        _last = now;
        Place();
    }

    private void Place() => _window.Position = new PixelPoint((int)Math.Round(_motion.X), (int)Math.Round(_motion.Y));
}
