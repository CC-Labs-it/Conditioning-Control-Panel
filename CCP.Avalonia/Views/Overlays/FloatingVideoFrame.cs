using System;
using System.Diagnostics;
using System.ComponentModel;
using Avalonia.Threading;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Video.Floating;
namespace ConditioningControlPanel.Avalonia.Views.Overlays;
internal sealed class FloatingVideoFrame
{
    private readonly Window _window;
    private readonly FloatingVideoMotionModel _motion;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly BeatLoop _beat;
    private readonly Func<double> _nowMs;
    private double _last, _lit, _breathSeconds;
    private bool _audioLit, _closed;
    internal bool Running => _beat.IsRunning;
    internal double BreathSeconds => _breathSeconds;
    private readonly Border? _frame;
    private readonly Func<bool> _audible;
    internal double MotionSpeed { get => _motion.Speed; set => _motion.Speed = value; }
    internal FloatingVideoFrame(Window window, Func<double>? nowMs = null, Border? frame = null, Func<bool>? audible = null)
    {
        _window = window; _frame = frame;
        _audible = audible ?? (() => false);
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
        window.PointerPressed += Press; window.PointerMoved += Move; window.PointerReleased += Release;
        window.PointerCaptureLost += (_, _) => { if (_motion.Held) _motion.Drop(); };
        window.Screens.Changed += DisplaysChanged;
        var settings = CoreSettings.Current;
        PropertyChangedEventHandler changed = (_, e) => { if (e.PropertyName is nameof(CoreSettings.Current.MotionLevel) or nameof(CoreSettings.Current.MandatoryVideoFloatingSpeedPercent)) Dispatcher.UIThread.Post(Refresh); };
        void Rebind() => Dispatcher.UIThread.Post(() => { if (_closed) return; settings.PropertyChanged -= changed; settings = CoreSettings.Current; settings.PropertyChanged += changed; Refresh(); });
        settings.PropertyChanged += changed;
        var service = CoreSettings.Service;
        if (service != null) service.CurrentReplaced += Rebind;
        window.Closed += (_, _) => { _closed = true; _beat.Stop(); _motion.Drop(); window.Screens.Changed -= DisplaysChanged; settings.PropertyChanged -= changed; if (service != null) service.CurrentReplaced -= Rebind; };
        RefreshDesktop(); Refresh();
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
        _motion.Drop(); _motion.Rehome();
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
        e.Pointer.Capture(_window);
        Refresh();
    }
    private void Move(object? sender, PointerEventArgs e)
    {
        if (!_motion.Held) return;
        var p = Pointer(e);
        _motion.Move(p.X, p.Y, _nowMs());
        _motion.Step(0.001);
        Place();
    }
    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        if (!_motion.Held || e.InitialPressMouseButton != MouseButton.Left) return;
        var p = Pointer(e);
        var now = _nowMs();
        _motion.Move(p.X, p.Y, now);
        _motion.Release(now, CoreSettings.Current.MotionLevel);
        e.Pointer.Capture(null);
        Place();
    }
    internal void Refresh()
    {
        if (_closed) return;
        var audible = _audible();
        if (audible && !_audioLit) _breathSeconds = 0;
        _audioLit = audible;
        if (!_beat.IsRunning) _last = _nowMs() / 1000.0;
        if (NeedsClock) _beat.Start(); else Tick();
    }
    private bool NeedsClock => _motion.Active || (_audioLit ? _lit < 1 : _lit > 0)
        || (_audioLit && CoreSettings.Current.MotionLevel != ConditioningControlPanel.Models.MotionLevel.Off);
    internal void Tick()
    {
        var now = _nowMs() / 1000.0;
        var dt = Math.Clamp(now - _last, 0, 0.05);
        _lit = FloatingFrameLook.Approach(_lit, _audioLit, dt);
        if (_audioLit) _breathSeconds += dt;
        if (_frame != null)
        {
            var (r, g, b) = FloatingFrameLook.Border(FloatingFrameLook.AccentR, FloatingFrameLook.AccentG,
                FloatingFrameLook.AccentB, _lit, FloatingFrameLook.Breath(_breathSeconds, CoreSettings.Current.MotionLevel));
            var color = Color.FromRgb(r, g, b);
            if (_frame.BorderBrush is not SolidColorBrush brush || brush.Color != color) _frame.BorderBrush = new SolidColorBrush(color);
        }
        MotionSpeed = CoreSettings.Current.MandatoryVideoFloatingSpeedPercent / 100.0;
        _motion.Resize(_window.Bounds.Width * _window.RenderScaling, _window.Bounds.Height * _window.RenderScaling);
        if (!_motion.Active)
            _motion.Resync(_window.Position.X, _window.Position.Y, _motion.W, _motion.H, rehome: true);
        _motion.Step(Math.Clamp(now - _last, 0, 0.05));
        _last = now;
        Place();
        if (!NeedsClock) _beat.Stop();
    }
    private void Place() => _window.Position = new PixelPoint((int)Math.Round(_motion.X), (int)Math.Round(_motion.Y));
}
