namespace ConditioningControlPanel.Services.Video;

/// <summary>Floating reveal admission, shared with hosts that report painted frames or played time.</summary>
public static class FloatingFirstFrame
{
    public const int ReadyBudgetMs = 4000;
    public const int PlayingGraceMs = 1000;
    public const int AdvanceMs = 400;
    public enum Verdict { Wait, Reveal, Skip }

    public static bool Painted(int frames, long firstMediaMs, long mediaMs, bool visible) =>
        visible && frames >= 3 && mediaMs > firstMediaMs;

    public static Verdict Decide(bool painted, long? sincePlayingMs, long advancedMs, long? sinceReadyMs)
    {
        if (painted || sincePlayingMs >= PlayingGraceMs && advancedMs >= AdvanceMs) return Verdict.Reveal;
        // Lift the veil at the budget even if a playing output cannot report frame proof.
        if (sincePlayingMs != null)
            return (sinceReadyMs ?? sincePlayingMs) >= ReadyBudgetMs ? Verdict.Reveal : Verdict.Wait;
        return sinceReadyMs >= ReadyBudgetMs ? Verdict.Skip : Verdict.Wait;
    }
}
