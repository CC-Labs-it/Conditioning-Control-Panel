using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Video.Floating
{
    internal static class FloatingFrameLook
    {
        public const double BorderDip = 4.0;
        public const double CornerDip = 10.0;

        public const double LightInSeconds = 0.18;
        public const double LightOutSeconds = 0.32;
        public const double BreathSeconds = 2.4;

        public const double QuietInk = 0.62;
        public const double BreathLift = 0.38;

        public const byte AccentR = 0xFF, AccentG = 0x69, AccentB = 0xB4;
        private const byte InkR = 0x16, InkG = 0x0C, InkB = 0x14;

        public static double Approach(double lit, bool on, double dt)
        {
            if (dt <= 0) return Math.Clamp(lit, 0, 1);
            var step = dt / (on ? LightInSeconds : LightOutSeconds);
            return on ? Math.Min(1.0, lit + step) : Math.Max(0.0, lit - step);
        }

        public static double Breath(double seconds, MotionLevel level)
        {
            var scale = level == MotionLevel.Full ? 1.0 : level == MotionLevel.Reduced ? 0.5 : 0.0;
            if (scale <= 0) return 0;
            return scale * (0.5 - 0.5 * Math.Cos(2.0 * Math.PI * seconds / BreathSeconds));
        }

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
