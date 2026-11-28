namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Afterglow's option box (take two, owner 2026-11-15): how many ghosts make a line, how long
/// the finished line stays lit, how bright a resting ghost is. Defaults reproduce the effect as
/// built (4 words, 1.2 s, 42 percent). The October near-cursor options are not read by this
/// effect and get no rows.
/// </summary>
public static class AfterglowLineOptions
{
    public const int WordsMin = 2, WordsMax = AfterglowLine.MaxCapacity, WordsDefault = AfterglowLine.Capacity;
    public const double HoldMin = 0.5, HoldMax = 6.0, HoldDefault = AfterglowLine.FinaleHold, HoldStep = 0.1;
    public const int GhostMin = 15, GhostMax = 90, GhostDefault = 42;

    public static int ClampWords(int v) => Math.Clamp(v, WordsMin, WordsMax);

    /// <summary>Clamped to 0.5..6 s and snapped to tenths.</summary>
    public static double ClampHold(double v)
    {
        if (double.IsNaN(v)) return HoldDefault;
        return Math.Round(Math.Clamp(v, HoldMin, HoldMax) / HoldStep) * HoldStep;
    }

    public static int ClampGhost(int v) => Math.Clamp(v, GhostMin, GhostMax);

    /// <summary>Percent to the 0..1 alpha a resting ghost is drawn at.</summary>
    public static double GhostAlpha(int percent) => ClampGhost(percent) / 100.0;
}
