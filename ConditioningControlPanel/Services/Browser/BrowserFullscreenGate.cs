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
    /// page event can never reopen the topmost host behind the user's back.</summary>
    internal void Clear() => _pending = null;

    /// <summary>Only a change is applied: entering while in, or leaving while out, does nothing.</summary>
    internal static bool ShouldApply(bool? wish, bool isFullscreen) => wish.HasValue && wish.Value != isFullscreen;

    /// <summary>Starts a new enter or exit; deferred steps of older ones see a stale token.</summary>
    internal int BeginTransition() => ++_transition;

    internal bool IsCurrent(int token) => token == _transition;
}
