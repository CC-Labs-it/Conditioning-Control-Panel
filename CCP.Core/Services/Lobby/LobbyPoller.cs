using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using Serilog;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>
/// WPF <c>Services/Lobby/LobbyService.cs</c> (App.Lobby) for heads off Windows: fans out to chess
/// (<see cref="PbpLobbyApi"/>), Goon (<see cref="GoonOpenTables"/>) and the Remote directory
/// (<see cref="RemoteDirectoryApi"/>), folds in friends and hands one <see cref="LobbySnapshot"/>
/// on. Polls ONLY while leased (<see cref="Watch"/>), every 10 s, backing off to 60 s after a
/// failed round (<see cref="LobbyPollPlan"/>). Signed out it asks nobody. The head's
/// <see cref="IUiTimer"/> replaces WPF's DispatcherTimer. Named apart from the WPF type (P25).
/// </summary>
public sealed class LobbyPoller : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IUiTimer> _makeTimer;
    private int _watchers;
    private IUiTimer? _timer;
    private TimeSpan _interval = LobbyPollPlan.Base;
    private Task? _inflight;
    private bool _disposed;

    public LobbyPoller(Func<IUiTimer> makeTimer) => _makeTimer = makeTimer;

    /// <summary>Seams. The real wires by default.</summary>
    internal Func<CancellationToken, Task<PbpLobbyReply>> FetchChess { get; set; } = ct => new PbpLobbyApi().FetchAsync(ct);
    internal Func<Task<OpenTablesReply>> FetchGoon { get; set; } = () => GoonOpenTables.RefreshAsync();
    internal Func<Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)>> FetchRemote { get; set; } = () => new RemoteDirectoryApi().ListAsync();
    public Func<IReadOnlyList<Friend>> ReadFriends { get; set; } = () => Array.Empty<Friend>();
    internal Func<bool> SignedIn { get; set; } = () => LobbyWire.DefaultIdentity() != null;
    internal Func<long> NowMs { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public LobbySnapshot Snapshot { get; private set; } = LobbySnapshot.Empty;

    /// <summary>Raised on whatever thread the round finished; marshal before touching UI.</summary>
    public event Action<LobbySnapshot>? Changed;

    public TimeSpan CurrentInterval => _interval;

    /// <summary>False after a round where neither chess nor the Remote directory answered.</summary>
    public bool LastRoundOk { get; private set; } = true;
    public bool IsWatched { get { lock (_gate) return _watchers > 0; } }

    /// <summary>Start watching; dispose the lease to stop. The last lease out stops the poll.
    /// Call on the UI thread (the timer is the head's UI timer).</summary>
    public IDisposable Watch()
    {
        bool first;
        lock (_gate) { first = ++_watchers == 1; }
        if (first)
        {
            _interval = LobbyPollPlan.Base;
            if (_timer == null) { _timer = _makeTimer(); _timer.Tick += OnTick; }
            _timer.Interval = _interval;
            _timer.Start();
        }
        _ = RefreshAsync();
        return new Lease(this);
    }

    private void Unwatch()
    {
        bool last;
        lock (_gate) { last = _watchers > 0 && --_watchers == 0; }
        if (last) _timer?.Stop();
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
                var chessTask = Safe(() => FetchChess(CancellationToken.None), PbpLobbyReply.Empty);
                var goonTask = Safe(FetchGoon, OpenTablesReply.Empty);
                var remoteTask = Safe<(IReadOnlyList<RemoteSeat> Seats, bool Ok)>(FetchRemote, (Array.Empty<RemoteSeat>(), false));
                await Task.WhenAll(chessTask, goonTask, remoteTask).ConfigureAwait(false);
                var remote = remoteTask.Result;
                // Goon's wire cannot tell "no tables" from "no answer", so it never counts as a fault.
                ok = chessTask.Result.Ok || remote.Ok;
                IReadOnlyList<Friend> friends;
                try { friends = ReadFriends(); } catch { friends = Array.Empty<Friend>(); }
                snap = LobbyMerge.Build(chessTask.Result, goonTask.Result, remote.Seats, friends, true, NowMs());
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Lobby: round failed: {E}", ex.Message);
            snap = Snapshot;
            ok = false;
        }
        finally
        {
            lock (_gate) { _inflight = null; }
        }

        LastRoundOk = ok;
        _interval = LobbyPollPlan.Next(_interval, ok);
        var t = _timer;
        if (t != null)
        {
            var next = _interval;
            try { t.OnUiThread(() => { if (IsWatched) t.Interval = next; }); } catch { }
        }

        Snapshot = snap;
        try { Changed?.Invoke(snap); } catch (Exception ex) { Log.Debug("Lobby.Changed: {E}", ex.Message); }
    }

    private static async Task<T> Safe<T>(Func<Task<T>> fetch, T fallback)
    {
        try { return await fetch().ConfigureAwait(false); }
        catch { return fallback; }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _watchers = 0; }
        try { _timer?.Stop(); } catch { }
    }

    private sealed class Lease : IDisposable
    {
        private LobbyPoller? _owner;
        public Lease(LobbyPoller owner) => _owner = owner;
        public void Dispose() { Interlocked.Exchange(ref _owner, null)?.Unwatch(); }
    }
}
