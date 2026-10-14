using System;

namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// Pure rules the "windows" surfaces of Gentle visuals share (video windows, lock card, Chaos
    /// overlays). Everything takes plain values so tests never touch App.Settings; the live switch is
    /// <see cref="Safety.GentleVisuals"/>. Every rule returns today's value when gentle is off.
    /// </summary>
    internal static class GentleWindowRules
    {
        /// <summary>The LibVLC / page playback rate for a gentle speed. Always 1 (owner desk test,
        /// 2026-10-14: "it should play normally"): videos keep the gentle fade in and out but play at
        /// normal speed; the speed setting slows GIFs and spirals only. Kept as the one seam every
        /// video path asks, so a later change is one line.</summary>
        public static float PlaybackRate(bool on, double speed) => 1f;

        /// <summary>Wall-clock seconds a clip of <paramref name="clipSeconds"/> takes at
        /// <paramref name="rate"/>. A slowed clip runs longer, so the duration guillotine and the
        /// stuck-queue timeout must stretch with it or they cut the clip short. Rate 1 (or junk)
        /// returns the clip length unchanged.</summary>
        public static double WallSeconds(double clipSeconds, double rate)
            => double.IsNaN(rate) || rate <= 0 || rate >= 1.0 ? clipSeconds : clipSeconds / rate;

        /// <summary>The soft exit (a black curtain that fades away after the video windows close)
        /// only follows a natural end. Panic, engine stop, app exit, Esc and every forced teardown
        /// stay instant.</summary>
        public static bool SoftExit(bool on, bool naturalEnd) => on && naturalEnd;

        /// <summary>Opacity keyframes (value, ms) for an effect that rises, holds and fades: the
        /// gentle stand-in for a strike or flicker that snapped to full brightness.</summary>
        public static (double Value, int Ms)[] RiseHoldFade(double peak, int riseMs, int holdMs, int fadeMs)
        {
            riseMs = Math.Max(0, riseMs);
            holdMs = Math.Max(0, holdMs);
            fadeMs = Math.Max(0, fadeMs);
            return new[]
            {
                (0.0, 0),
                (peak, riseMs),
                (peak, riseMs + holdMs),
                (0.0, riseMs + holdMs + fadeMs),
            };
        }
    }
}
