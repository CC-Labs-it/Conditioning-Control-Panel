namespace ConditioningControlPanel.Looks;

/// <summary>
/// "Limit pink filter to" (accessibility, asked 2026-11-22 by a player who gets light-sensitivity
/// migraines from a filter over 10 percent). Off by default. When on, nothing may paint the pink
/// tint or the V2 fog stronger than the limit: not the slider, a preset, a session, a mod, a ramp,
/// a pulse or a Deeper band. The heads apply it where the tint is drawn, so every owner of the
/// opacity upstream keeps working unchanged and only the painted value is clipped. Pure.
/// </summary>
public static class PinkComfort
{
    /// <summary>The limit's range in percent, the same as the pink filter slider (5 to 50).</summary>
    public const int MinPct = 5, MaxPct = 50, DefaultPct = 10;

    public static int ClampPct(int pct) => Math.Clamp(pct, MinPct, MaxPct);

    /// <summary>The highest opacity (0..1) the tint may paint: 1 with the limit off.</summary>
    public static double Cap(bool on, int pct) => on ? ClampPct(pct) / 100.0 : 1.0;

    /// <summary>The opacity (0..1) the tint paints for the one it was asked for. Limit off: the
    /// asked value as it was (only kept inside 0..1).</summary>
    public static double Apply(double opacity, bool on, int pct)
    {
        double o = double.IsNaN(opacity) ? 0 : Math.Clamp(opacity, 0, 1);
        return Math.Min(o, Cap(on, pct));
    }

    /// <summary>The V2 fog's thickness under the limit. The fog is drawn thicker than the flat tint
    /// on purpose, so a capped filter alone would still paint about 46 percent at a 10 percent limit;
    /// with the limit on the fog is held to the limit too. Limit off: unchanged.</summary>
    public static double FogStrength(double strength, bool on, int pct)
    {
        double s = double.IsNaN(strength) ? 0 : Math.Clamp(strength, 0, 1);
        return Math.Min(s, Cap(on, pct));
    }

    /// <summary>True when the limit holds the filter's own slider value back (the note on the slider).</summary>
    public static bool Clips(int filterPct, bool on, int pct) => on && filterPct > ClampPct(pct);
}
