namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Steam's options in the gold box (owner, 2026-11-15): every clamp, default and slider-to-number
/// mapping. The middle of each reproduces the fog as it shipped. Opacity, colour and monitor are
/// the pink filter's own and are not repeated here.
/// </summary>
public static class SteamOptions
{
    /// <summary>Creep speed picks, the same order as the old Creep's speed setting (Slow, Normal, Fast).</summary>
    public const int Slow = 0, Normal = 1, Fast = 2;

    /// <summary>Seconds from a thin rim to the whole screen. Normal is the four minutes Steam shipped with.</summary>
    public static double CreepSeconds(int speed) => speed switch
    {
        Slow => 480,
        Fast => 120,
        _ => 240,
    };

    /// <summary>How far in the fog has crept after <paramref name="ageSeconds"/>: a thin rim at
    /// first (0.45), the whole screen (1.5) once <paramref name="creepSeconds"/> have passed.</summary>
    public static double Level(double ageSeconds, double creepSeconds)
        => 0.45 + 1.05 * Math.Clamp(ageSeconds / Math.Max(1, creepSeconds), 0, 1);

    /// <summary>"Fog returns", percent of the stock heal speed. 100 = as shipped.</summary>
    public const int ReturnMin = 25, ReturnMax = 300, ReturnDefault = 100;
    public static int ClampReturn(int pct) => Math.Clamp(pct, ReturnMin, ReturnMax);
    /// <summary>The heal rate (1/s) the field closes holes and swirls with. At the top it still
    /// takes about 1.5 s to close a hole, far slower than the Gentle 300 ms floor.</summary>
    public static double HealRate(int pct) => SteamField.HealRate * ClampReturn(pct) / 100.0;

    /// <summary>"Drift", percent of the stock billow. 0 = still fog, 100 = as shipped.</summary>
    public const int DriftMin = 0, DriftMax = 200, DriftDefault = 100;
    public static int ClampDrift(int pct) => Math.Clamp(pct, DriftMin, DriftMax);
    public static double Drift(int pct) => ClampDrift(pct) / 100.0;
}
