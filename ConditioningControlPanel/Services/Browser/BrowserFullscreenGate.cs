namespace ConditioningControlPanel.Services.Browser;

/// <summary>
/// Moves the embedded browser's fullscreen switch OUT of the WebView2 event that asked for it
/// (ccp-bugs #1565, #1570, #1585, #1589, #1596).
///
/// ContainsFullScreenElementChanged and WebMessageReceived arrive as WebView2 COM callbacks on the
/// UI thread. The old handler ran the whole switch inside that callback: it reparented the WebView2,
/// Show()ed a topmost window and pumped the dispatcher at Render priority, so queued work (a chat
/// scroll calling UpdateLayout, a SizeToContent window resizing) ran nested inside WebView2 and
/// WPF's synchronous resize render waited on the render thread from there. Every hang report has
/// the UI thread stuck in HwndTarget.OnResize -> MediaContext.CompleteRender under that callback.
///
/// The callbacks now only record the wanted state here and post one drain at Normal priority. The
/// latest wish wins, so a page that flips in and out quickly costs one transition, not two
/// half-built ones. The transition token lets a deferred step (the maximize after Show) tell that
/// a newer enter or exit has started and stand down. UI thread only, so no locking.
/// </summary>
internal sealed class BrowserFullscreenGate
{
    private bool? _pending;
    private bool _drainQueued;
    private int _transition;

    /// <summary>Records the latest wanted state. True when the caller must post exactly one drain.</summary>
    internal bool Request(bool fullscreen)
    {
        _pending = fullscreen;
        if (_drainQueued) return false;
        _drainQueued = true;
        return true;
    }

    /// <summary>The drain: hands back the latest wish (null when it was cleared) and frees the slot.</summary>
    internal bool? Take()
    {
        _drainQueued = false;
        var wish = _pending;
        _pending = null;
        return wish;
    }

    /// <summary>A synchronous exit (Esc, panic, session teardown) drops a queued enter, so a late
    /// page event can never reopen the topmost host behind the user's back. It also ends a
    /// takeover's hold and arm: the app no longer wants that video fullscreen.</summary>
    internal void Clear()
    {
        _pending = null;
        _takeoverArmedUntil = long.MinValue;
        _holdUntil = long.MinValue;
        _takeoverLive = false;
    }

    // ---- app-started takeover (ccp-bugs #1649) ---------------------------------------------
    //
    // Since the switch left the WebView2 callback, Chromium finishes the page's HTML5 fullscreen
    // inside the small panel cell first and the reparent into the borderless host comes after.
    // The reparent can drop the page's fullscreen for a moment, and the takeover script answers
    // that with fsExit (Chromium may also report false, and ccp_exit_fullscreen follows once the
    // forced flag is set). Latest wish wins, so the enter was undone and the video kept playing
    // in the embedded browser. For a takeover the app started, those page exits are held for a
    // short time around the enter; the host stays and the page is asked back into fullscreen.
    // Esc, F11, panic and session teardown never pass through here (they exit directly).

    /// <summary>How long after the takeover script is injected its fullscreen still counts as
    /// app-started (the script waits up to 10 s for the player, then for playback).</summary>
    internal const long TakeoverArmMs = 20_000;

    /// <summary>How long page exits are held after a takeover's enter wish or applied enter.</summary>
    internal const long TakeoverHoldMs = 2_500;

    /// <summary>How long after the takeover script says it is asking for fullscreen the enter
    /// still counts as app-started (the enter follows the ask at once).</summary>
    internal const long TakeoverAskMs = 5_000;

    private long _takeoverArmedUntil = long.MinValue;
    private long _holdUntil = long.MinValue;
    private bool _takeoverLive;

    /// <summary>The next enter belongs to an app-started takeover (its script was just injected).</summary>
    internal void ArmTakeover(long nowMs)
    {
        _takeoverArmedUntil = nowMs + TakeoverArmMs;
        _takeoverLive = true;
    }

    /// <summary>
    /// The takeover script is asking for fullscreen right now. Its ask has no deadline: a slow
    /// page, or a player the user had to start by hand (the script then waits for playback), can
    /// come long after the arm ran out, and that enter was undone by the reparent exactly as
    /// before the hold existed. The ask arms again, but only for a takeover that was injected
    /// and not ended since: after Esc, panic or teardown (Clear) a late ask arms nothing.
    /// </summary>
    internal bool NoteTakeoverAsk(long nowMs)
    {
        if (!_takeoverLive) return false;
        if (nowMs + TakeoverAskMs > _takeoverArmedUntil) _takeoverArmedUntil = nowMs + TakeoverAskMs;
        return true;
    }

    /// <summary>A takeover asked to enter: hold page exits until the enter is applied.</summary>
    internal void NoteEnterWish(long nowMs)
    {
        if (nowMs <= _takeoverArmedUntil) _holdUntil = nowMs + TakeoverHoldMs;
    }

    /// <summary>The enter was applied. True when it is a takeover's: the hold restarts from now
    /// (the reparent, Show and maximize all come after this) and the arm is used up.</summary>
    internal bool NoteEnterApplied(long nowMs)
    {
        if (nowMs > _takeoverArmedUntil) return false;
        _takeoverArmedUntil = long.MinValue;
        _takeoverLive = false; // one enter per takeover: a later ask from the page arms nothing
        _holdUntil = nowMs + TakeoverHoldMs;
        return true;
    }

    /// <summary>True while a page exit (fsExit, ccp_exit_fullscreen, Chromium false) must be held.</summary>
    internal bool HoldsExit(long nowMs) => nowMs <= _holdUntil;

    /// <summary>Only a change is applied: entering while in, or leaving while out, does nothing.</summary>
    internal static bool ShouldApply(bool? wish, bool isFullscreen) => wish.HasValue && wish.Value != isFullscreen;

    /// <summary>Starts a new enter or exit; deferred steps of older ones see a stale token.</summary>
    internal int BeginTransition() => ++_transition;

    internal bool IsCurrent(int token) => token == _transition;
}
