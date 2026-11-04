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
    private readonly List<Ellipse> _rings = new();
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
        for (int i = 0; i < centers.Length; i++)
        {
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
                    if (dist >= 218) continue;
                    if (dist < .01) { delta = new Vector(1, 0); dist = 1; }
                    var push = delta / dist * ((218 - dist) / 2 + .5);
                    centers[i] -= push; centers[j] += push;
                }
        void Fan()
        {
            var radius = 110 / Math.Sin(Math.PI / centers.Length);
            for (int i = 0; i < centers.Length; i++) centers[i] = new Point(cx + radius * Math.Cos(2 * Math.PI * i / centers.Length), cy + radius * Math.Sin(2 * Math.PI * i / centers.Length));
        }
        bool overlap = false;
        for (int i = 0; i < centers.Length; i++)
            for (int j = i + 1; j < centers.Length; j++)
                if (Distance(centers[i], centers[j]) < 218) overlap = true;
        if (overlap) Fan();
        double left = 0, top = 0, right = 0, bottom = 0;
        void Measure()
        {
            left = top = double.MaxValue; right = bottom = double.MinValue;
            foreach (var p in centers) { left = Math.Min(left, p.X - 109); right = Math.Max(right, p.X + 109); top = Math.Min(top, p.Y - 139); bottom = Math.Max(bottom, p.Y + 109); }
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
        Position = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
        EscapeBounds = new Rect(left - 140, top - 140, Width + 280, Height + 280);
        var canvas = new Canvas { Width = Width, Height = Height, IsHitTestVisible = false };
        canvas.Children.Add(new Border { Width = Width, Height = Height, CornerRadius = new CornerRadius(22), Background = new SolidColorBrush(Color.FromArgb(238, 26, 26, 46)), BorderBrush = Brushes.HotPink, BorderThickness = new Thickness(2) });
        var caption = new TextBlock { Text = Loc.Get("gaze_refine_prompt"), Foreground = Brushes.White, Width = Width, FontSize = 14, TextAlignment = TextAlignment.Center };
        Canvas.SetTop(caption, 10); canvas.Children.Add(caption);
        for (int i = 0; i < centers.Length; i++)
        {
            var p = centers[i] + new Vector(dx, dy);
            _chips.Add(new Rect(p.X - 75, p.Y - 75, 150, 150));
            IBrush fill = choices[i] is AmbientBubble ? Brushes.HotPink : Brushes.DarkSlateBlue;
            if (choices[i] is FlashOverlayWindow { GazePreview: { } preview }) fill = new ImageBrush { Source = preview, Stretch = Stretch.UniformToFill };
            var body = new Ellipse { Width = 150, Height = 150, Fill = fill, Stroke = Brushes.White, StrokeThickness = 2 };
            Canvas.SetLeft(body, p.X - left - 75); Canvas.SetTop(body, p.Y - top - 75); canvas.Children.Add(body);
            var ring = new Ellipse { Width = 158, Height = 158, Stroke = Brushes.HotPink, StrokeThickness = 6, Opacity = 0 };
            Canvas.SetLeft(ring, p.X - left - 79); Canvas.SetTop(ring, p.Y - top - 79); canvas.Children.Add(ring); _rings.Add(ring);
        }
        Content = canvas;
    }

    internal int HitTestChip(Point p)
    {
        for (int i = 0; i < _chips.Count; i++)
            if (Distance(p, _chips[i].Center) <= 109) return i;
        return -1;
    }
    internal void Progress(int index, double value)
    {
        for (int i = 0; i < _rings.Count; i++) _rings[i].Opacity = i == index ? Math.Clamp(value, 0, 1) : 0;
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
            panel.Show();
            if (panel.ActualTransparencyLevel != WindowTransparencyLevel.None && X11Overlay.SetClickThrough(panel, true)) return panel;
        }
        catch { }
        panel?.Close();
        return null;
    }
}
