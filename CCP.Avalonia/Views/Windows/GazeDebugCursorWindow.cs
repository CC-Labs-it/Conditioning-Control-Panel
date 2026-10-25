using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

// WPF GazeDebugCursorService: transient debug visualization, never persisted.
// ponytail: reuse the calibration's Core-backed comet instead of a second Skia renderer.
internal sealed class GazeDebugCursorWindow : Window
{
    internal static GazeDebugCursorWindow? Active { get; private set; }
    internal static Func<Window, bool> MakeClickThrough = w =>
        X11Overlay.SetClickThrough(w, true) && w.ActualTransparencyLevel != WindowTransparencyLevel.None;
    private readonly Canvas _canvas = new();
    private readonly GazeCursorTrailControl _trail = new();
    private readonly Ellipse _dot = new() { Width = 16, Height = 16, Fill = Brushes.HotPink };
    private readonly DispatcherTimer _timer;
    private Point? _position;
    private long _last;

    public GazeDebugCursorWindow()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = ShowActivated = CanResize = Focusable = IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _canvas;
        _canvas.Children.Add(_trail);
        _canvas.Children.Add(_dot);
        _dot.IsVisible = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) =>
        {
            _timer.Stop();
            var tracker = WebcamTracker.Instance;
            tracker.OnGazeMove -= Move;
            tracker.OnFaceLost -= Clear;
            tracker.StateChanged -= State;
            if (Active == this) Active = null;
        };
    }

    internal static bool ShowDebug()
    {
        if (Active != null) return true;
        var window = new GazeDebugCursorWindow();
        // One virtual-desktop stage, as WPF uses; gaze coordinates are physical pixels.
        PixelRect? bounds = null;
        foreach (var screen in window.Screens.All)
            bounds = bounds is { } b ? b.Union(screen.Bounds) : screen.Bounds;
        if (bounds is not { } stage) return false;
        window.Position = stage.Position;
        window.Width = stage.Width / window.DesktopScaling;
        window.Height = stage.Height / window.DesktopScaling;
        window._trail.Width = window.Width;
        window._trail.Height = window.Height;
        window.Show();
        if (!MakeClickThrough(window)) { window.Close(); return false; }
        Active = window;
        var tracker = WebcamTracker.Instance;
        tracker.OnGazeMove += window.Move;
        tracker.OnFaceLost += window.Clear;
        tracker.StateChanged += window.State;
        return true;
    }

    internal static void CloseActive() => Active?.Close();
    internal bool DotVisible => _dot.IsVisible;
    internal void Move(Point point)
    {
        if (!WebcamTracker.Instance.IsRunning || WebcamTracker.Instance.Calibration == null) return;
        _position = new Point((point.X - Position.X) / DesktopScaling, (point.Y - Position.Y) / DesktopScaling);
        _dot.IsVisible = true;
        Canvas.SetLeft(_dot, _position.Value.X - 8);
        Canvas.SetTop(_dot, _position.Value.Y - 8);
        if (!_timer.IsEnabled) { _last = Environment.TickCount64; _timer.Start(); }
    }
    internal void Clear()
    {
        _position = null;
        _dot.IsVisible = false;
        _timer.Stop();
        _trail.History.Clear();
        _trail.InvalidateVisual();
    }
    private void State()
    {
        if (!WebcamStatus.IsLive(WebcamTracker.Instance.State) || WebcamTracker.Instance.Calibration == null) Clear();
    }
    private void Tick()
    {
        long now = Environment.TickCount64;
        _trail.History.Tick((now - _last) / 1000.0, _position is { } p ? (p.X, p.Y) : null);
        _last = now;
        _trail.InvalidateVisual();
    }
}
