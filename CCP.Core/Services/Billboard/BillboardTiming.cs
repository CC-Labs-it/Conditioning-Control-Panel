using System;

namespace ConditioningControlPanel.Services.Billboard;

/// <summary>WPF CubicBezierEase.Thud, without UI types. The y handles may overshoot.</summary>
public static class BillboardTiming
{
    public static double Thud(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var c = DashboardBillboard.ThudCurve;
        double lo = 0, hi = 1;
        for (int i = 0; i < 40; i++)
        {
            double t = (lo + hi) / 2;
            if (Bezier(t, c.X1, c.X2) < x) lo = t;
            else hi = t;
        }
        return Bezier((lo + hi) / 2, c.Y1, c.Y2);
    }

    private static double Bezier(double t, double p1, double p2) =>
        3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
}
