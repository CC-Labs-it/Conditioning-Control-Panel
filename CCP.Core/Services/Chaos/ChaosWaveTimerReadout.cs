using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The Pocket Watch countdown's text and warning states, lifted from WPF
/// <c>ChaosWaveTimerOverlay.SetText</c> (ConditioningControlPanel/Chaos/ChaosWaveTimerOverlay.cs:161-199)
/// so both heads draw the same readout. <see cref="Urgent"/> is the red clock (the wave's last
/// ten seconds); <see cref="FinalRush"/> is urgent on the last wave, where the clock also blinks.
/// </summary>
public readonly record struct ChaosWaveTimerReadout(string Wave, string Clock, string Score, bool Urgent, bool FinalRush)
{
    /// <summary>Seconds left in a wave at which the clock turns red (inclusive).</summary>
    public const double UrgentSeconds = 10;

    /// <summary>WPF's final-rush blink (:186): opacity 1.0 -> 0.25 over 420 ms, auto-reversed, forever.</summary>
    public static double BlinkOpacity(TimeSpan sinceStart)
    {
        const double half = 420;
        double t = Math.Abs(sinceStart.TotalMilliseconds) % (2 * half);
        double phase = t < half ? t / half : (2 * half - t) / half;
        return 1.0 - 0.75 * phase;
    }

    public static ChaosWaveTimerReadout For(int wave, int waveCount, double secLeftInWave, double score)
    {
        bool last = wave >= waveCount;
        int s = (int)Math.Max(0, Math.Ceiling(secLeftInWave));
        bool urgent = secLeftInWave <= UrgentSeconds;
        return new ChaosWaveTimerReadout(
            last ? "LAST WAVE" : $"WAVE {wave}/{waveCount}",
            $"{s / 60}:{s % 60:00}",
            $"{(int)score:N0}",
            urgent,
            urgent && last);
    }
}
