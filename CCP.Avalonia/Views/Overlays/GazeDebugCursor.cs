using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF Services/Tracking/GazeDebugCursorService.cs:159, 203, 270, 332, 423: overlay, feed, face loss, trail, dot.
// ponytail: this row has one requester; Focus Gaze's shared requester/gold lock tint is not ported.
// Draw with Avalonia primitives instead of mutable cross-thread Skia resources.
internal sealed class GazeDebugCursor : Window
{
    internal static GazeDebugCursor? Active { get; private set; }
    internal static Func<Window, bool> MakeClickThrough = w =>
        X11Overlay.SetClickThrough(w, true) && w.ActualTransparencyLevel != WindowTransparencyLevel.None;
    private readonly CursorDrawing _drawing = new();
    private long _lastTick;
    private readonly FrameClock _clock;
    private readonly FrameClock _stackingClock;
    private static WebcamTracker Tracker => WebcamTracker.Instance;

    internal GazeDebugCursor(global::ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource? stackingFrames = null, Action<Window>? raise = null)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = ShowActivated = CanResize = Focusable = IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 640; Height = 400;
        Content = _drawing;
        _clock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(33) };
        _clock.Tick += (_, _) => Advance();
        // WPF GazeDebugCursorService.cs:316-329: reassert even after the trail expires.
        _stackingClock = stackingFrames == null ? new FrameClock(this) : new FrameClock(stackingFrames);
        _stackingClock.Interval = TimeSpan.FromMilliseconds(200);
        _stackingClock.Tick += (_, _) =>
        {
            try { if (raise != null) raise(this); else X11Overlay.Raise(this); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Gaze cursor stacking failed"); }
        };
        Opened += (_, _) => _stackingClock.Start();
        Closed += (_, _) =>
        {
            Tracker.OnGazeMove -= Move;
            Tracker.OnFaceLost -= FaceLost;
            Tracker.StateChanged -= StateChanged;
            FaceLost();
            _stackingClock.Stop();
            if (Active == this) Active = null;
        };
    }

    internal static void ShowCursor()
    {
        if (Active != null) return;
        var w = new GazeDebugCursor();
        Active = w;
        try
        {
            w.Show();
            if (!MakeClickThrough(w)) { w.Close(); return; }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Gaze cursor overlay unavailable");
            w.Close();
            return;
        }
        Tracker.OnGazeMove += w.Move;
        Tracker.OnFaceLost += w.FaceLost;
        Tracker.StateChanged += w.StateChanged;
    }

    internal bool HasGaze => _drawing.HasGaze;
    internal bool ClockRunning => _clock.IsEnabled;
    internal int TrailCount => _drawing.History.Count;
    internal void Advance(double? seconds = null)
    {
        var now = Environment.TickCount64;
        _drawing.History.Tick(seconds ?? (now - _lastTick) / 1000.0, null);
        _lastTick = now;
        _drawing.InvalidateVisual();
        if (_drawing.History.Count == 0) _clock.Stop();
    }
    internal static void CloseActive() => Active?.Close();
    private void StateChanged() { if (!Tracker.IsRunning) FaceLost(); }
    private void FaceLost() { _drawing.Clear(); _clock.Stop(); }

    // Gaze samples are monitor-local DIPs. Position is physical; dimensions and drawing are DIPs.
    internal void Move(Point p)
    {
        if (Tracker.Calibration?.MonitorBounds is not { } mb || !double.IsFinite(p.X) || !double.IsFinite(p.Y)) return;
        var screen = Screens.All.FirstOrDefault(s => s.DisplayName == mb.DeviceName)
            ?? Screens.All.FirstOrDefault(s => s.Bounds.X == mb.X && s.Bounds.Y == mb.Y)
            ?? Screens.Primary;
        if (screen == null) return;
        if (Position != screen.Bounds.Position) _drawing.Clear();
        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / screen.Scaling;
        Height = screen.Bounds.Height / screen.Scaling;
        var calibrationScale = mb.DpiScale is > 0.25 and < 8 ? mb.DpiScale : 1;
        _drawing.Move(new Point(p.X * calibrationScale / screen.Scaling, p.Y * calibrationScale / screen.Scaling));
        if (!_clock.IsEnabled) _lastTick = Environment.TickCount64;
        _clock.Start();
    }

    private sealed class CursorDrawing : GazeCursorTrailControl
    {
        private Point? _position;
        internal bool HasGaze => _position != null;
        private static readonly IBrush Pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
        private static readonly IPen Rim = new Pen(Brushes.White, 1.6);
        private static readonly IBrush Bloom = new RadialGradientBrush
        {
            GradientStops = new GradientStops
            {
                new(Color.FromArgb(190, 0xFF, 0x69, 0xB4), 0),
                new(Colors.Transparent, 1),
            },
        };
        internal void Move(Point point)
        {
            _position = point;
            History.Tick(0, (point.X, point.Y));
            InvalidateVisual();
        }
        internal void Clear() { _position = null; History.Clear(); InvalidateVisual(); }
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_position is not { } p) return;
            context.DrawEllipse(Bloom, null, p, 26, 26);
            context.DrawEllipse(Pink, Rim, p, 8, 8);
            context.DrawEllipse(Brushes.White, null, p, 3, 3);
        }
    }
}
