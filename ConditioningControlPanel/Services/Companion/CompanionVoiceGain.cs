using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The one place the companion's spoken-line gain is computed (7.1.8, owner: a "Voice level"
    /// slider, because the voice was drowned by the engine and had no level of its own).
    ///
    /// <para>Before the slider every voice path wrote its own <c>master^1.5 x trim</c>. The slider
    /// multiplies that by a factor that is exactly 1 at <see cref="DefaultLevel"/>, so a player who
    /// never touches it hears the same loudness as before, bit for bit.</para>
    ///
    /// <para>Below the default the factor falls in a straight line to silence at 0. Above it the
    /// factor climbs to <see cref="MaxBoost"/> at 100; the result is capped at full scale, so the
    /// boost is real whenever the master volume leaves headroom and never clips.</para>
    /// </summary>
    public static class CompanionVoiceGain
    {
        /// <summary>The level that reproduces the loudness the app had before the slider.</summary>
        public const int DefaultLevel = 80;

        /// <summary>Factor at level 100 (twice the old loudness, about +6 dB).</summary>
        public const float MaxBoost = 2f;

        // Per-path trims, unchanged from the code they were lifted out of.
        public const float IdleTrim = 0.8f;      // idle voice lines
        public const float BarkTrim = 0.85f;     // barks and voiced flashes
        public const float EventTrim = 0.56f;    // event phrases
        public const float NoteTrim = 0.9f;      // the note clip
        public const float PreviewTrim = 0.7f;   // phrase preview
        public const float FullTrim = 1f;        // the 50-click line

        /// <summary>The slider's own multiplier: 0 at 0, exactly 1 at the default, MaxBoost at 100.</summary>
        public static float LevelFactor(int voiceLevel)
        {
            var level = Math.Clamp(voiceLevel, 0, 100);
            if (level == DefaultLevel) return 1f;
            if (level < DefaultLevel) return level / (float)DefaultLevel;
            return 1f + (MaxBoost - 1f) * (level - DefaultLevel) / (100 - DefaultLevel);
        }

        /// <summary>
        /// Gain for one spoken line. <paramref name="masterVolume"/> and
        /// <paramref name="voiceLevel"/> are the 0-100 settings; 0 on either is silence.
        /// </summary>
        public static float Compute(int masterVolume, int voiceLevel, float trim)
        {
            var master = Math.Clamp(masterVolume, 0, 100) / 100f;
            if (master <= 0f) return 0f;
            return ApplyLevel((float)Math.Pow(master, 1.5) * trim, voiceLevel);
        }

        /// <summary>Lays the slider over a gain a caller already curved and trimmed.</summary>
        public static float ApplyLevel(float gain, int voiceLevel)
        {
            var level = Math.Clamp(voiceLevel, 0, 100);
            if (level <= 0) return 0f;
            return Math.Clamp(gain * LevelFactor(level), 0f, 1f);
        }

        /// <summary>The gain from the live settings. Null settings read as the defaults.</summary>
        public static float Current(float trim)
        {
            var s = App.Settings?.Current;
            return Compute(s?.MasterVolume ?? 100, s?.CompanionVoiceLevel ?? DefaultLevel, trim);
        }
    }
}
