namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// The numbers the Undertow lens runs on this tick, as shares of the screen width (the shrink as a
/// plain multiplier). <see cref="Stock"/> is the lens as it shipped before it had options.
/// </summary>
public readonly record struct UndertowTuning(
    double MaxRadius, double StillRadius, double MinRadius, double KickPerClick, double ShrinkScale)
{
    public static readonly UndertowTuning Stock = UndertowOptions.Tuning(
        UndertowOptions.SizeDefault, UndertowOptions.ShrinkDefault, UndertowOptions.WidenDefault);
}

/// <summary>
/// Undertow's three options (owner, 2026-11-15): lens size, how fast it shrinks on its own, how
/// much a click widens it. Each is a percent of the stock lens; 100 is the lens exactly as it was.
/// Every clamp and the slider-to-number mapping live here; the lens maths takes a
/// <see cref="UndertowTuning"/>.
/// </summary>
public static class UndertowOptions
{
    // The stock lens (the mockup and the owner's earlier calls), shares of the screen width.
    public const double StockMaxRadius = 0.5;
    public const double StockStillRadius = 0.25;
    public const double StockMinRadius = 0.09;
    public const double StockKickPerClick = 0.2;

    /// <summary>Lens size: scales the whole lens (widest, smallest and the still lens) together.</summary>
    public const int SizeMin = 50, SizeMax = 150, SizeDefault = 100;
    /// <summary>Shrink speed: 0 holds the lens open, 200 drains twice as fast.</summary>
    public const int ShrinkMin = 0, ShrinkMax = 200, ShrinkDefault = 100;
    /// <summary>Click widen: 0 = a click only ripples, 200 = twice the widening per click.</summary>
    public const int WidenMin = 0, WidenMax = 200, WidenDefault = 100;

    public static int ClampSize(int v) => Math.Clamp(v, SizeMin, SizeMax);
    public static int ClampShrink(int v) => Math.Clamp(v, ShrinkMin, ShrinkMax);
    public static int ClampWiden(int v) => Math.Clamp(v, WidenMin, WidenMax);

    /// <summary>Slider value to a stored percent: snapped to steps of 5, then clamped.</summary>
    public static int Snap(double sliderValue) => (int)(Math.Round(sliderValue / 5) * 5);

    public static UndertowTuning Tuning(int sizePercent, int shrinkPercent, int widenPercent)
    {
        double size = ClampSize(sizePercent) / 100.0;
        return new UndertowTuning(
            StockMaxRadius * size,
            StockStillRadius * size,
            StockMinRadius * size,
            StockKickPerClick * (ClampWiden(widenPercent) / 100.0),
            ClampShrink(shrinkPercent) / 100.0);
    }
}
