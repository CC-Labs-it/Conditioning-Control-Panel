using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Bubbles v2 (Back Room prizes): the pure half of the ambient bubble motion choice, ported from
    /// WPF <c>Services/AmbientBubbleMotion.cs</c> (renamed: the WPF head still carries its own copy,
    /// and two public types with one full name would collide there). The picker stores a
    /// <see cref="BubbleMotionStyle"/>; every spawn resolves it to ONE concrete style. Ownership is
    /// passed in by the head and never read from settings, so a synced profile carrying a style the
    /// account does not own simply floats up. MotionLevel Off is plain FloatUp, Reduced halves the
    /// v2 speed and the spiral turns.
    /// </summary>
    public static class BubbleMotionRoll
    {
        /// <summary>WPF AmbientBubbleMotion.IsOwned: Mix needs any v2 style.</summary>
        public static bool IsOwned(BubbleMotionStyle style, bool rainOwned, bool spiralOwned) => style switch
        {
            BubbleMotionStyle.FloatUp => true,
            BubbleMotionStyle.Rain => rainOwned,
            BubbleMotionStyle.SpiralIn => spiralOwned,
            BubbleMotionStyle.Mix => rainOwned || spiralOwned,
            _ => false,
        };

        /// <summary>WPF AmbientBubbleMotion.Resolve: <paramref name="roll"/> (uniform 0..1) is used only
        /// by Mix, which picks with equal weight among FloatUp plus the owned v2 styles. Never returns Mix.</summary>
        public static BubbleMotionStyle Resolve(BubbleMotionStyle picked, bool rainOwned, bool spiralOwned,
                                                MotionLevel level, double roll)
        {
            if (level == MotionLevel.Off) return BubbleMotionStyle.FloatUp;
            switch (picked)
            {
                case BubbleMotionStyle.Rain:
                    return rainOwned ? BubbleMotionStyle.Rain : BubbleMotionStyle.FloatUp;
                case BubbleMotionStyle.SpiralIn:
                    return spiralOwned ? BubbleMotionStyle.SpiralIn : BubbleMotionStyle.FloatUp;
                case BubbleMotionStyle.Mix:
                {
                    int n = 1 + (rainOwned ? 1 : 0) + (spiralOwned ? 1 : 0);
                    int idx = (int)Math.Clamp(Math.Floor(Math.Clamp(roll, 0.0, 0.999999) * n), 0, n - 1);
                    if (idx == 0) return BubbleMotionStyle.FloatUp;
                    if (rainOwned && idx == 1) return BubbleMotionStyle.Rain;
                    return spiralOwned ? BubbleMotionStyle.SpiralIn : BubbleMotionStyle.Rain;
                }
                default:
                    return BubbleMotionStyle.FloatUp;
            }
        }

        /// <summary>Speed multiplier for the v2 styles at a motion level (FloatUp is never scaled).</summary>
        public static double SpeedMult(MotionLevel level) => level == MotionLevel.Reduced ? 0.5 : 1.0;
    }

    /// <summary>
    /// Spiral In path maths (WPF <c>SpiralInPath</c>), in DIPs and steps. A bubble starts on a ring
    /// around the screen centre and advances its angle while the radius shrinks at a constant rate,
    /// so the angle step lands the requested turns exactly when the radius reaches the core. The
    /// fade runs over the last <see cref="FadeBandDip"/> DIPs of radius and hits 0 at the core.
    /// </summary>
    public static class BubbleSpiralPath
    {
        public const double StartRadiusFrac = 0.42;
        public const double CoreDip = 24;
        public const double FadeBandDip = 110;
        public const double FullTurns = 2.5;
        public const double RadialPerSpeed = 0.55;

        public readonly record struct State(double Radius, double Angle, double Fade, bool Done);

        public static double StartRadius(double shortDim) => Math.Max(CoreDip * 4, shortDim * StartRadiusFrac);

        public static double Turns(MotionLevel level) => level == MotionLevel.Reduced ? FullTurns * 0.5 : FullTurns;

        public static State Start(double startRadius, double startAngle) => new(startRadius, startAngle, 1.0, false);

        public static State Step(State s, double startRadius, double radialPerFrame, double turns, double direction, double ts)
        {
            if (s.Done) return s;
            double radial = Math.Max(0.01, radialPerFrame);
            double r = Math.Max(CoreDip, s.Radius - radial * ts);
            double totalFrames = Math.Max(1.0, (startRadius - CoreDip) / radial);
            double dir = direction < 0 ? -1.0 : 1.0;
            double a = s.Angle + dir * (2.0 * Math.PI * turns / totalFrames) * ts;
            double fade = Math.Clamp((r - CoreDip) / FadeBandDip, 0.0, 1.0);
            bool done = r <= CoreDip + 1e-9;
            return new State(r, a, fade, done);
        }
    }
}
