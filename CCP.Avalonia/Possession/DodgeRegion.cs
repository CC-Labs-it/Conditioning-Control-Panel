// WPF Services/Possession/Effects/DodgeEffect.cs:132-160, 195-245, 401-452.
// Geometry only. The pointer-driven effect and its lifetime are not wired yet.
using System;
using Avalonia;

namespace ConditioningControlPanel.Avalonia.Possession;

internal static class DodgeRegion
{
    internal const double EdgeMarginPx = 12;
    private const double Epsilon = 0.5;
    private const double MinStepPx = 6;

    internal static bool IsStrip(Rect clip, double width, double height) =>
        !Empty(clip) && ((width > 0 && clip.Width < width * 0.5) ||
                         (height > 0 && clip.Height < height * 0.5));

    internal static bool Contains(Rect region, Rect rect) =>
        !Empty(region) && !Empty(rect) && rect.X >= region.X - Epsilon &&
        rect.Y >= region.Y - Epsilon && rect.Right <= region.Right + Epsilon &&
        rect.Bottom <= region.Bottom + Epsilon;

    internal static bool TryOffsetRange(Rect home, Rect region, out double minX, out double maxX,
        out double minY, out double maxY)
    {
        minX = maxX = minY = maxY = 0;
        if (Empty(home) || Empty(region) || region.Width + Epsilon < home.Width ||
            region.Height + Epsilon < home.Height) return false;
        minX = region.X - home.X;
        maxX = Math.Max(minX, region.Right - home.Right);
        minY = region.Y - home.Y;
        maxY = Math.Max(minY, region.Bottom - home.Bottom);
        return true;
    }

    // Distances are rolled by the caller, including WPF's photosafe amplitude adjustment.
    internal static bool TryStep(Rect home, Rect region, Vector offset, Point cursor, Vector velocity,
        double sidewaysDistance, double awayDistance, out Vector target)
    {
        target = offset;
        if (!TryOffsetRange(home, region, out var minX, out var maxX, out var minY, out var maxY)) return false;
        var current = new Rect(home.X + offset.X, home.Y + offset.Y, home.Width, home.Height);
        bool predicted = velocity.Length >= 220 && current.Contains(cursor + velocity * 0.3);
        if (!predicted && !current.Inflate(24).Contains(cursor)) return false;
        double x = offset.X, y = offset.Y;
        if (predicted)
        {
            var dir = velocity / velocity.Length;
            var perp = new Vector(-dir.Y, dir.X);
            double ax = Math.Clamp(offset.X + perp.X * sidewaysDistance, minX, maxX);
            double ay = Math.Clamp(offset.Y + perp.Y * sidewaysDistance, minY, maxY);
            double bx = Math.Clamp(offset.X - perp.X * sidewaysDistance, minX, maxX);
            double by = Math.Clamp(offset.Y - perp.Y * sidewaysDistance, minY, maxY);
            double a = Math.Abs(ax - offset.X) + Math.Abs(ay - offset.Y);
            double b = Math.Abs(bx - offset.X) + Math.Abs(by - offset.Y);
            if (a >= b) { x = ax; y = ay; } else { x = bx; y = by; }
            if (a < MinStepPx && b < MinStepPx)
            {
                x = Math.Clamp(offset.X - dir.X * sidewaysDistance, minX, maxX);
                y = Math.Clamp(offset.Y - dir.Y * sidewaysDistance, minY, maxY);
            }
        }
        if (Math.Abs(x - offset.X) < MinStepPx && Math.Abs(y - offset.Y) < MinStepPx)
        {
            y = Math.Clamp(offset.Y, minY, maxY);
            double away = cursor.X < home.X + offset.X + home.Width / 2 ? 1 : -1;
            x = Math.Clamp(offset.X + away * awayDistance, minX, maxX);
            if (Math.Abs(x - offset.X) < MinStepPx)
                x = Math.Clamp(offset.X - away * awayDistance, minX, maxX);
        }
        if (Math.Abs(x - offset.X) < 2 && Math.Abs(y - offset.Y) < 2) return false;
        if (!Contains(region, new Rect(home.X + x, home.Y + y, home.Width, home.Height))) return false;
        target = new Vector(x, y);
        return true;
    }

    private static bool Empty(Rect rect) => rect.Width <= 0 || rect.Height <= 0;
}
