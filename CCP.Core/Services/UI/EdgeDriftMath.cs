using System;
namespace ConditioningControlPanel.Services.UI
{
    public enum EdgeSide
    {
        Top,
        Right,
        Bottom,
        Left,
    }

    public static class EdgeDriftMath
    {
        public const double SpeedMin = 0.018, SpeedMax = 0.030;
        public const double DepthMin = 0.15, DepthMax = 0.85;
        public const double SizeMinPx = 1.5, SizeMaxPx = 3.0;
        public const double LifeMin = 9.0, LifeMax = 16.0;
        public const double BaseAlpha = 0.30;
        public const double FlickerMin = 0.85;
        public const double BudgetShare = 0.30;
        public const int MaxPerStrip = 6;
        public const double EndFade = 0.05;
        public const double SpawnEverySeconds = 0.6;

        public static int Target(int liveBudget) =>
            liveBudget <= 0 ? 0 : Math.Min(MaxPerStrip, (int)Math.Round(liveBudget * BudgetShare));

        public static double Advance(double along, double speed, double dt) =>
            along + Math.Max(0.0, speed) * Math.Max(0.0, dt);

        public static bool IsSpent(double along, double life) => life <= 0.0 || along >= 1.0;

        public static (double X, double Y) Position(EdgeSide side, double along, double depth) => side switch
        {
            EdgeSide.Top => (along, depth),
            EdgeSide.Right => (1.0 - depth, along),
            EdgeSide.Bottom => (1.0 - along, 1.0 - depth),
            _ => (depth, 1.0 - along),
        };

        public static double Alpha(double along, double life, double max, double flickerPhase, double intensity)
        {
            if (max <= 0) return 0;
            double env = Math.Sin(Math.PI * Math.Clamp(1.0 - life / max, 0.0, 1.0));
            double flicker = FlickerMin + (1.0 - FlickerMin) * (0.5 + 0.5 * Math.Sin(flickerPhase));
            double ends = Math.Clamp(Math.Min(along, 1.0 - along) / EndFade, 0.0, 1.0);
            return BaseAlpha * env * flicker * Math.Clamp(intensity, 0.0, 1.5) * ends;
        }
    }

}
