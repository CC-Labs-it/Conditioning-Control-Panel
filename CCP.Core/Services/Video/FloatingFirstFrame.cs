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
        if (sincePlayingMs != null) return Verdict.Wait;
        return sinceReadyMs >= ReadyBudgetMs ? Verdict.Skip : Verdict.Wait;
    }
}
