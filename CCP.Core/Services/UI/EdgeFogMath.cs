using System;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>Window edge, ordered clockwise from the top (WPF AmbientFxCanvas.cs:58).</summary>
    public enum EdgeSide { Top, Right, Bottom, Left }

    /// <summary>Edge fog budget, geometry and opacity from WPF AmbientFxCanvas.EdgeFog.cs:19.</summary>
    public static class EdgeFogMath
    {
        public const double StripPx = 56;
        public const int BigLong = 5;
        public const double ReducedCount = 0.5, ReducedSpeed = 0.5;
        public const double LeanShare = 0.6;
        public const int FullBudget = 60;
        public const double FadeShare = 0.35, SmallAlphaMax = 0.22, InnerClearPx = 2;

        /// <summary>Zero budget means no puffs; a lean budget and Reduced motion narrow the count.</summary>
        public static int Target(int fullCount, int liveBudget, bool reduced)
        {
            if (fullCount <= 0 || liveBudget <= 0) return 0;
            double n = fullCount * (liveBudget >= FullBudget ? 1.0 : LeanShare);
            if (reduced) n *= ReducedCount;
            return Math.Max(1, (int)Math.Round(n, MidpointRounding.ToEven));
        }

        private static double Envelope(double age, double life)
        {
            if (life <= 0 || age <= 0 || age >= life) return 0;
            double edge = Math.Min(age / life, 1.0 - age / life) / FadeShare;
            return edge >= 1 ? 1 : edge * edge * (3 - 2 * edge);
        }

        public static double Alpha(double peak, double age, double life, double gain) =>
            Math.Min(SmallAlphaMax, Math.Max(0, peak) * Envelope(age, life) * Math.Clamp(gain, 0, 1.5));

        public static double MaxDepth(double sizePx, double breathePx) =>
            StripPx - sizePx / 2 - breathePx - InnerClearPx;

        public static double Advance(double along, double speedPx, double dt) =>
            along + speedPx * Math.Max(0.0, dt);

        public static bool IsSpent(double age, double life, double along, double length, double spanPx) =>
            age >= life || along < -spanPx || along > length + spanPx;

        /// <summary>Map clockwise along-distance and inward depth onto each clipped strip.</summary>
        public static (double X, double Y) Position(EdgeSide side, double along, double depth, double w, double h) => side switch
        {
            EdgeSide.Top => (along, depth),
            EdgeSide.Right => (w - depth, along),
            EdgeSide.Bottom => (w - along, h - depth),
            _ => (depth, h - along),
        };

        public static double Length(EdgeSide side, double w, double h) =>
            side is EdgeSide.Top or EdgeSide.Bottom ? w : h;
    }
}
