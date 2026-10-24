using System;

namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// Pure rules the "windows" surfaces of Gentle visuals share (video windows, lock card, Chaos
    /// overlays). Everything takes plain values so tests never touch App.Settings; the live switch is
    /// <see cref="GentleVisuals"/>. Every rule returns today's value when gentle is off.
    /// </summary>
    public static class GentleWindowRules
    {
        /// <summary>The LibVLC / page playback rate for a gentle speed. 1 (no SetRate call) when
        /// off or at full speed.</summary>
        public static float PlaybackRate(bool on, double speed)
            => on ? (float)GentleVisuals.ClampSpeed(speed) : 1f;

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
