using System;

namespace ConditioningControlPanel.Services.Billboard;

/// <summary>Shared WPF cubic Bezier timing. The y handles may overshoot.</summary>
public static class BillboardTiming
{
    public static double Thud(double x)
    {
        var c = DashboardBillboard.ThudCurve;
        return Ease(x, c.X1, c.Y1, c.X2, c.Y2);
    }

    // WPF CubicBezierEase: Newton first, then bisection for a near-zero slope.
    public static double Ease(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double t = x;
        for (int i = 0; i < 8; i++)
        {
            double err = Bezier(t, x1, x2) - x;
            if (Math.Abs(err) < 1e-6) return Bezier(t, y1, y2);
            double d = Slope(t, x1, x2);
            if (Math.Abs(d) < 1e-6) break;
            t -= err / d;
        }
        double lo = 0, hi = 1; t = x;
        for (int i = 0; i < 40; i++)
        {
            double v = Bezier(t, x1, x2);
            if (Math.Abs(v - x) < 1e-7) break;
            if (v < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return Bezier(t, y1, y2);
    }

    private static double Bezier(double t, double p1, double p2) =>
        3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
    private static double Slope(double t, double p1, double p2) =>
        3 * (1 - t) * (1 - t) * p1 + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (1 - p2);
}
