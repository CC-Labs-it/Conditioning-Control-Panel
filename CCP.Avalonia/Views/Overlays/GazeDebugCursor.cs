using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF GazeDebugCursorService: a click-through pink dot, fading 0.55 s trail, face-loss hide.
// ponytail: this row has one requester; Focus Gaze's shared requester/gold lock tint is not ported.
// Draw with Avalonia primitives instead of mutable cross-thread Skia resources.
internal sealed class GazeDebugCursor : Window
{
    internal static GazeDebugCursor? Active { get; private set; }
    internal static Func<Window, bool> MakeClickThrough = w =>
        X11Overlay.SetClickThrough(w, true) && w.ActualTransparencyLevel != WindowTransparencyLevel.None;
    private readonly CursorDrawing _drawing = new();
    private readonly FrameClock _clock;
    private static WebcamTracker Tracker => WebcamTracker.Instance;

    internal GazeDebugCursor()
    {
        SystemDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = ShowActivated = CanResize = Focusable = IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 640; Height = 400;
        Content = _drawing;
        _clock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(33) };
        _clock.Tick += (_, _) => { _drawing.InvalidateVisual(); if (!_drawing.HasTrail) _clock.Stop(); };
        Closed += (_, _) =>
        {
            Tracker.OnGazeMove -= Move;
            Tracker.OnFaceLost -= FaceLost;
            Tracker.StateChanged -= StateChanged;
            _clock.Stop();
            if (Active == this) Active = null;
        };
    }

    internal static void ShowCursor()
    {
        if (Active != null) return;
        var w = new GazeDebugCursor();
        Active = w;
        w.Show();
        if (!MakeClickThrough(w)) { w.Close(); return; }
        Tracker.OnGazeMove += w.Move;
        Tracker.OnFaceLost += w.FaceLost;
        Tracker.StateChanged += w.StateChanged;
    }

    internal bool HasGaze => _drawing.HasGaze;
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
        _clock.Start();
    }

    private sealed class CursorDrawing : Control
    {
        private readonly List<(Point Point, long At)> _trail = new();
        private Point? _position;
        private static long Now => Environment.TickCount64;
        internal bool HasGaze => _position != null;
        internal bool HasTrail => _trail.Count > 0;
        private static readonly IBrush Pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
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
            if (_trail.Count == 0 || Math.Pow(_trail[^1].Point.X - point.X, 2) + Math.Pow(_trail[^1].Point.Y - point.Y, 2) >= 9)
                _trail.Add((point, Now));
            if (_trail.Count > 64) _trail.RemoveAt(0);
            InvalidateVisual();
        }
        internal void Clear() { _position = null; _trail.Clear(); InvalidateVisual(); }
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            _trail.RemoveAll(p => Now - p.At >= 550);
            for (int i = 1; i < _trail.Count; i++)
            {
                double life = Math.Clamp(1 - (Now - _trail[i].At) / 550.0, 0, 1);
                using (context.PushOpacity(life * life))
                {
                    context.DrawLine(new Pen(Pink, 7), _trail[i - 1].Point, _trail[i].Point);
                    context.DrawLine(new Pen(Brushes.White, 2), _trail[i - 1].Point, _trail[i].Point);
                }
            }
            if (_position is not { } p) return;
            context.DrawEllipse(Bloom, null, p, 26, 26);
            context.DrawEllipse(Pink, new Pen(Brushes.White, 1.6), p, 8, 8);
            context.DrawEllipse(Brushes.White, null, p, 3, 3);
        }
    }
}
