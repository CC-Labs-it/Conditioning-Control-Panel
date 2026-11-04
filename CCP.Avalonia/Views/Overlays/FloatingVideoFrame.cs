using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Video.Floating;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// Window coordinates and pointer samples are device pixels; content sizes are DIPs.
internal sealed class FloatingVideoFrame
{
    private readonly Window _window;
    private readonly FloatingVideoMotion _motion;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly BeatLoop _beat;
    private readonly Func<double> _nowMs;
    private double _last;
    private Func<(PixelPoint, bool)>? _readPointer = OperatingSystem.IsWindows() ? ReadPointer : null;
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private static (PixelPoint, bool) ReadPointer() => GetCursorPos(out var p)
        ? (new PixelPoint(p.X, p.Y), GetAsyncKeyState(1) < 0) : (default, false);
    internal double MotionSpeed { get => _motion.Speed; set => _motion.Speed = value; }

    internal FloatingVideoFrame(Window window, Func<double>? nowMs = null)
    {
        _window = window;
        _nowMs = nowMs ?? (() => _clock.Elapsed.TotalMilliseconds);
        var b = window.Screens.ScreenFromWindow(window)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        _motion = new(window.Position.X, window.Position.Y, window.Width * window.RenderScaling,
            window.Height * window.RenderScaling, b.X, b.Y, b.Width, b.Height);
        MotionSpeed = CoreSettings.Current.MandatoryVideoFloatingSpeedPercent / 100.0;
        _motion.MonitorAt = (x, y) =>
        {
            var area = window.Screens.All.OrderBy(s => Distance(s.WorkingArea, x, y)).FirstOrDefault()?.WorkingArea;
            return area is { } a ? (a.X, a.Y, a.Width, a.Height) : null;
        };
        _beat = new BeatLoop(window, _ => Tick());
        window.Opened += (_, _) =>
        {
            if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } h)
                Platform.Win32Overlay.SetOverrideRedirect(window, h.Handle, passive: true);
        };
        window.PointerPressed += Press;
        window.PointerMoved += Move;
        window.PointerReleased += Release;
        window.PointerCaptureLost += (_, _) => { if (_readPointer is null && _motion.Held) { _motion.Drop(); Place(); _beat.Stop(); } };
        window.Screens.Changed += DisplaysChanged;
        window.SizeChanged += (_, _) => SyncSize();
        window.Closed += (_, _) => { _beat.Stop(); _motion.Drop(); window.Screens.Changed -= DisplaysChanged; };
        RefreshDesktop();
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
        _beat.Stop();
        RefreshDesktop();
        _motion.Drop();
        SyncSize();
    }

    private void SyncSize()
    {
        _motion.Resize(_window.Bounds.Width * _window.RenderScaling, _window.Bounds.Height * _window.RenderScaling);
        if (!_motion.Active)
            _motion.Resync(_window.Position.X, _window.Position.Y, _motion.W, _motion.H, rehome: true);
        Place();
    }

    private PixelPoint Pointer(PointerEventArgs e) => _window.PointToScreen(e.GetPosition(_window));
    private void Press(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_window).Properties.IsLeftButtonPressed) return;
        _motion.Resync(_window.Position.X, _window.Position.Y,
            _window.Bounds.Width * _window.RenderScaling, _window.Bounds.Height * _window.RenderScaling, rehome: true);
        var p = Pointer(e);
        _motion.Press(p.X, p.Y, _nowMs());
        if (_readPointer is null) e.Pointer.Capture(_window);
        _last = _nowMs() / 1000.0;
        _beat.Start();
    }

    private void Move(object? sender, PointerEventArgs e)
    {
        if (_readPointer is not null || !_motion.Held) return;
        var p = Pointer(e);
        _motion.Move(p.X, p.Y, _nowMs());
        _motion.Step(0.001);
        Place();
    }

    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (_readPointer is not null || !_motion.Held || e.InitialPressMouseButton != MouseButton.Left) return;
        var p = Pointer(e);
        var now = _nowMs();
        _motion.Move(p.X, p.Y, now);
        _motion.Release(now, AmbientFxCanvas.Env.Level);
        e.Pointer.Capture(null);
        Place();
        if (!_motion.Active) _beat.Stop();
    }

    internal void Tick()
    {
        MotionSpeed = CoreSettings.Current.MandatoryVideoFloatingSpeedPercent / 100.0;
        if (_motion.Held && _readPointer is { } read)
        {
            var (p, down) = read(); var time = _nowMs();
            _motion.Move(p.X, p.Y, time);
            if (!down) { _motion.Release(time, AmbientFxCanvas.Env.Level); Place(); }
        }
        if (!_motion.Active) { _beat.Stop(); return; }
        var now = _nowMs() / 1000.0;
        _motion.Resize(_window.Bounds.Width * _window.RenderScaling, _window.Bounds.Height * _window.RenderScaling);
        _motion.Step(Math.Clamp(now - _last, 0, 0.05));
        _last = now;
        Place();
        if (!_motion.Active) _beat.Stop();
    }

    private void Place()
    {
        var position = new PixelPoint((int)Math.Round(_motion.X), (int)Math.Round(_motion.Y));
        if (_window.Position != position) _window.Position = position;
    }
}
