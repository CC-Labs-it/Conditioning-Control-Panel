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

    /// <summary>"Fewest flips" / "Most flips" (ccp-bugs #1636): the roll's own ends, 1 to 9.
    /// <see cref="RollUnset"/> (the default) = follow the older single slider.</summary>
    public const int RollMin = 1, RollMax = 9, RollUnset = 0;
    public static int ClampRoll(int v) => Math.Clamp(v, RollMin, RollMax);
    /// <summary>A stored end: 0 or below stays unset, anything else is clamped to 1..9.</summary>
    public static int ClampEnd(int v) => v <= RollUnset ? RollUnset : ClampRoll(v);

    /// <summary>
    /// The BreakAt roll with the player's own ends. An unset end (0) follows the older single slider
    /// (<paramref name="flips"/>, one either side), so a settings file or preset from before the two
    /// ends existed rolls exactly as it did. Ends are clamped to 1..9 and never cross: a low end above
    /// the high end lifts the high end to it.
    /// </summary>
    public static (int Min, int Max) BreakRange(int flips, int fewest, int most)
    {
        var (lo, hi) = BreakRange(flips);
        if (fewest > RollUnset) lo = ClampRoll(fewest);
        if (most > RollUnset) hi = ClampRoll(most);
        return (lo, Math.Max(lo, hi));
    }

    /// <summary>
    /// The two ends to store for a roll the player set on the sliders. An end equal to what the older
    /// single slider already rolls (<paramref name="flips"/>, one either side) is stored unset, so
    /// moving one slider never marks the other as changed and a roll back at stock (3..5) counts as
    /// no change at all. <see cref="BreakRange(int, int, int)"/> of the result is the same roll.
    /// </summary>
    public static (int Fewest, int Most) StoredEnds(int flips, int lo, int hi)
    {
        var (followLo, followHi) = BreakRange(flips);
        lo = ClampRoll(lo);
        hi = Math.Max(lo, ClampRoll(hi));
        return (lo == followLo ? RollUnset : lo, hi == followHi ? RollUnset : hi);
    }

    /// <summary>How far the ring travels, in sides of the card that broke.</summary>
    public static double ReachSides(int reachPercent) => StockReachSides * (ClampReach(reachPercent) / 100.0);
}
