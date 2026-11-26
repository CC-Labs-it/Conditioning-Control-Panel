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

    /// <summary>"Reach" (owner, 2026-11-17), percent of the way in the fog may come: at the bottom it stays a
    /// rim at the screen edges, 100 = the whole screen, as shipped.</summary>
    public const int ReachMin = 10, ReachMax = 100, ReachDefault = 100;
    public static int ClampReach(int pct) => Math.Clamp(pct, ReachMin, ReachMax);
    /// <summary><see cref="Level(double,double)"/> scaled by the reach, the starting rim included (owner
    /// 2026-11-18, tier-2 round: the old curve held only the creep back, so the bottom still kept the
    /// 0.45 rim, a band a fifth of the screen deep). Now 10 percent is a thin faint line at the very
    /// edges, 25 about a tenth of the height, 100 the fog as shipped. The creep still covers the same
    /// share of the way in the same time, so a low reach is a slow rim, never a fog that stops dead.</summary>
    public static double Level(double ageSeconds, double creepSeconds, int reachPct)
        => Level(ageSeconds, creepSeconds) * ClampReach(reachPct) / 100.0;

    /// <summary>The filter opacity (0..1) at which the fog is as thick as it shipped (the filter's own default, 10 percent).</summary>
    public const double StrengthPivot = 0.10;

    /// <summary>
    /// How thick the fog is drawn for the pink filter's opacity (0..1). From the default up it is the
    /// shipped curve (thicker than the flat tint, never opaque). Below the default it thins in a
    /// straight line to nothing (owner 2026-11-18, tier-2 round: the old 0.3 floor kept 5 percent
    /// nearly as dense as 10), so the slider's bottom is half the default fog.
    /// </summary>
    public static double Strength(double filterOpacity)
    {
        double o = double.IsNaN(filterOpacity) ? StrengthPivot : Math.Clamp(filterOpacity, 0, 1);
        double stock = Math.Clamp(0.3 + o * 1.6, 0.3, 0.82);
        return o >= StrengthPivot ? stock : (0.3 + StrengthPivot * 1.6) * o / StrengthPivot;
    }

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
