using System;

namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// Gentle visuals: one switch for players who are sensitive to flashes and fast motion
    /// (renardeau, feedback thread 1535020659258691828, built 2026-10-14). The copy promises
    /// "softer, slower visuals", never "epilepsy safe".
    ///
    /// <para>The rules every surface follows while it is on:</para>
    /// <list type="bullet">
    /// <item>FADE: anything that appears or leaves (flash, subliminal, spiral, pink filter, brain
    /// drain, video window, lock card) takes at least <see cref="FadeSeconds"/> to do it. A surface
    /// that already fades slower keeps its own length (<see cref="Fade(double)"/> only lengthens).</item>
    /// <item>SPEED: GIFs, spirals and videos play at <see cref="Speed"/> (0.25-1). Frame delays are
    /// divided by it (<see cref="ScaleDelayMs"/>), LibVLC gets it through SetRate.</item>
    /// <item>RISE: no brightness or opacity rise is faster than <see cref="RiseFloorMs"/>
    /// (<see cref="RiseMs"/>); pulses that doubled an overlay at once now ease up and back.</item>
    /// <item>NO FLICKER: an effect that blinks (glitch exits, e-stim bolts, field flicker,
    /// Possession blinks) plays as a plain fade when <see cref="AllowFlicker"/> is false.</item>
    /// <item>The effect's own colour flashes and the Back Room / Lockdown safe modes read as on
    /// (<see cref="ColourFlashes"/>, <see cref="Photosafe"/>), and UI motion is capped at Reduced
    /// (Services/MotionFx.ResolveLevel).</item>
    /// </list>
    ///
    /// <para>Pure functions take the switch as a parameter so tests never touch App.Settings; the
    /// live properties read the current settings.</para>
    /// </summary>
    public static class GentleVisuals
    {
        public const double MinFadeSeconds = 0.25;
        public const double MaxFadeSeconds = 2.0;
        public const double DefaultFadeSeconds = 0.75;

        public const double MinSpeed = 0.25;
        public const double MaxSpeed = 1.0;
        public const double DefaultSpeed = 0.5;

        /// <summary>No brightness rise faster than this while gentle visuals are on.</summary>
        public const int RiseFloorMs = 300;

        /// <summary>The choices the settings row offers (seconds and playback speed).</summary>
        public static readonly double[] FadeChoices = { 0.25, 0.5, 0.75, 1.0, 1.5, 2.0 };
        public static readonly double[] SpeedChoices = { 1.0, 0.75, 0.5, 0.25 };

        public static double ClampFade(double seconds)
            => double.IsNaN(seconds) ? DefaultFadeSeconds : Math.Clamp(seconds, MinFadeSeconds, MaxFadeSeconds);

        public static double ClampSpeed(double speed)
            => double.IsNaN(speed) ? DefaultSpeed : Math.Clamp(speed, MinSpeed, MaxSpeed);

        /// <summary>
        /// The effective UI motion level: Off stays Off; the OS animation flag being off or gentle
        /// visuals being on caps it at Reduced (they only ever remove motion). Shared by every head.
        /// </summary>
        public static Models.MotionLevel CapMotion(Models.MotionLevel setting, bool osAnimationsEnabled, bool gentleVisuals)
        {
            if (setting == Models.MotionLevel.Off) return Models.MotionLevel.Off;
            if (!osAnimationsEnabled || gentleVisuals) return Models.MotionLevel.Reduced;
            return setting;
        }

        // ---- live reads ----

        /// <summary>True when the player switched gentle visuals on.</summary>
        public static bool On => CoreSettings.Service?.Current?.GentleVisuals == true;

        /// <summary>The fade length in seconds while on; 0 when off (nothing to lengthen to).</summary>
        public static double FadeSeconds
            => On ? ClampFade(CoreSettings.Service?.Current?.GentleFadeSeconds ?? DefaultFadeSeconds) : 0;

        /// <summary>Playback speed for GIFs, spirals and videos: 1 when off.</summary>
        public static double Speed
            => On ? ClampSpeed(CoreSettings.Service?.Current?.GentleSpeed ?? DefaultSpeed) : 1.0;

        public static bool AllowFlicker => !On;

        /// <summary>Effect colour flashes (Chaos vignette and friends) as the player set them, off while gentle.</summary>
        public static bool ColourFlashes(bool setting) => setting && !On;

        /// <summary>Photosensitive-safe modes (Lockdown Possession, Back Room Calm) read as on while gentle.</summary>
        public static bool Photosafe(bool setting) => setting || On;

        public static double Fade(double normalSeconds) => Fade(normalSeconds, On, FadeSeconds);

        public static TimeSpan Fade(TimeSpan normal) => TimeSpan.FromSeconds(Fade(normal.TotalSeconds));

        public static int FadeMs(int normalMs) => (int)Math.Round(Fade(normalMs / 1000.0) * 1000.0);

        public static int RiseMs(int normalMs) => RiseMs(normalMs, On);

        public static double ScaleDelayMs(double frameDelayMs) => ScaleDelayMs(frameDelayMs, Speed);

        // ---- pure rules ----

        /// <summary>Lengthen a fade to the gentle length; never shortens one that is already slower.</summary>
        public static double Fade(double normalSeconds, bool on, double gentleSeconds)
        {
            var normal = double.IsNaN(normalSeconds) || normalSeconds < 0 ? 0 : normalSeconds;
            return on ? Math.Max(normal, ClampFade(gentleSeconds)) : normal;
        }

        public static int RiseMs(int normalMs, bool on) => on ? Math.Max(normalMs, RiseFloorMs) : normalMs;

        /// <summary>A frame delay at the given speed: 0.5x doubles it. Speed is clamped, so never faster.</summary>
        public static double ScaleDelayMs(double frameDelayMs, double speed)
            => frameDelayMs / ClampSpeed(speed);
    }
}
