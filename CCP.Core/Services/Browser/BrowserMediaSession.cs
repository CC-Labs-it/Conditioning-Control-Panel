using System;
using System.Threading;
using Serilog;

namespace ConditioningControlPanel.Services.Browser;

/// <summary>
/// WPF Services/Browser/BrowserMediaService.cs without WPF: whether web media is playing in the Home
/// browser, who started it, and whether the rest of the app should wait. The head feeds it the page's
/// ccpMedia reports and takeover messages (WPF MainWindow.Browser.cs:1089-1240).
/// This head has no InteractionQueue: <see cref="HoldsScreen"/> stands in for WPF's WebVideo slot.
/// </summary>
public sealed class BrowserMediaSession
{
    public enum MediaOwner { Autonomy, User, AvatarLink, Chaos, Remote }

    public static BrowserMediaSession Shared { get; internal set; } = new();

    // WPF BrowserMediaService.cs:72-84.
    private const double HeartbeatTimeoutSeconds = 20, LoadWindowTimeoutSeconds = 60,
        TakeoverGraceSeconds = 15, TakeoverMaxSeconds = 3600;

    private readonly object _lock = new();
    private readonly TimeProvider _time;
    private bool _isPlaying, _isTakeover, _hasEverPlayed;
    private MediaOwner? _owner;
    private double _durationSeconds;
    private double? _userCapSeconds;
    private DateTimeOffset _lastHeartbeat, _lastEnded = DateTimeOffset.MinValue, _hardStop = DateTimeOffset.MaxValue;
    private ITimer? _tick;

    public BrowserMediaSession(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Head: stop the page takeover (WPF ForceEndCore -> MainWindow.EndWebVideoTakeover).
    /// May be called off the UI thread; the head marshals.</summary>
    public Action? TearDownTakeover { get; set; }

    public bool IsPlaying { get { lock (_lock) return _isPlaying; } }
    public bool IsTakeover { get { lock (_lock) return _isTakeover; } }
    public MediaOwner? Owner { get { lock (_lock) return _owner; } }

    /// <summary>A session is open (WPF: it holds the WebVideo slot, so mandatory videos and bubble counts wait).</summary>
    public bool HoldsScreen { get { lock (_lock) return _owner != null; } }

    /// <summary>WPF ShouldDeferInterruptions: live playback with Protect Browser Video on.</summary>
    public bool ShouldDeferInterruptions => Gate(includeCoolOff: false);

    /// <summary>WPF ShouldDeferNewVideo: adds the cool-off after playback.</summary>
    public bool ShouldDeferNewVideo => Gate(includeCoolOff: true);

    private bool Gate(bool includeCoolOff)
    {
        var s = CoreSettings.Current;
        lock (_lock)
        {
            var since = _lastEnded == DateTimeOffset.MinValue ? double.PositiveInfinity
                : (_time.GetUtcNow() - _lastEnded).TotalSeconds;
            return ResolveDeferInterruptions(s.ProtectBrowserVideoPlayback, _isPlaying, since,
                s.BrowserVideoGraceSeconds, includeCoolOff);
        }
    }

    /// <summary>WPF BrowserMediaService.ResolveDeferInterruptions, unchanged.</summary>
    public static bool ResolveDeferInterruptions(bool protectEnabled, bool isPlaying, double secondsSinceEnded,
        int graceSeconds, bool includeCoolOff = true)
    {
        if (!protectEnabled) return false;
        if (isPlaying) return true;
        if (!includeCoolOff) return false;
        if (double.IsInfinity(secondsSinceEnded) || double.IsNaN(secondsSinceEnded)) return false;
        return secondsSinceEnded < Math.Max(0, graceSeconds);
    }

    /// <summary>WPF ReplaceSession: an app or user navigation hands the session over before it loads.</summary>
    public void ReplaceSession(MediaOwner owner, bool takeover)
    {
        lock (_lock)
        {
            _owner = owner;
            _isTakeover = takeover;
            _userCapSeconds = null;
            _durationSeconds = 0;
            _hasEverPlayed = false;
            _hardStop = DateTimeOffset.MaxValue;
            _lastHeartbeat = _time.GetUtcNow();
        }
        StartTick();
        Log.Information("BrowserMedia: session replaced by {Owner} (takeover={Takeover})", owner, takeover);
    }

    /// <summary>WPF OnMediaPlaying: the page reports audible playback; nobody claimed it, so the user started it.</summary>
    public void OnMediaPlaying(double durationSeconds)
    {
        lock (_lock)
        {
            _isPlaying = true;
            // WPF :248 + VideoLengthCap.BrowserTakeoverSeconds: only a video the app makes the player watch is cut.
            var max = CoreSettings.Current.VideoMaxDurationSeconds;
            if (!_hasEverPlayed && _isTakeover && max > 0 && _owner is not (MediaOwner.User or MediaOwner.AvatarLink))
                _userCapSeconds = max;
            _hasEverPlayed = true;
            _lastHeartbeat = _time.GetUtcNow();
            if (_owner == null)
            {
                _owner = MediaOwner.User;
                _isTakeover = false;
                Log.Information("BrowserMedia: user-started web media detected");
            }
            if (IsUsableDuration(durationSeconds))
            {
                _durationSeconds = durationSeconds;
                if (_isTakeover) ArmHardStop(durationSeconds);
            }
        }
        StartTick();
    }

    /// <summary>WPF OnMediaProgress: the 1 s heartbeat; re-arms the hard stop when a site auto-advances.</summary>
    public void OnMediaProgress(double positionSeconds, double durationSeconds, double playedSeconds = 0)
    {
        bool cutReached;
        lock (_lock)
        {
            if (!_isPlaying) return;
            _lastHeartbeat = _time.GetUtcNow();
            cutReached = _isTakeover && _userCapSeconds is double cap && double.IsFinite(playedSeconds) && playedSeconds >= cap;
            if (IsUsableDuration(durationSeconds) && Math.Abs(durationSeconds - _durationSeconds) > 1.0)
            {
                _durationSeconds = durationSeconds;
                if (_isTakeover) ArmHardStop(durationSeconds);
            }
        }
        if (cutReached) ForceEnd("max-video-length");
    }

    /// <summary>WPF OnMediaStopped: idempotent; starts the cool-off.</summary>
    public void OnMediaStopped(string reason)
    {
        lock (_lock)
        {
            if (!_isPlaying && _owner == null) return;
            // WPF :346: the outgoing page's pagehide is the way INTO a claimed session, not its end.
            if (reason == "navigated" && !_hasEverPlayed && _owner.HasValue)
            {
                _lastHeartbeat = _time.GetUtcNow();
                return;
            }
            _isPlaying = _isTakeover = _hasEverPlayed = false;
            _owner = null;
            _durationSeconds = 0;
            _hardStop = DateTimeOffset.MaxValue;
            _lastEnded = _time.GetUtcNow();
            _tick?.Dispose();
            _tick = null;
        }
        Log.Information("BrowserMedia: session ended ({Reason})", reason);
    }

    /// <summary>WPF ForceEnd/ForceEndCore: tear the takeover down, then the normal stopped bookkeeping.</summary>
    public void ForceEnd(string reason)
    {
        try { TearDownTakeover?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "BrowserMedia: force-ending browser takeover failed"); }
        finally { OnMediaStopped(reason); }
    }

    /// <summary>WPF OnFullscreenExited: the takeover ends, the page may keep playing for the user.</summary>
    public void OnFullscreenExited()
    {
        lock (_lock)
        {
            if (!_isTakeover) return;
            _isTakeover = false;
            _hardStop = DateTimeOffset.MaxValue;
            if (_isPlaying) _owner = MediaOwner.User;
        }
    }

    private static bool IsUsableDuration(double s) => double.IsFinite(s) && s > 0;

    // WPF ScheduleTakeoverHardStop: clip length + 15 s, at most an hour. Checked by the tick.
    private void ArmHardStop(double durationSeconds) =>
        _hardStop = _time.GetUtcNow() + TimeSpan.FromSeconds(Math.Min(durationSeconds + TakeoverGraceSeconds, TakeoverMaxSeconds));

    // shortcut: one 1 s tick checks heartbeat and hard stop (WPF: 5 s heartbeat timer plus a Task.Delay per clip).
    private void StartTick()
    {
        lock (_lock)
            _tick ??= _time.CreateTimer(_ => CoreDispatch.Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>WPF OnHeartbeatTick plus the hard-stop timer.</summary>
    internal void Tick()
    {
        string? reason = null;
        bool takeover;
        lock (_lock)
        {
            if (_owner == null && !_isPlaying) { _tick?.Dispose(); _tick = null; return; }
            var now = _time.GetUtcNow();
            takeover = _isTakeover;
            if (takeover && now >= _hardStop) reason = "takeover-overran";
            else if ((now - _lastHeartbeat).TotalSeconds >= (_hasEverPlayed ? HeartbeatTimeoutSeconds : LoadWindowTimeoutSeconds))
                reason = "heartbeat-lost";
        }
        if (reason == null) return;
        Log.Warning("BrowserMedia: ending session ({Reason})", reason);
        if (takeover) ForceEnd(reason);
        else OnMediaStopped(reason);
    }

    /// <summary>Tests: whether the heartbeat tick is armed.</summary>
    internal bool TickArmed { get { lock (_lock) return _tick != null; } }
}
