using System;

namespace ConditioningControlPanel.Services.Video;

/// <summary>
/// "Loop short videos until the minimum time" (AppSettings.MandatoryVideoLoopShortClips, off by
/// default). With it off, the minimum (AppSettings.VideoMinDurationSeconds) stays a queue filter
/// that skips shorter clips. With it on, a shorter clip is kept and replayed in place until the
/// minimum has played, then it ends once, the way any video ends: one attention verdict, one
/// reward or penalty, never one per pass.
/// <para>
/// The last pass always plays to the clip's end, so a run lasts the minimum plus at most one pass.
/// LibVLC repeats through the media's <c>:input-repeat</c> option (no EndReached between passes);
/// the browser page replays on its own <c>ended</c> (player.js, same rule as <see cref="ReplayAfterPass"/>).
/// </para>
/// </summary>
public static class VideoMinLoop
{
    /// <summary>LibVLC's :input-repeat ceiling; a pathological 0.01 s clip stops there.</summary>
    public const int MaxPasses = 65535;

    /// <summary>
    /// Whether the loop is in play for a full-screen mandatory video. Floating windows, Chaos
    /// segments, Deeper enhancements and credited (Scrolller) clips keep today's behaviour.
    /// </summary>
    public static bool Applies(bool enabled, int minSeconds, bool floating = false, bool segment = false,
        bool enhanced = false, bool credited = false)
        => enabled && minSeconds > 0 && !floating && !segment && !enhanced && !credited;

    /// <summary>The queue filter drops a clip shorter than the minimum only while the loop is off.</summary>
    public static bool FilterDropsShortClip(bool loopEnabled, int minSeconds, bool floating, double clipSeconds)
        => minSeconds > 0 && clipSeconds < minSeconds && !Applies(loopEnabled, minSeconds, floating);

    /// <summary>
    /// How many times the clip plays in total: 1 when the loop does not apply, the length is unknown,
    /// or the clip already reaches the minimum; otherwise just enough whole passes to reach it.
    /// </summary>
    public static int Passes(bool applies, int minSeconds, long clipMs)
    {
        if (!applies || minSeconds <= 0 || clipMs <= 0) return 1;
        long minMs = minSeconds * 1000L;
        if (clipMs >= minMs) return 1;
        long passes = (minMs + clipMs - 1) / clipMs;
        return (int)Math.Min(passes, MaxPasses);
    }

    /// <summary>LibVLC <c>:input-repeat</c> value: extra passes after the first (0 = no option).</summary>
    public static int RepeatCount(bool applies, int minSeconds, long clipMs)
        => Passes(applies, minSeconds, clipMs) - 1;

    /// <summary>What the browser page is told to loop up to, in ms (0 = never loop).</summary>
    public static long PageLoopUntilMs(bool applies, int minSeconds)
        => applies && minSeconds > 0 ? minSeconds * 1000L : 0;

    /// <summary>
    /// The page's rule at the end of a pass: replay while less than the target has played.
    /// Mirrored in Resources/web/player/player.js ('ended').
    /// </summary>
    public static bool ReplayAfterPass(long loopUntilMs, long playedMs)
        => loopUntilMs > 0 && playedMs < loopUntilMs;

    /// <summary>Seconds of clip the safety timer must allow for: one pass times the passes.</summary>
    public static double SafetySeconds(double clipSeconds, int passes)
        => clipSeconds * Math.Max(1, passes);
}
