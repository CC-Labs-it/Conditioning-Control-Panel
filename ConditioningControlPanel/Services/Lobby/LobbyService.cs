using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>
/// THE LOBBY (App.Lobby). Fans out to the existing open-table wires (Glassroad races
/// <c>GET /v2/race/open</c> via <see cref="RaceOpenApi"/>, chess
/// <c>GET /v2/pbp/lobby</c>, Goon <c>POST /v2/goon/open</c> via <see cref="GoonOpenTables"/>,
/// Remote <c>GET /v2/directory/list</c> via <see cref="AvailableSubjectsService"/>), folds in
/// the friends list, and hands one <see cref="LobbySnapshot"/> to the page and the launcher.
///
/// Polls ONLY while somebody watches. <see cref="Watch"/> (the Lobby page, the launcher
/// dropdown) polls every 10 s, doubling to 60 s after a failed round; <see cref="WatchBadge"/>
/// (the rail badge) alone polls every 2 minutes. Signed out it asks nobody. A tier refusal on
/// the race list is remembered (<see cref="LobbyPollPlan.TierLockHold"/>) instead of re-asked
/// every round.
/// </summary>
public sealed class LobbyService : IDisposable
{
    private readonly object _gate = new();
    private int _watchers;
    private int _badgeWatchers;
    private DispatcherTimer? _timer;
    private TimeSpan _interval = LobbyPollPlan.Base;
    private Task? _inflight;
    private bool _disposed;
    private long _lastRoundMs;
    private RaceOpenReply? _raceLocked;
    private long _raceLockedUntilMs;

    /// <summary>Seams. The real wires by default.</summary>
    internal Func<CancellationToken, Task<PbpLobbyReply>> FetchChess { get; set; } = ct => new PbpLobbyApi().FetchAsync(ct);
    internal Func<Task<OpenTablesReply>> FetchGoon { get; set; } = () => GoonOpenTables.RefreshAsync();
    internal Func<Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)>> FetchRemote { get; set; } = FetchRemoteFromDirectory;
    internal Func<CancellationToken, Task<RaceOpenReply>> FetchRace { get; set; } = ct => new RaceOpenApi().FetchAsync(ct);
    internal Func<IReadOnlyList<Friend>> ReadFriends { get; set; } = () => App.Friends?.Snapshot.Friends ?? Array.Empty<Friend>();
    internal Func<bool> SignedIn { get; set; } = () => BackRoomApi.AppIdentity() != null;
    internal Func<long> NowMs { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public LobbySnapshot Snapshot { get; private set; } = LobbySnapshot.Empty;

    /// <summary>Raised on whatever thread the round finished; marshal before touching UI.</summary>
    public event Action<LobbySnapshot>? Changed;

    public TimeSpan CurrentInterval => _interval;

    /// <summary>False after a round where neither chess nor the Remote directory answered.</summary>
    public bool LastRoundOk { get; private set; } = true;
    public bool IsWatched { get { lock (_gate) return _watchers + _badgeWatchers > 0; } }

    /// <summary>True when only the rail badge watches: the slow pace.</summary>
    public bool BadgeOnly { get { lock (_gate) return _watchers == 0 && _badgeWatchers > 0; } }

    /// <summary>Start watching at the full pace (the Lobby page, the launcher drop). Dispose the
    /// lease to stop; the last lease out stops the poll. Call on the UI thread (the timer is a
    /// DispatcherTimer).</summary>
    public IDisposable Watch()
    {
        bool firstFull;
        lock (_gate)
        {
            firstFull = ++_watchers == 1;
            // Somebody is looking at the race row now: ask the server again.
            if (firstFull) { _raceLocked = null; _raceLockedUntilMs = 0; }
        }
        if (firstFull) ApplyPace(LobbyPollPlan.Base);
        _ = RefreshAsync();
        return new Lease(this, badge: false);
    }

    /// <summary>Start watching for the rail badge only: one round every
    /// <see cref="LobbyPollPlan.Badge"/>, and no round on taking the lease when the last one
    /// is fresher than that. A full <see cref="Watch"/> lease beside it wins.</summary>
    public IDisposable WatchBadge()
    {
        bool startTimer, roundNow;
        lock (_gate)
        {
            startTimer = ++_badgeWatchers == 1 && _watchers == 0;
            roundNow = _watchers == 0 && LobbyPollPlan.BadgeWantsRoundNow(NowMs(), _lastRoundMs);
        }
        if (startTimer) ApplyPace(LobbyPollPlan.Badge);
        if (roundNow) _ = RefreshAsync();
        return new Lease(this, badge: true);
    }

    private void ApplyPace(TimeSpan interval)
    {
        _interval = interval;
        _timer ??= new DispatcherTimer(DispatcherPriority.Normal);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Interval = _interval;
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void Unwatch(bool badge)
    {
        bool stop, slow;
        lock (_gate)
        {
            if (badge) { if (_badgeWatchers > 0) _badgeWatchers--; }
            else if (_watchers > 0) _watchers--;
            stop = _watchers == 0 && _badgeWatchers == 0;
            slow = !badge && _watchers == 0 && _badgeWatchers > 0;
        }
        if (stop) _timer?.Stop();
        else if (slow) ApplyPace(LobbyPollPlan.Badge);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsWatched) { _timer?.Stop(); return; }
        _ = RefreshAsync();
    }

    /// <summary>One round. Joins a round already in flight.</summary>
    public Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_inflight != null) return _inflight;
            var run = RunAsync();
            _inflight = run.IsCompleted ? null : run;
            return run;
        }
    }

    private async Task RunAsync()
    {
        bool ok = true;
        LobbySnapshot snap;
        try
        {
            if (!SignedIn())
            {
                snap = LobbySnapshot.Empty;
            }
            else
            {
                var chessTask = SafeChess();
                var goonTask = SafeGoon();
                var remoteTask = SafeRemote();
                var raceTask = SafeRace();
                await Task.WhenAll(chessTask, goonTask, remoteTask, raceTask).ConfigureAwait(false);
                var chess = chessTask.Result;
                var remote = remoteTask.Result;
                var race = raceTask.Result;
                // Goon's wire cannot tell "no tables" from "no answer", so it never counts as a fault.
                ok = chess.Ok || remote.Ok || race.Ok;
                IReadOnlyList<Friend> friends;
                try { friends = ReadFriends(); } catch { friends = Array.Empty<Friend>(); }
                snap = LobbyMerge.Build(chess, goonTask.Result, remote.Seats, friends, true, NowMs(), race);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Lobby: round failed: {E}", ex.Message);
            snap = Snapshot;
            ok = false;
        }
        finally
        {
            lock (_gate) { _inflight = null; _lastRoundMs = NowMs(); }
        }

        LastRoundOk = ok;
        _interval = LobbyPollPlan.Next(_interval, ok, BadgeOnly);
        try
        {
            var t = _timer;
            if (t != null) t.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => { if (t.IsEnabled) t.Interval = _interval; }));
        }
        catch { }

        Snapshot = snap;
        try { Changed?.Invoke(snap); } catch (Exception ex) { App.Logger?.Debug("Lobby.Changed: {E}", ex.Message); }
    }

    private async Task<PbpLobbyReply> SafeChess()
    {
        try { return await FetchChess(CancellationToken.None).ConfigureAwait(false); }
        catch { return PbpLobbyReply.Empty; }
    }

    private async Task<OpenTablesReply> SafeGoon()
    {
        try { return await FetchGoon().ConfigureAwait(false); }
        catch { return OpenTablesReply.Empty; }
    }

    /// <summary>The race list, unless the server refused this account's tier a moment ago:
    /// then the remembered refusal (it paints the Prime lock row) and no request.</summary>
    private async Task<RaceOpenReply> SafeRace()
    {
        long now = NowMs();
        RaceOpenReply? held;
        lock (_gate) held = now < _raceLockedUntilMs ? _raceLocked : null;
        if (held != null) return held;
        try
        {
            var reply = await FetchRace(CancellationToken.None).ConfigureAwait(false);
            lock (_gate)
            {
                if (reply.TierLocked) { _raceLocked = reply; _raceLockedUntilMs = now + (long)LobbyPollPlan.TierLockHold.TotalMilliseconds; }
                else { _raceLocked = null; _raceLockedUntilMs = 0; }
            }
            return reply;
        }
        catch { return RaceOpenReply.Empty; }
    }

    private async Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)> SafeRemote()
    {
        try { return await FetchRemote().ConfigureAwait(false); }
        catch { return (Array.Empty<RemoteSeat>(), false); }
    }

    /// <summary>The Remote directory, through the service that already owns it (and its claim
    /// flow). Its collection lives on the UI thread, so the copy is taken there.</summary>
    private static async Task<(IReadOnlyList<RemoteSeat>, bool)> FetchRemoteFromDirectory()
    {
        var dir = App.AvailableSubjects;
        if (dir == null) return (Array.Empty<RemoteSeat>(), false);
        await dir.RefreshAsync().ConfigureAwait(false);
        var app = Application.Current;
        if (app == null) return (Array.Empty<RemoteSeat>(), false);
        return app.Dispatcher.Invoke(() =>
        {
            var seats = dir.Entries
                .Select(e => new RemoteSeat(e.UnifiedId, e.DisplayName, e.Level, e.Tier, e.Tags.ToList(), e.Claimed))
                .ToList();
            return ((IReadOnlyList<RemoteSeat>)seats, !dir.HasError);
        });
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _watchers = 0; _badgeWatchers = 0; }
        try { _timer?.Stop(); } catch { }
    }

    private sealed class Lease : IDisposable
    {
        private LobbyService? _owner;
        private readonly bool _badge;
        public Lease(LobbyService owner, bool badge) { _owner = owner; _badge = badge; }
        public void Dispose() { Interlocked.Exchange(ref _owner, null)?.Unwatch(_badge); }
    }
}
