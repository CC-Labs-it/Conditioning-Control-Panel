using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>WPF calibration bubble geometry and elliptical rolling-mean dwell, shared by both heads.</summary>
public sealed class CalibrationBubbleProbe
{
    public const double RadiusX = 130, RadiusY = 195, HeaderClear = 108, OuterFraction = 0.35;
    public const int TickMs = 33, DwellNeededMs = 700, TimeoutMs = 10000;
    private readonly Queue<(double X, double Y)> _mean = new();
    private readonly double _x, _y;
    private double _dwell;
    public CalibrationBubbleProbe(double x, double y) { _x = x; _y = y; }
    public bool OnTarget { get; private set; }
    public bool Complete => _dwell >= DwellNeededMs;
    public double Progress => Math.Clamp(_dwell / DwellNeededMs, 0, 1);

    /// <summary>One 33 ms tick. Stale gaze decays dwell; a wobble does not discard the rolling mean.</summary>
    public void Tick(bool fresh, double x, double y)
    {
        if (fresh)
        {
            _mean.Enqueue((x, y));
            while (_mean.Count > 12) _mean.Dequeue();
        }
        OnTarget = false;
        if (fresh && _mean.Count >= 3)
        {
            double dx = (_mean.Average(p => p.X) - _x) / RadiusX;
            double dy = (_mean.Average(p => p.Y) - _y) / RadiusY;
            OnTarget = Math.Sqrt(dx * dx + dy * dy) <= 1;
        }
        _dwell = OnTarget ? _dwell + TickMs : Math.Max(0, _dwell - TickMs);
    }

    public sealed record Layout((double X, double Y)[] Points, double Center, double Scale, bool Degenerate);

    /// <summary>Two mirror pairs about one centre preserve equal column means even on short displays.</summary>
    public static Layout BuildLayout(double w, double h, double ringHalfHeight)
    {
        double rowMin = HeaderClear + ringHalfHeight, rowMax = h - ringHalfHeight - 16;
        double center, scale;
        bool degenerate = rowMin > rowMax;
        if (degenerate) { center = h / 2; scale = 0; }
        else
        {
            double maxScale = (rowMax - rowMin) / (2 * OuterFraction * h);
            if (maxScale >= 1)
            {
                scale = 1;
                double lo = rowMin + OuterFraction * h, hi = rowMax - OuterFraction * h;
                center = Math.Clamp(h / 2, lo, Math.Max(lo, hi));
            }
            else { scale = maxScale; center = (rowMin + rowMax) / 2; }
        }
        double outer = OuterFraction * h * scale, inner = 0.12 * h * scale;
        return new(new[] { (w * 0.24, center - outer), (w * 0.76, center + inner),
            (w * 0.24, center + outer), (w * 0.76, center - inner) }, center, scale, degenerate);
    }

    public static double Distance((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    /// <summary>WPF MedianPoint: upper median on each axis, including an even sample count.</summary>
    public static (double X, double Y) Median(IReadOnlyList<(double X, double Y)> points) =>
        (points.Select(p => p.X).OrderBy(x => x).ElementAt(points.Count / 2),
         points.Select(p => p.Y).OrderBy(y => y).ElementAt(points.Count / 2));
}
