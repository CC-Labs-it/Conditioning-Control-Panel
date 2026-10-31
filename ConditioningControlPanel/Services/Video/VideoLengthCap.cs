using System;
using ConditioningControlPanel.Services.Browser;

namespace ConditioningControlPanel.Services.Video;

/// <summary>
/// The player's "longest video" limit (AppSettings.VideoMaxDurationSeconds, 0 = off) as a rule
/// every video surface can ask, so none of them has to remember it on its own (ccp-bugs #1438:
/// a companion takeover played a 25 minute web video past a 20 minute limit, because the browser
/// takeover never read the setting; the floating windows did not either).
/// <para>
/// It is a cut, not a filter: it never depends on a clip's reported length, so a web or pack
/// video with no duration metadata stops at the limit like any other.
/// </para>
/// </summary>
public static class VideoLengthCap
{
    /// <summary>Whether a limit is set at all. 0 or less means the player set none.</summary>
    public static bool IsSet(int maxSeconds) => maxSeconds > 0;

    /// <summary>Uniform millisecond offset leaving a whole capped slice. Unknown lengths start at 0.</summary>
    public static long StartOffsetMs(int maxSeconds, long durationMs, Random random)
        => VideoLengthCapRules.StartOffsetMs(maxSeconds, durationMs, random);

    /// <summary>
    /// A window that knows how much of its clip has played (floating windows): true once it has
    /// played the limit. Played time, not wall clock, so a grace pause does not eat the allowance.
    /// </summary>
    public static bool Reached(int maxSeconds, long playedMs)
        => IsSet(maxSeconds) && playedMs >= maxSeconds * 1000L;

    /// <summary>
    /// A video the app put on screen through the embedded browser (companion takeover, Chaos
    /// link, remote controller): seconds from first playback until it is cut, or null for none.
    /// A video the player picked themselves (their own browsing, a link they clicked in a speech
    /// bubble) is never cut: the limit is about what the app makes them watch.
    /// </summary>
    public static double? BrowserTakeoverSeconds(int maxSeconds, BrowserMediaService.MediaOwner? owner)
    {
        if (!IsSet(maxSeconds) || owner == null) return null;
        return owner is BrowserMediaService.MediaOwner.User or BrowserMediaService.MediaOwner.AvatarLink
            ? null
            : maxSeconds;
    }
}
