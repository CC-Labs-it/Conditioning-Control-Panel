using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>One rabbit's trail in screen pixels. WPF BubbleService.cs:3929-3946.</summary>
public sealed class ChaosRabbitTrails
{
    public readonly record struct Point(double X, double Y, DateTime Time);
    private readonly List<Point> _points = new();
    private double _lastX, _lastY;
    public IReadOnlyList<Point> Points => _points;

    /// <summary>Returns the emitted direction, or null when no point was recorded.</summary>
    public (double X, double Y)? Step(double x, double y, double dpiScale, double trailSec,
        bool sweeper, bool telegraphing, bool popping, bool skia, DateTime now)
    {
        double life = sweeper ? Math.Max(0.5, trailSec) : trailSec;
        if (life <= 0 || telegraphing || popping)
        {
            _points.Clear();
            return null;
        }
        double dx = x - _lastX, dy = y - _lastY;
        double gap = (skia ? 22 : 40) * dpiScale;
        (double X, double Y)? direction = null;
        if (dx * dx + dy * dy >= gap * gap)
        {
            _lastX = x; _lastY = y;
            _points.Add(new Point(x, y, now));
            direction = (dx, dy);
        }
        var cutoff = now.AddSeconds(-life);
        while (_points.Count > 0 && _points[0].Time < cutoff) _points.RemoveAt(0);
        if (_points.Count > 60) _points.RemoveAt(0);
        return direction;
    }
}
