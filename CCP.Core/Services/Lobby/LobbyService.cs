using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
namespace ConditioningControlPanel.Services.Lobby;
/// <summary>Adapted from WPF LobbyService: one poll while watched, shared rounds, 10 to 60s backoff.
/// Call Watch/Dispose on the timer's UI thread. Changed is delivered on that thread too.</summary>
public sealed class LobbyService : IDisposable
{
    private readonly IUiTimer _timer;
    private readonly Func<bool> _signedIn;
    private readonly Func<Task<PbpLobbyReply>> _chess;
    private readonly Func<Task<OpenTablesReply>> _goon;
    private readonly Func<Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)>> _remote;
    private readonly Func<IReadOnlyList<Friend>> _friends;
    private readonly object _gate = new();
    private Task? _inflight;
    private int _watchers;
    private bool _disposed;
    public LobbySnapshot Snapshot { get; private set; } = LobbySnapshot.Empty;
    public bool LastRoundOk { get; private set; } = true;
    public event Action<LobbySnapshot>? Changed;
    public LobbyService(IUiTimer timer, Func<bool> signedIn, Func<IReadOnlyList<Friend>> friends,
        Func<Task<PbpLobbyReply>> chess, Func<Task<OpenTablesReply>> goon,
        Func<Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)>> remote)
    {
        _timer = timer; _signedIn = signedIn; _friends = friends;
        _chess = chess; _goon = goon; _remote = remote;
        _timer.Interval = LobbyPollPlan.Base;
        _timer.Tick += OnTick;
    }
    public IDisposable Watch()
    {
        if (_disposed) return new Lease(null);
        if (++_watchers == 1) { _timer.Interval = LobbyPollPlan.Base; _timer.Start(); }
        _ = RefreshAsync();
        return new Lease(this);
    }
    private void OnTick(object? sender, EventArgs e)
    {
        if (!_disposed && _watchers > 0) _ = RefreshAsync();
    }
    public Task RefreshAsync()
    {
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_inflight != null) return _inflight;
            // Set before running: synchronous fake wires and Changed callbacks may re-enter.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inflight = completion.Task;
            _ = RunAsync(completion);
            return completion.Task;
        }
    }
    private static async Task<T> Safe<T>(Func<Task<T>> fetch, T empty)
    {
        try { return await fetch().ConfigureAwait(false); } catch { return empty; }
    }
    private async Task RunAsync(TaskCompletionSource completion)
    {
        var snapshot = Snapshot;
        var ok = true;
        try
        {
            if (!_signedIn()) snapshot = LobbySnapshot.Empty;
            else
            {
                var chess = Safe(_chess, PbpLobbyReply.Empty);
                var goon = Safe(_goon, OpenTablesReply.Empty);
                var remote = Safe(_remote, ((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), false));
                await Task.WhenAll(chess, goon, remote).ConfigureAwait(false);
                ok = chess.Result.Ok || remote.Result.Item2; // Goon has no error distinction in WPF.
                IReadOnlyList<Friend> friends;
                try { friends = _friends(); } catch { friends = Array.Empty<Friend>(); }
                snapshot = LobbyMerge.Build(chess.Result, goon.Result, remote.Result.Item1, friends, true,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                if (!_signedIn()) snapshot = LobbySnapshot.Empty;
            }
        }
        catch { ok = false; }
        var result = snapshot;
        var success = ok;
        _timer.OnUiThread(() =>
        {
            try
            {
                if (_disposed) return;
                Snapshot = result; LastRoundOk = success;
                _timer.Interval = LobbyPollPlan.Next(_timer.Interval, success);
                try { Changed?.Invoke(result); } catch { /* WPF isolates subscriber failures. */ }
            }
            finally
            {
                lock (_gate) _inflight = null;
                completion.TrySetResult();
            }
        });
    }
    public void Dispose()
    {
        _disposed = true; _watchers = 0;
        _timer.Stop(); _timer.Tick -= OnTick;
    }
    private sealed class Lease(LobbyService? owner) : IDisposable
    {
        private LobbyService? _owner = owner;
        public void Dispose()
        {
            var service = Interlocked.Exchange(ref _owner, null);
            if (service != null && service._watchers > 0 && --service._watchers == 0) service._timer.Stop();
        }
    }
}
