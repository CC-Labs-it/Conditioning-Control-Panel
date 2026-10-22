namespace ConditioningControlPanel.Services;

/// <summary>
/// The per-tick clip decision for one online GIF flash, pulled out of the heartbeat so it can be
/// tested without LibVLC. A flash shows its poster until it holds a player; one refused at the
/// player cap asks again whenever a slot is free (the poster upgrades to the clip); one that is
/// leaving lends its slot; one that lost its slot that way and is back on screen (a linger)
/// starts over as refused-at-cap.
/// </summary>
internal static class FlashClipUpgrade
{
    /// <summary>A leaving flash's player may be taken by the next flash that needs a slot.</summary>
    internal static bool Evictable(bool showing) => !showing;

    /// <summary>The player was evicted while leaving, and the flash is showing again.</summary>
    internal static bool Reclaim(bool hasPlayer, bool playerDisposed, bool showing)
        => hasPlayer && playerDisposed && showing;

    internal static bool ShouldTryStart(bool hasClipPath, bool hasSurface, bool showing, bool hasPlayer,
        bool attempted, bool refusedAtCap, bool canStart)
        => hasClipPath && hasSurface && showing && !hasPlayer && !attempted && (!refusedAtCap || canStart);

    /// <summary>After a start: refused at the cap = try again later; anything else is final.</summary>
    internal static (bool Attempted, bool RefusedAtCap) AfterTry(bool started, bool atCap)
    {
        bool refused = !started && atCap;
        return (!refused, refused);
    }
}
