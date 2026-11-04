using System;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>Particle drift rules from WPF AmbientFxCanvas.cs:74.</summary>
    public static class EdgeDriftMath
    {
        /// <summary>Strip lengths per second: one full side in about 35 to 55 s.</summary>
        public const double SpeedMin = 0.018, SpeedMax = 0.030;
        /// <summary>Depth band across the strip, so no mote sits on the line or bleeds inward.</summary>
        public const double DepthMin = 0.15, DepthMax = 0.85;
        /// <summary>Mote diameter in NATIVE pixels (never min-scaled: the strip is 30 px thin).</summary>
        public const double SizeMinPx = 1.5, SizeMaxPx = 3.0;
        /// <summary>Life in seconds, on a sine envelope.</summary>
        public const double LifeMin = 9.0, LifeMax = 16.0;
        /// <summary>Peak alpha before flicker and intensity.</summary>
        public const double BaseAlpha = 0.30;
        /// <summary>Flicker floor; the ceiling is 1.0.</summary>
        public const double FlickerMin = 0.85;
        /// <summary>Share of the canvas's live particle budget one strip may spend.</summary>
        public const double BudgetShare = 0.30;
        /// <summary>Hard cap per strip (24 across the four sides).</summary>
        public const int MaxPerStrip = 6;
        /// <summary>Along-axis fade at each strip end, so a mote leaves a corner instead of popping.</summary>
        public const double EndFade = 0.05;
        /// <summary>Seconds between spawns while a strip is under target: fills over a few seconds.</summary>
        public const double SpawnEverySeconds = 0.6;

        /// <summary>Motes one strip may hold at this live budget: round(budget x 0.30), capped 6.</summary>
        public static int Target(int liveBudget) =>
            liveBudget <= 0 ? 0 : Math.Min(MaxPerStrip, (int)Math.Round(liveBudget * BudgetShare));

        /// <summary>Clockwise drift: along only ever grows.</summary>
        public static double Advance(double along, double speed, double dt) =>
            along + Math.Max(0.0, speed) * Math.Max(0.0, dt);

        /// <summary>A mote is spent when its life runs out or it reaches the strip's end.</summary>
        public static bool IsSpent(double along, double life) => life <= 0.0 || along >= 1.0;

        /// <summary>Element-normalized (x, y) of a mote on a strip of the given side.</summary>
        public static (double X, double Y) Position(EdgeSide side, double along, double depth) => side switch
        {
            EdgeSide.Top => (along, depth),
            EdgeSide.Right => (1.0 - depth, along),
            EdgeSide.Bottom => (1.0 - along, 1.0 - depth),
            _ => (depth, 1.0 - along),
        };

        /// <summary>
        /// Alpha: 0.30 x sine life envelope x flicker 0.85..1.0 x intensity x end fade.
        /// </summary>
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
