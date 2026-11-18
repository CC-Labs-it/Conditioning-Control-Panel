namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Froth's option box (owner, 2026-11-15): how big a merged bubble may grow, how far a big pop
/// reaches, how hard the cursor pushes. Defaults reproduce the effect as built (4.5 times a fresh
/// bubble, full reach, full push). OWNER RULE: none of these touches pay. A merged bubble pays
/// the sum of its parts at any size (<see cref="FrothRules.MergedPayout"/>).
/// </summary>
public static class FrothOptions
{
    /// <summary>Biggest merged bubble, in sizes of a fresh one. Never under the ripe size, so a gold rim stays reachable.</summary>
    public const double MaxSizeMin = 2.5, MaxSizeMax = 6.0, MaxSizeDefault = FrothRules.MaxGrow, MaxSizeStep = 0.5;
    /// <summary>Pop reach in percent. 0 = a pop never sets off its neighbours.</summary>
    public const int ReachMin = 0, ReachMax = 200, ReachDefault = 100, ReachStep = 10;
    /// <summary>Cursor push in percent. 0 = the cursor never moves a bubble.</summary>
    public const int PushMin = 0, PushMax = 200, PushDefault = 100, PushStep = 10;

    public static double ClampMaxSize(double v)
    {
        if (double.IsNaN(v)) return MaxSizeDefault;
        return Math.Round(Math.Clamp(v, MaxSizeMin, MaxSizeMax) / MaxSizeStep) * MaxSizeStep;
    }

    public static int ClampReach(int v) => Snap(v, ReachMin, ReachMax, ReachStep);
    public static int ClampPush(int v) => Snap(v, PushMin, PushMax, PushStep);

    /// <summary>Multiplier on <see cref="FrothRules.Reach"/>: 1 at 100 percent.</summary>
    public static double ReachFactor(int reach) => ClampReach(reach) / 100.0;

    /// <summary>Multiplier on <see cref="FrothRules.NudgeAccel"/>: 1 at 100 percent.</summary>
    public static double PushFactor(int push) => ClampPush(push) / 100.0;

    private static int Snap(int v, int lo, int hi, int step)
        => (int)Math.Round(Math.Clamp(v, lo, hi) / (double)step, MidpointRounding.AwayFromZero) * step;
}
