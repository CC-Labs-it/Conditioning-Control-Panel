using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The pure numbers of the Chaos bubble field, lifted out of WPF <c>ChaosModeService</c>
/// (SpawnTick, OnBenignPopped) so every head spawns and scores alike. Same operations in the
/// same order as the original lines, so WPF's numbers do not move.
/// </summary>
public static class ChaosFieldMath
{
    /// <summary>The refill cadence never runs faster than this (ms) before the perf backoff.</summary>
    public const double MIN_SPAWN_INTERVAL_MS = 280;

    /// <summary>SpawnTick: the run intensity nudged by difficulty, clamped to 0..1.</summary>
    public static double EffectiveIntensity(double intensity, double difficultyMult) =>
        Math.Clamp(intensity + (difficultyMult - 1.0) * 0.15, 0, 1);

    /// <summary>SpawnTick field density: 6 early, 16 late, times the square root of difficulty.</summary>
    public static int MaxConcurrent(double intensity, double difficultyMult) =>
        (int)Math.Round((6 + intensity * 10) * Math.Sqrt(difficultyMult));

    /// <summary>
    /// SpawnTick refill cadence in ms: 1000 early to 320 late (divided by difficulty), divided
    /// by the spawn-rate multiplier, stretched by slow-mo (<paramref name="slowMoFactor"/> is 1
    /// when slow-mo is off), floored at <see cref="MIN_SPAWN_INTERVAL_MS"/>, then times the perf
    /// governor's backoff.
    /// </summary>
    public static double SpawnIntervalMs(double intensity, double difficultyMult, double spawnRateMult,
                                         double slowMoFactor = 1.0, double perfBackoff = 1.0)
    {
        double interval = (1000 - intensity * 680) / difficultyMult;
        interval /= Math.Clamp(spawnRateMult, 0.1, 10.0);
        interval /= slowMoFactor;
        return Math.Max(MIN_SPAWN_INTERVAL_MS, interval) * perfBackoff;
    }

    /// <summary>OnBenignPopped, the ordinary treat: base points times every pay multiplier.</summary>
    public static double TreatPoints(int strength, double benignBaseline, double payMult, double pendulum,
                                     double chanceFlip, double totalMult, double boonPayMult) =>
        ChaosRunEngine.BasePoints(strength) * benignBaseline * payMult * pendulum
            * chanceFlip * totalMult * boonPayMult;

    /// <summary>OnBenignPopped, the mimic prism: ten times base, no treat baseline.</summary>
    public static double PrismPoints(int strength, double totalMult, double boonPayMult) =>
        ChaosRunEngine.BasePoints(strength) * 10.0 * totalMult * boonPayMult;

    /// <summary>Focus a treat pop refuels: heavies (pay above 1x) a little more.</summary>
    public static double TreatFocusGain(double payMult) =>
        payMult > 1 ? ChaosTuning.FOCUS_PER_HEAVY : ChaosTuning.FOCUS_PER_POP;

    /// <summary>Blank Eyes: a pop's payout floats only when the sin is on and it paid something.</summary>
    public static bool ShowsPopScore(bool showPopScores, double points) => showPopScores && points > 0;
}
