namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Deck's two options (owner, 2026-11-15): about how many flips a card takes before it breaks, and
/// how far the break knocks the cards and bubbles around it. Defaults are the stock numbers
/// exactly: a card breaks on a roll of 3 to 5, and the ring reaches 1.6 card sides.
/// </summary>
public static class DeckOptions
{
    /// <summary>Flips to break: the middle of the roll. Each card rolls one either side of it.</summary>
    public const int FlipsMin = 1, FlipsMax = 7, FlipsDefault = 4;
    /// <summary>Break reach, percent of the stock ring. 0 = a break knocks nothing.</summary>
    public const int ReachMin = 0, ReachMax = 200, ReachDefault = 100;
    /// <summary>The stock ring reach in sides of the card that broke.</summary>
    public const double StockReachSides = 1.6;

    public static int ClampFlips(int v) => Math.Clamp(v, FlipsMin, FlipsMax);
    public static int ClampReach(int v) => Math.Clamp(v, ReachMin, ReachMax);

    /// <summary>Slider value to a stored percent: snapped to steps of 5.</summary>
    public static int SnapReach(double sliderValue) => (int)(Math.Round(sliderValue / 5) * 5);

    /// <summary>The BreakAt roll, inclusive both ends. Stock (4) is 3..5; never below 1.</summary>
    public static (int Min, int Max) BreakRange(int flips)
    {
        int f = ClampFlips(flips);
        return (Math.Max(1, f - 1), f + 1);
    }

    /// <summary>How far the ring travels, in sides of the card that broke.</summary>
    public static double ReachSides(int reachPercent) => StockReachSides * (ClampReach(reachPercent) / 100.0);
}
