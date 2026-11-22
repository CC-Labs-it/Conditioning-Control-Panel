namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// The numbers Lights Down runs on this frame, from the three options. <see cref="PeakAlpha"/> is
/// how dark the dim gets where it is darkest (0..0.95), <see cref="InRate"/> how fast it comes in
/// (full screen: exponential per second once the mouse rests; floating: scales the slow growth),
/// <see cref="OutRate"/> how fast it pulls back (linear per second).
/// </summary>
public readonly record struct LightsDownTuning(double PeakAlpha, double InRate, double OutRate)
{
    public static readonly LightsDownTuning Stock = LightsDownOptions.Tuning(
        LightsDownOptions.DarknessDefault, LightsDownOptions.DimSpeedDefault, LightsDownOptions.ReturnSpeedDefault);
}

/// <summary>
/// Lights Down's three options (owner, 2026-11-15, kept through the 2026-11-17 rework): how dark it
/// gets, how fast it comes in, how fast it pulls back when the mouse moves or the eyes leave.
/// Defaults are the reworked effect exactly (owner, 2026-11-17): full screen = a small dim at the
/// screen edges that pulls back while the mouse moves; floating = the whole screen dims slowly from
/// the edges in, except a soft hole with a mod-coloured halo around every floating video.
/// </summary>
public static class LightsDownOptions
{
    /// <summary>Full screen: once the mouse rests, the edge dim comes back at this exponential rate (63% in 1.1 s).</summary>
    public const double StockInRate = 0.9;
    /// <summary>Pull back, linear per second: from full to nothing in 0.4 s.</summary>
    public const double StockOutRate = 2.5;
    /// <summary>Floating: seconds from the first edge shadow to the whole screen dimmed, at the stock speed.</summary>
    public const double StockGrowSeconds = 24;

    /// <summary>Darkness, percent (50 = stock).</summary>
    public const int DarknessMin = 0, DarknessMax = 100, DarknessDefault = 50;
    /// <summary>Dimming speed, percent of the stock in rate.</summary>
    public const int DimSpeedMin = 50, DimSpeedMax = 150, DimSpeedDefault = 100;
    /// <summary>Lights back speed, percent of the stock pull back.</summary>
    public const int ReturnSpeedMin = 50, ReturnSpeedMax = 150, ReturnSpeedDefault = 100;

    /// <summary>The darkest the dim ever gets (darkness 100).</summary>
    public const double MaxAlpha = 0.95;

    public static int ClampDarkness(int v) => Math.Clamp(v, DarknessMin, DarknessMax);
    public static int ClampDimSpeed(int v) => Math.Clamp(v, DimSpeedMin, DimSpeedMax);
    public static int ClampReturnSpeed(int v) => Math.Clamp(v, ReturnSpeedMin, ReturnSpeedMax);

    /// <summary>Slider value to a stored percent: snapped to steps of 5.</summary>
    public static int Snap(double sliderValue) => (int)(Math.Round(sliderValue / 5) * 5);

    /// <summary>Darkness percent to the peak alpha: 0 = none, 50 = about 0.64, 100 = 0.95.
    /// Eased so the low half of the slider still reads as a change.</summary>
    public static double PeakAlpha(int darknessPercent)
    {
        double d = ClampDarkness(darknessPercent) / 100.0;
        return MaxAlpha * (1 - Math.Pow(1 - d, 1.6));
    }

    public static LightsDownTuning Tuning(int darknessPercent, int dimSpeedPercent, int returnSpeedPercent)
        => new(PeakAlpha(darknessPercent),
            StockInRate * (ClampDimSpeed(dimSpeedPercent) / 100.0),
            StockOutRate * (ClampReturnSpeed(returnSpeedPercent) / 100.0));

    /// <summary>Floating growth length for this tuning: faster dimming = shorter.</summary>
    public static double GrowSeconds(LightsDownTuning t)
        => StockGrowSeconds * StockInRate / Math.Max(1e-6, t.InRate);

    /// <summary>
    /// The pull back rate a frame uses. Gentle visuals (<paramref name="gentleFadeSeconds"/> &gt; 0)
    /// only lengthen it: never faster than one fade length, never under 0.3 s.
    /// </summary>
    public static double OutRateFor(LightsDownTuning t, double gentleFadeSeconds)
        => gentleFadeSeconds > 0 ? Math.Min(t.OutRate, 1 / Math.Max(0.3, gentleFadeSeconds)) : t.OutRate;
}
