namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// The normal subliminal cards beside Afterglow, the Subliminals V2 (owner, 2026-11-17): while
/// the V2 really runs, the ambient cards rest and each word comes up as its Afterglow ghost
/// instead. The player's "Also show the normal subliminals" switch brings the cards back, so
/// both run. A weekly try never rests them (a taste, not a swap), and with no Afterglow running
/// (switched off, locked, base off, no compositor) nothing changes. Only the ambient scheduler
/// asks: a point-fired card (remote, Deeper, keyword triggers, Autonomy) always shows. Pure.
/// </summary>
public static class AfterglowBase
{
    /// <summary>The switch's default: off, so turning the V2 on rests the normal cards.</summary>
    public const bool AlsoBaseDefault = false;

    /// <summary>Should the ambient subliminal card rest this time.</summary>
    public static bool CardRests(bool afterglowRuns, bool trying, bool alsoBase)
        => afterglowRuns && !trying && !alsoBase;
}
