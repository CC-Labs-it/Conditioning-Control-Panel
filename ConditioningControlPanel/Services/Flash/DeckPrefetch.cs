namespace ConditioningControlPanel.Services;

/// <summary>
/// Super Flicker Deck: the next picture is got ready while a card rests (tier-2 report 2026-11-18:
/// cards paused side-on whenever a picture took longer than the 0.3 s from the click to the flip's
/// edge). A still is cheap to keep decoded; a GIF or a clip can hold up to 30 MB of frames, so it
/// keeps only its path (the slow part, a download or a pack decrypt, is done) and decodes on the
/// click. At spawn a card gets only a LOCAL picture ready, so a card nobody clicks never spends the
/// Scrolller daily share; after its first flip it may get a remote one ready too.
/// </summary>
public static class DeckPrefetch
{
    /// <summary>Keep the decoded frames (a still, no clip) rather than only the path.</summary>
    public static bool KeepDecoded(int frames, bool hasClip) => frames == 1 && !hasClip;
}
