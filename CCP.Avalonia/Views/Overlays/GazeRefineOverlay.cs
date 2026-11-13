using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF GazeRefineOverlay.cs:73: a click-through magnification of the candidate constellation.
internal sealed class GazeRefineOverlay : Window
{
    private readonly List<Rect> _chips = new();
    private readonly List<Path> _rings = new();
    private readonly List<Control> _bodies = new();
    internal PixelPoint DesiredPosition { get; private set; }
    internal Rect EscapeBounds { get; private set; }
    internal Rect ChipBounds(int index) => _chips[index];

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    // The supplied work area and source bounds are desktop DIPs. Position is physical pixels.
    internal GazeRefineOverlay(IReadOnlyList<object> choices, IReadOnlyList<Rect> bounds, Rect work, double scale)
    {
        SystemDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; CanResize = false;
        Focusable = false; IsHitTestVisible = false; WindowStartupLocation = WindowStartupLocation.Manual;
        var centers = new Point[bounds.Count];
        double cx = 0, cy = 0;
        for (int i = 0; i < bounds.Count; i++) { cx += bounds[i].Center.X; cy += bounds[i].Center.Y; }
        cx /= bounds.Count; cy /= bounds.Count;
        double min = double.MaxValue;
        for (int i = 0; i < bounds.Count; i++)
            for (int j = i + 1; j < bounds.Count; j++)
                min = Math.Min(min, Distance(bounds[i].Center, bounds[j].Center));
        double zoom = min < 4 ? 20 : Math.Clamp(210 / min, 1.6, 20);
        var radii = new double[centers.Length];
        for (int i = 0; i < centers.Length; i++)
        {
            radii[i] = Math.Clamp(Math.Max(bounds[i].Width, bounds[i].Height) * zoom, 150, 220) / 2;
            var d = bounds[i].Center - new Point(cx, cy);
            centers[i] = new Point(cx + d.X * zoom, cy + d.Y * zoom);
            if (min < 4) centers[i] = new Point(cx + 145 * Math.Cos(2 * Math.PI * i / centers.Length), cy + 145 * Math.Sin(2 * Math.PI * i / centers.Length));
        }
        // 150 DIP chips plus 34 DIP hit slack must have disjoint catchments.
        for (int pass = 0; pass < 30; pass++)
            for (int i = 0; i < centers.Length; i++)
                for (int j = i + 1; j < centers.Length; j++)
                {
                    var delta = new Vector(centers[j].X - centers[i].X, centers[j].Y - centers[i].Y); var dist = delta.Length;
                    double separation = Math.Max(210, radii[i] + radii[j] + 68);
                    if (dist >= separation) continue;
                    if (dist < .01) { delta = new Vector(1, 0); dist = 1; }
                    var push = delta / dist * ((separation - dist) / 2 + .5);
                    centers[i] -= push; centers[j] += push;
                }
        void Fan()
        {
            double widest = 210; foreach (var r in radii) widest = Math.Max(widest, 2 * (r + 34));
            var radius = (widest + 1) / (2 * Math.Sin(Math.PI / centers.Length));
            for (int i = 0; i < centers.Length; i++) centers[i] = new Point(cx + radius * Math.Cos(2 * Math.PI * i / centers.Length), cy + radius * Math.Sin(2 * Math.PI * i / centers.Length));
        }
        bool overlap = false;
        for (int i = 0; i < centers.Length; i++)
            for (int j = i + 1; j < centers.Length; j++)
                if (Distance(centers[i], centers[j]) < Math.Max(210, radii[i] + radii[j] + 68)) overlap = true;
        if (overlap) Fan();
        double left = 0, top = 0, right = 0, bottom = 0;
        void Measure()
        {
            left = top = double.MaxValue; right = bottom = double.MinValue;
            for (int i = 0; i < centers.Length; i++) { var p = centers[i]; var r = radii[i] + 34; left = Math.Min(left, p.X - r); right = Math.Max(right, p.X + r); top = Math.Min(top, p.Y - r - 30); bottom = Math.Max(bottom, p.Y + r); }
        }
        Measure();
        if (right - left > work.Width || bottom - top > work.Height)
        {
            Fan();
            Measure();
        }
        if (right - left > work.Width || bottom - top > work.Height) throw new InvalidOperationException("Refine panel does not fit the monitor.");
        double dx = Math.Clamp(left, work.Left, work.Right - (right - left)) - left;
        double dy = Math.Clamp(top, work.Top, work.Bottom - (bottom - top)) - top;
        left += dx; top += dy; right += dx; bottom += dy;
        Width = right - left; Height = bottom - top;
        DesiredPosition = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
        EscapeBounds = new Rect(left - 140, top - 140, Width + 280, Height + 280);
        var canvas = new Canvas { Width = Width, Height = Height, IsHitTestVisible = false, Opacity = 0, Transitions = new global::Avalonia.Animation.Transitions { new global::Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(130) } } };
        canvas.Children.Add(new Border { Width = Width, Height = Height, CornerRadius = new CornerRadius(22), Background = new SolidColorBrush(Color.FromArgb(238, 26, 26, 46)), BorderBrush = Brushes.HotPink, BorderThickness = new Thickness(2) });
        var caption = new TextBlock { Text = Loc.Get("gaze_refine_prompt"), Foreground = Brushes.White, Width = Width, FontSize = 14, TextAlignment = TextAlignment.Center };
        Canvas.SetTop(caption, 10); canvas.Children.Add(caption);
        for (int i = 0; i < centers.Length; i++)
        {
            var p = centers[i] + new Vector(dx, dy);
            double r = radii[i], size = r * 2;
            _chips.Add(new Rect(p.X - r, p.Y - r, size, size));
            Control body;
            if (choices[i] is AmbientBubble)
                body = new Ellipse { Width = size, Height = size, Stroke = new SolidColorBrush(Color.FromArgb(221, 255, 255, 255)), StrokeThickness = 1.5, Fill = new RadialGradientBrush { Center = new RelativePoint(.45, .42, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.35, .3, RelativeUnit.Relative), GradientStops = { new(Color.FromArgb(85, 255, 255, 255), 0), new(Color.FromArgb(170, 255, 105, 180), .62), new(Color.FromArgb(102, 180, 105, 255), 1) } } };
            else
            {
                IBrush fill = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new(Color.FromRgb(37, 37, 66), 0), new(Color.FromRgb(58, 42, 85), 1) } };
                if (choices[i] is FlashOverlayWindow { GazePreview: { } preview }) fill = new ImageBrush { Source = preview, Stretch = Stretch.UniformToFill };
                var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(14), Background = fill, BorderBrush = Brushes.HotPink, BorderThickness = new Thickness(2) };
                if (choices[i] is not FlashOverlayWindow) tile.Child = new Polygon { Points = { new(size * .5, size * .28), new(size * .72, size * .5), new(size * .5, size * .72), new(size * .28, size * .5) }, Fill = Brushes.HotPink };
                body = tile;
            }
            Canvas.SetLeft(body, p.X - left - r); Canvas.SetTop(body, p.Y - top - r); canvas.Children.Add(body); _bodies.Add(body);
            var ring = new Path { Stroke = Brushes.HotPink, StrokeThickness = 6, StrokeLineCap = PenLineCap.Round };
            canvas.Children.Add(ring); _rings.Add(ring);
        }
        Content = canvas;
        Opened += (_, _) => canvas.Opacity = 1;
    }

    internal int HitTestChip(Point p)
    {
        for (int i = 0; i < _chips.Count; i++)
            if (Distance(p, _chips[i].Center) <= _chips[i].Width / 2 + 34) return i;
        return -1;
    }
    internal void Progress(int index, double value)
    {
        for (int i = 0; i < _rings.Count; i++)
        {
            double t = i == index ? Math.Clamp(value, 0, .999) : 0;
            _bodies[i].RenderTransformOrigin = RelativePoint.Center; _bodies[i].RenderTransform = new ScaleTransform(1 + t * .08, 1 + t * .08);
            if (t <= .001) { _rings[i].Data = null; continue; }
            var b = _chips[i]; double r = b.Width / 2 + 12, x = Canvas.GetLeft(_bodies[i]) + b.Width / 2, y = Canvas.GetTop(_bodies[i]) + b.Height / 2, a = t * 2 * Math.PI;
            var fig = new PathFigure { StartPoint = new Point(x, y - r), IsClosed = false, IsFilled = false };
            fig.Segments!.Add(new ArcSegment { Point = new Point(x + Math.Sin(a) * r, y - Math.Cos(a) * r), Size = new Size(r, r), IsLargeArc = t > .5, SweepDirection = SweepDirection.Clockwise });
            _rings[i].Data = new PathGeometry { Figures = new PathFigures { fig } };
        }
    }

    internal static GazeRefineOverlay? Open(IReadOnlyList<object> choices, IReadOnlyList<Rect> bounds)
    {
        var mb = WebcamTracker.Instance.Calibration?.MonitorBounds;
        var screen = WebcamScreens.Resolve(WebcamScreens.Enumerate(), mb?.DeviceName);
        if (screen == null || !X11Overlay.IsAvailable) return null;
        double k = screen.Scaling > 0 ? screen.Scaling : 1;
        var w = screen.WorkingArea;
        GazeRefineOverlay? panel = null;
        try
        {
            panel = new GazeRefineOverlay(choices, bounds, new Rect(w.X / k, w.Y / k, w.Width / k, w.Height / k), k);
            if (!X11Overlay.SetOverrideRedirect(panel, new PixelRect(panel.DesiredPosition, new PixelSize((int)Math.Ceiling(panel.Width * k), (int)Math.Ceiling(panel.Height * k))), passive: true) || !X11Overlay.SetClickThrough(panel, true)) { panel.Close(); return null; }
            panel.Position = panel.DesiredPosition;
            panel.Show();
            if (panel.ActualTransparencyLevel != WindowTransparencyLevel.None) return panel;
        }
        catch { }
        panel?.Close();
        return null;
    }
}
