using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

public partial class EmiDeskWindow
{
    internal PixelRect[]? InputRects { get; private set; }
    // Instance seam: tests observe native requests without an X server.
    internal Action<PixelRect[]?>? InputRegionWriter { get; set; }

    // WPF EmiDeskWindow.xaml:49-70 transforms the hit-testable BodyRoot, including its controls.
    // Observe the same transform slots as the renderer, including cancellation/reset writes.
    private void ApplyInputRegion()
    {
        if (_closingForGood || !IsVisible) return;
        try
        {
            InputRects = _bubbleCanvas.IsHitTestVisible ? null : BodyInputRects();
            if (InputRegionWriter is { } write) write(InputRects);
            else if (X11Overlay.BackendOf(TryGetPlatformHandle()) == OverlayBackend.X11)
            {
                if (InputRects is null) X11Overlay.SetInputRect(this, null);
                else X11Overlay.SetInputRects(this, InputRects, InputRects.Length);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] input region failed"); }
    }

    private PixelRect[] BodyInputRects()
    {
        var matrix = _bodyRoot.TransformToVisual(this);
        if (matrix is not { } transform || !transform.HasInverse) return Array.Empty<PixelRect>();
        var size = _bodyRoot.Bounds.Size;
        double s = RenderScaling > 0 ? RenderScaling : 1;
        var corners = new[] { new Point(), new Point(size.Width, 0),
            new Point(size.Width, size.Height), new Point(0, size.Height) };
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        for (int i = 0; i < corners.Length; i++)
        {
            var p = transform.Transform(corners[i]);
            corners[i] = new Point(p.X * s, p.Y * s);
            minY = Math.Min(minY, corners[i].Y); maxY = Math.Max(maxY, corners[i].Y);
        }
        int width = (int)Math.Ceiling(ClientSize.Width * s);
        int bottom = Math.Min((int)Math.Ceiling(ClientSize.Height * s), (int)Math.Ceiling(maxY));
        var rects = new List<PixelRect>();
        // Cover each pixel row intersecting the transformed quad. Bounding-box corners stay empty.
        // Round outwards so a control at a fractional pixel edge is still reachable.
        for (int y = Math.Max(0, (int)Math.Floor(minY)); y < bottom; y++)
        {
            double left = double.PositiveInfinity, right = double.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i]; var b = corners[(i + 1) % 4];
                if (a.Y > b.Y) (a, b) = (b, a);
                if (b.Y < y || a.Y > y + 1) continue;
                double x0, x1;
                if (b.Y == a.Y) { x0 = a.X; x1 = b.X; }
                else
                {
                    double slope = (b.X - a.X) / (b.Y - a.Y);
                    x0 = a.X + (Math.Clamp((double)y, a.Y, b.Y) - a.Y) * slope;
                    x1 = a.X + (Math.Clamp(y + 1.0, a.Y, b.Y) - a.Y) * slope;
                }
                left = Math.Min(left, Math.Min(x0, x1)); right = Math.Max(right, Math.Max(x0, x1));
            }
            if (!double.IsFinite(left) || !double.IsFinite(right)) continue;
            int x = Math.Max(0, (int)Math.Floor(left)), end = Math.Min(width, (int)Math.Ceiling(right));
            if (end <= x) continue;
            if (rects.Count > 0 && rects[^1] is var last && last.X == x && last.Width == end - x && last.Bottom == y)
                rects[^1] = new PixelRect(x, last.Y, last.Width, last.Height + 1);
            else rects.Add(new PixelRect(x, y, end - x, 1));
        }
        return rects.ToArray();
    }
}
