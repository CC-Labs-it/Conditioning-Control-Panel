namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// The numbers Lights Down runs on this frame. <see cref="Stock"/> is the effect as it shipped
/// before it had options: dim 0.5 per unit of depth, rise 0.55 per second (exponential), fall 1.3
/// per second (linear).
/// </summary>
public readonly record struct LightsDownTuning(double DimPerDepth, double RiseRate, double FallRate)
{
    public static readonly LightsDownTuning Stock = LightsDownOptions.Tuning(
        LightsDownOptions.DarknessDefault, LightsDownOptions.DimSpeedDefault, LightsDownOptions.ReturnSpeedDefault);
}

/// <summary>
/// Lights Down's three options (owner, 2026-11-15): how dark the room gets, how fast it gets there
/// while the video is watched, how fast the lights come back when it is not. Defaults are the
/// stock numbers exactly. Photosafe keeps the stock return (its own 0.4 s fade), whatever is set.
/// </summary>
public static class LightsDownOptions
{
    public const double StockDimPerDepth = 0.5;
    public const double StockRiseRate = 0.55;
    public const double StockFallRate = 1.3;

    /// <summary>Room darkness, percent: the dim over the room per unit of depth (50 = stock).</summary>
    public const int DarknessMin = 0, DarknessMax = 100, DarknessDefault = 50;
    /// <summary>Dimming speed, percent of the stock rise. 150 is still a slow exponential (about 1.2 s time constant).</summary>
    public const int DimSpeedMin = 50, DimSpeedMax = 150, DimSpeedDefault = 100;
    /// <summary>Return speed, percent of the stock fall when the eyes leave the picture.</summary>
    public const int ReturnSpeedMin = 50, ReturnSpeedMax = 150, ReturnSpeedDefault = 100;

    public static int ClampDarkness(int v) => Math.Clamp(v, DarknessMin, DarknessMax);
    public static int ClampDimSpeed(int v) => Math.Clamp(v, DimSpeedMin, DimSpeedMax);
    public static int ClampReturnSpeed(int v) => Math.Clamp(v, ReturnSpeedMin, ReturnSpeedMax);

    /// <summary>Slider value to a stored percent: snapped to steps of 5.</summary>
    public static int Snap(double sliderValue) => (int)(Math.Round(sliderValue / 5) * 5);

    public static LightsDownTuning Tuning(int darknessPercent, int dimSpeedPercent, int returnSpeedPercent)
    {
        // Darkness 50 must give exactly the stock 0.5.
        double dim = StockDimPerDepth * (ClampDarkness(darknessPercent) / (double)DarknessDefault);
        return new LightsDownTuning(
            dim,
            StockRiseRate * (ClampDimSpeed(dimSpeedPercent) / 100.0),
            StockFallRate * (ClampReturnSpeed(returnSpeedPercent) / 100.0));
    }

    /// <summary>The fall rate a frame uses: photosafe always keeps the stock one.</summary>
    public static double FallRateFor(LightsDownTuning t, bool photosafe) => photosafe ? StockFallRate : t.FallRate;
}
