using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// The For You stand-down (#1073), one copy for every scheduler that has to wait out the feed: WPF
/// VideoService.DeferTriggerPastFeed / RunWhenFeedClear (VideoService.cs:2019, :2093) and
/// BubbleCountService.DeferGamePastFeed (BubbleCountService.cs:143), which are the same code twice.
/// One pending replay at a time (a feed session that swallows several triggers replays one), held at
/// most <see cref="MaxWait"/> from the FIRST defer of a chain, polled every <see cref="Poll"/> because
/// there is no "feed left the screen" event. The replay re-asserts its own preconditions and may
/// defer again, inheriting the ceiling.
/// </summary>
internal sealed class FypFeedDefer
{
    /// <summary>WPF FeedDeferMaxWait: a video or game that lands minutes after it was earned is a surprise.</summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(90);
    /// <summary>WPF RunWhenFeedClear's 500 ms DispatcherTimer.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(500);

    /// <summary>The head says whether the feed is on the user's screen (WPF FeedOwnsTheScreen:
    /// IsActive and not ghosted). Unseeded: never, so nothing stands down.</summary>
    public static volatile Func<bool>? FeedOnScreen;

    public static bool FeedOwnsTheScreen
    {
        get { try { return FeedOnScreen?.Invoke() == true; } catch { return false; } }
    }

    private readonly TimeProvider _time;
    private readonly string _tag;
    private ITimer? _poll;
    private DateTimeOffset _deadline = DateTimeOffset.MinValue;

    public FypFeedDefer(TimeProvider time, string tag) { _time = time; _tag = tag; }

    /// <summary>A replay is parked.</summary>
    public bool IsPending => _poll != null;

    /// <summary>Hold <paramref name="replay"/> until the feed leaves the screen. <paramref name="gaveUp"/>
    /// runs on every path where the replay will not (coalesced excepted: the pending one still will).</summary>
    public void Defer(Action replay, Action? gaveUp = null)
    {
        if (_poll != null)
        {
            Log.Information("{Tag}: For You replay already pending - trigger coalesced", _tag);
            return;
        }
        var now = _time.GetUtcNow();
        if (_deadline == DateTimeOffset.MinValue) _deadline = now + MaxWait;
        if (_deadline <= now)
        {
            Log.Information("{Tag}: For You replay dropped - past the {Sec:F0}s defer ceiling", _tag, MaxWait.TotalSeconds);
            _deadline = DateTimeOffset.MinValue;
            Safe(gaveUp);
            return;
        }
        Arm(replay, gaveUp);
    }

    /// <summary>WPF Stop's latch reset: a defer that outlived the service must not coalesce every later trigger.</summary>
    public void Reset()
    {
        var t = _poll;
        _poll = null;
        t?.Dispose();
        _deadline = DateTimeOffset.MinValue;
    }

    private void Arm(Action replay, Action? gaveUp)
    {
        ITimer? created = null;
        created = _time.CreateTimer(_ => { var mine = created; CoreDispatch.Post(() => Tick(mine, replay, gaveUp)); },
            null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _poll = created;
        created.Change(Poll, Timeout.InfiniteTimeSpan);
    }

    private void Tick(ITimer? mine, Action replay, Action? gaveUp)
    {
        if (mine == null || !ReferenceEquals(mine, _poll)) return;   // reset, or a newer chain
        _poll = null;
        mine.Dispose();
        var onScreen = FeedOwnsTheScreen;
        if (onScreen && _time.GetUtcNow() < _deadline) { Arm(replay, gaveUp); return; }   // still on screen, still in the window
        if (onScreen)
        {
            Log.Information("{Tag}: deferred trigger expired - For You feed still on screen after {Sec:F0}s", _tag, MaxWait.TotalSeconds);
            _deadline = DateTimeOffset.MinValue;
            Safe(gaveUp);
            return;
        }
        Log.Information("{Tag}: For You feed left the screen - firing deferred trigger", _tag);
        try { replay(); }
        catch (Exception ex) { Log.Debug("{Tag}: deferred replay failed: {E}", _tag, ex.Message); }
        // The replay may have parked itself again (the feed came back): only end the chain when it really ended.
        if (_poll == null) _deadline = DateTimeOffset.MinValue;
    }

    private static void Safe(Action? a)
    {
        try { a?.Invoke(); } catch (Exception ex) { Log.Debug("FypFeedDefer: give-up callback failed: {E}", ex.Message); }
    }
}
