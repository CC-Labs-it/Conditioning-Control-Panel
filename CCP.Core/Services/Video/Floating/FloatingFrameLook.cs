using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Video.Floating
{
    /// <summary>
    /// The numbers behind a floating video's border: how thick, how round, what colour when it is
    /// quiet and when it carries the sound, and how the change eases. Pure, so the tests can read
    /// the same values the frame paints.
    /// </summary>
    internal static class FloatingFrameLook
    {
        /// <summary>Border width in DIPs. The video is inset by this much on every side.</summary>
        public const double BorderDip = 4.0;
        /// <summary>Corner radius in DIPs where the frame has to cut the corner itself (Windows 10).</summary>
        public const double CornerDip = 10.0;

        /// <summary>The light comes up in about this long...</summary>
        public const double LightInSeconds = 0.18;
        /// <summary>...and goes down a little slower.</summary>
        public const double LightOutSeconds = 0.32;
        /// <summary>One full breath of a lit border.</summary>
        public const double BreathSeconds = 2.4;

        /// <summary>How far a quiet border sinks toward the ink.</summary>
        public const double QuietInk = 0.62;
        /// <summary>How far the top of a breath lifts a lit border toward white.</summary>
        public const double BreathLift = 0.38;

        /// <summary>Fallback when the theme has no PinkColor (tests, a stripped host).</summary>
        public const byte AccentR = 0xFF, AccentG = 0x69, AccentB = 0xB4;
        private const byte InkR = 0x16, InkG = 0x0C, InkB = 0x14;

        /// <summary>Move the light toward its target, linear in time, never past it.</summary>
        public static double Approach(double lit, bool on, double dt)
        {
            if (dt <= 0) return Math.Clamp(lit, 0, 1);
            var step = dt / (on ? LightInSeconds : LightOutSeconds);
            return on ? Math.Min(1.0, lit + step) : Math.Max(0.0, lit - step);
        }

        /// <summary>Breath 0..1 at <paramref name="seconds"/>: half amplitude at Reduced, none at Off.</summary>
        public static double Breath(double seconds, MotionLevel level)
        {
            var scale = level == MotionLevel.Full ? 1.0 : level == MotionLevel.Reduced ? 0.5 : 0.0;
            if (scale <= 0) return 0;
            return scale * (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * seconds / BreathSeconds));
        }

        /// <summary>
        /// The border colour. <paramref name="lit"/> 0 = quiet (the accent sunk toward ink),
        /// 1 = the accent itself; <paramref name="breath"/> lifts a lit border toward white.
        /// </summary>
        public static (byte R, byte G, byte B) Border(byte ar, byte ag, byte ab, double lit, double breath)
        {
            lit = Smooth(Math.Clamp(lit, 0, 1));
            breath = Math.Clamp(breath, 0, 1) * lit;
            var ink = QuietInk * (1.0 - lit);
            var lift = BreathLift * breath;
            return (Mix(ar, InkR, 255, ink, lift), Mix(ag, InkG, 255, ink, lift), Mix(ab, InkB, 255, ink, lift));
        }

        private static byte Mix(byte accent, byte ink, byte white, double toInk, double toWhite)
        {
            var v = accent + (ink - accent) * toInk;
            v += (white - v) * toWhite;
            return (byte)Math.Clamp(Math.Round(v), 0, 255);
        }

        private static double Smooth(double t) => t * t * (3.0 - 2.0 * t);
    }
}
