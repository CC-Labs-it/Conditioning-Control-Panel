using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>WPF LobbyService's watched discovery loop. Calls and timer ticks belong to the
/// head's UI thread, through the existing friends timer seam.</summary>
public sealed class LobbyService : IDisposable
{
    private readonly IUiTimer _timer;
    private int _watchers;
    private bool _disposed;
    private Task? _inflight;
    internal Func<CancellationToken, Task<PbpLobbyReply>> FetchChess = ct => new PbpLobbyApi().FetchAsync(ct);
    internal Func<CancellationToken, Task<OpenTablesReply>> FetchGoon = ct => GoonOpenTables.RefreshAsync(ct);
    internal Func<CancellationToken, Task<RaceOpenReply>> FetchRace = ct => new RaceOpenApi(identity: LobbyWire.DefaultIdentity, baseUrl: LobbyWire.DefaultBaseUrl() ?? "").FetchAsync(ct);
    internal Func<CancellationToken, Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)>> FetchRemote = ct => new RemoteLobbyApi().FetchAsync(ct);
    internal Func<string, Task<(string? Url, bool Refresh)>> ClaimRemote = id => new RemoteLobbyApi().TryClaimAsync(id);
    private IReadOnlyList<RemoteSeat> _remoteSeats = Array.Empty<RemoteSeat>();
    internal Func<bool> SignedIn = () => LobbyWire.DefaultIdentity() != null;
    internal Func<IReadOnlyList<Friend>> ReadFriends = () => Array.Empty<Friend>();
    public LobbySnapshot Snapshot { get; private set; } = LobbySnapshot.Empty;
    public bool LastRoundOk { get; private set; } = true;
    public bool IsWatched => _watchers > 0;
    public TimeSpan CurrentInterval { get; private set; } = LobbyPollPlan.Base;
    public event Action<LobbySnapshot>? Changed;

    public LobbyService(IUiTimer timer) { _timer = timer; _timer.Tick += OnTick; }

    // WPF Services/Lobby/LobbyService.cs:55-82: one timer, immediate refresh per lease.
    public IDisposable Watch()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (++_watchers == 1)
        {
            CurrentInterval = _timer.Interval = LobbyPollPlan.Base;
            _timer.Start();
        }
        _ = RefreshAsync();
        return new Lease(this);
    }

    private void OnTick(object? sender, EventArgs e) { if (IsWatched) _ = RefreshAsync(); }
    private void Unwatch() { if (_watchers > 0 && --_watchers == 0) _timer.Stop(); }

    // WPF :86-144: join an in-flight round; signed out asks nobody; partial success is usable.
    public Task RefreshAsync() => _disposed ? Task.CompletedTask : _inflight ??= RunAsync();

    private async Task RunAsync()
    {
        // Establish _inflight before even synchronous test wires or signed-out completion.
        await Task.Yield();
        try
        {
            bool ok = true;
            var snapshot = LobbySnapshot.Empty;
            if (SignedIn())
            {
                var chess = Safe(() => FetchChess(CancellationToken.None), PbpLobbyReply.Empty);
                var goon = Safe(() => FetchGoon(CancellationToken.None), OpenTablesReply.Empty);
                var race = Safe(() => FetchRace(CancellationToken.None), RaceOpenReply.Empty);
                var remote = Safe(() => FetchRemote(CancellationToken.None), (Seats: (IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), Ok: false));
                await Task.WhenAll(chess, goon, race, remote);
                if (remote.Result.Ok) _remoteSeats = remote.Result.Seats;
                ok = chess.Result.Ok || remote.Result.Ok || race.Result.Ok;
                IReadOnlyList<Friend> friends;
                try { friends = ReadFriends(); } catch { friends = Array.Empty<Friend>(); }
                snapshot = LobbyMerge.Build(chess.Result, goon.Result, _remoteSeats,
                    friends, true, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), race.Result);
            }
            if (_disposed) return;
            Snapshot = snapshot;
            LastRoundOk = ok;
            CurrentInterval = LobbyPollPlan.Next(CurrentInterval, ok);
            if (IsWatched) _timer.Interval = CurrentInterval;
            Changed?.Invoke(snapshot);
        }
        finally { _inflight = null; }
    }

    // WPF MainWindow.Lobby.cs:268-271 and AvailableSubjectsService.cs:219-245.
    public async Task<string?> ClaimRemoteAsync(string subjectId)
    {
        var result = await ClaimRemote(subjectId);
        if (result.Refresh) _ = RefreshAfterClaimAsync();
        return result.Url;
    }

    private async Task RefreshAfterClaimAsync()
    {
        if (_inflight is { } pending) await pending;
        await RefreshAsync();
    }

    private static async Task<T> Safe<T>(Func<Task<T>> fetch, T empty)
    {
        try { return await fetch(); } catch { return empty; }
    }

    public void Dispose()
    {
        _disposed = true;
        _watchers = 0;
        _timer.Stop();
        _timer.Tick -= OnTick;
    }

    private sealed class Lease(LobbyService owner) : IDisposable
    {
        private LobbyService? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Unwatch();
    }
}
