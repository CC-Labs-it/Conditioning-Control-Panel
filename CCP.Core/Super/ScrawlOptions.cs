namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Scrawl's option box (owner, 2026-11-15): how long the wall stamps stay, how many the walls
/// hold, how hard a clicked word slams. Defaults reproduce the effect as it shipped (14 s, 44
/// stamps, full slam). Clamps and slider maps only; the head reads the settings and passes the
/// numbers in.
/// </summary>
public static class ScrawlOptions
{
    public const int StampSecondsMin = 3, StampSecondsMax = 60, StampSecondsDefault = 14;
    public const int StampCapMin = 6, StampCapMax = 80, StampCapDefault = 44;
    /// <summary>Slam strength in percent. 0 = off: a click on a word goes through untouched.</summary>
    public const int SlamMin = 0, SlamMax = 200, SlamDefault = 100, SlamStep = 25;

    /// <summary>Stamp size in percent of the bouncing text size (owner 2026-11-18: big faint text must not mean giant stamps).</summary>
    public const int StampSizeMin = 25, StampSizeMax = 150, StampSizeDefault = 100, StampSizeStep = 5;
    /// <summary>The fewest stamps a scaled-down cap ever leaves.</summary>
    public const int EffectiveCapFloor = 4;

    /// <summary>Clamped and snapped to 5 percent steps.</summary>
    public static int ClampStampSize(int v)
    {
        v = Math.Clamp(v, StampSizeMin, StampSizeMax);
        return (int)Math.Round(v / (double)StampSizeStep, MidpointRounding.AwayFromZero) * StampSizeStep;
    }

    /// <summary>
    /// The cap the walls really hold. The slider counts stamps at 100 percent text; a bigger stamp
    /// covers more wall, so the cap shrinks in step with it (300 percent holds a third), and a second
    /// bouncing word halves it again: two words hit twice as often. Never grows past the slider.
    /// </summary>
    public static int EffectiveCap(int cap, double stampPercent, int words)
    {
        cap = ClampStampCap(cap);
        double scaled = stampPercent > 100 ? cap * 100.0 / stampPercent : cap;
        if (words > 1) scaled /= 2;
        return Math.Clamp((int)Math.Round(scaled), Math.Min(EffectiveCapFloor, cap), cap);
    }

    public static int ClampStampSeconds(int v) => Math.Clamp(v, StampSecondsMin, StampSecondsMax);
    public static int ClampStampCap(int v) => Math.Clamp(v, StampCapMin, StampCapMax);

    /// <summary>Clamped and snapped to 25 percent steps.</summary>
    public static int ClampSlam(int v)
    {
        v = Math.Clamp(v, SlamMin, SlamMax);
        return (int)Math.Round(v / (double)SlamStep, MidpointRounding.AwayFromZero) * SlamStep;
    }

    /// <summary>False at 0: no word is slammed and no click is ever taken.</summary>
    public static bool SlamOn(int slam) => ClampSlam(slam) > 0;

    /// <summary>Multiplier on the knock the other words get: 1 at 100 percent.</summary>
    public static double KnockFactor(int slam) => ClampSlam(slam) / 100.0;

    /// <summary>Multiplier on the slam's screen shake. Gentle visuals never shake harder than the stock slam.</summary>
    public static double ShakeFactor(int slam, bool gentle)
    {
        double f = KnockFactor(slam);
        return gentle ? Math.Min(1, f) : f;
    }
}
