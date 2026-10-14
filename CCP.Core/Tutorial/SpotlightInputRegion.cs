using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Tutorial;

/// <summary>WPF's physical window extent minus its spotlight rectangle, shared by both heads.</summary>
public static class SpotlightInputRegion
{
    public readonly record struct Rect(int X, int Y, int Width, int Height);

    public static Rect[] Create(double width, double height, double scaleX, double scaleY,
        double holeX, double holeY, double holeWidth, double holeHeight)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return Array.Empty<Rect>();
        scaleX = double.IsFinite(scaleX) && scaleX > 0 ? scaleX : 1;
        scaleY = double.IsFinite(scaleY) && scaleY > 0 ? scaleY : 1;
        int w = (int)Math.Round(width * scaleX), h = (int)Math.Round(height * scaleY);
        if (w <= 0 || h <= 0) return Array.Empty<Rect>();
        if (!double.IsFinite(holeX) || !double.IsFinite(holeY) || !double.IsFinite(holeWidth) ||
            !double.IsFinite(holeHeight) || holeWidth <= 0 || holeHeight <= 0)
            return new[] { new Rect(0, 0, w, h) };
        int l = Math.Clamp((int)Math.Round(holeX * scaleX), 0, w);
        int t = Math.Clamp((int)Math.Round(holeY * scaleY), 0, h);
        int r = Math.Clamp((int)Math.Round((holeX + holeWidth) * scaleX), 0, w);
        int b = Math.Clamp((int)Math.Round((holeY + holeHeight) * scaleY), 0, h);
        if (r <= l || b <= t) return new[] { new Rect(0, 0, w, h) };
        var rects = new List<Rect>(4);
        Add(0, 0, w, t);
        Add(0, b, w, h - b);
        Add(0, t, l, b - t);
        Add(r, t, w - r, b - t);
        return rects.ToArray();

        void Add(int x, int y, int rw, int rh)
        {
            if (rw > 0 && rh > 0) rects.Add(new Rect(x, y, rw, rh));
        }
    }
}
