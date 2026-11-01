// WPF Services/Lobby/LobbyService.cs:24-195 and AvailableSubjectsService.cs:105-185,255-275.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// The shell's two leases share one poll. The launcher does not yet consume this service.
internal sealed class LobbyService : IDisposable
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _timer = new();
    private int _watchers;
    private Task? _inflight;
    private CancellationTokenSource? _directoryRun;
    private IReadOnlyList<RemoteSeat> _remote = Array.Empty<RemoteSeat>();
    private bool _remoteOk = true;
    private bool _disposed;

    internal Func<(string UnifiedId, string Token)?> Identity { get; set; } = FriendsHead.Identity;
    internal Func<string?> BaseUrl { get; set; } = () => LobbyWire.DefaultBaseUrl();
    internal Func<bool> SignedIn { get; set; }
    internal Func<Task<PbpLobbyReply>> FetchChess { get; set; } = () => new PbpLobbyApi().FetchAsync();
    internal Func<Task<OpenTablesReply>> FetchGoon { get; set; } = () => GoonOpenTables.RefreshAsync();
    internal Func<Task<RaceOpenReply>> FetchRace { get; set; }
    internal Func<CancellationToken, Task<IReadOnlyList<RemoteSeat>>> FetchDirectory { get; set; }
    internal HttpClient DirectoryHttp { get; set; } = Http;
    internal Func<IReadOnlyList<Friend>> ReadFriends { get; set; } = () => FriendsHead.Service?.Snapshot.Friends ?? Array.Empty<Friend>();
    internal LobbySnapshot Snapshot { get; private set; } = LobbySnapshot.Empty;
    internal bool LastRoundOk { get; private set; } = true;
    internal int Watchers => _watchers;
    internal bool Polling => _timer.IsEnabled;
    internal TimeSpan Interval => _timer.Interval;
    internal event Action<LobbySnapshot>? Changed;

    internal LobbyService()
    {
        SignedIn = () => Identity() != null;
        FetchRace = () =>
        {
            var url = BaseUrl();
            return url == null ? Task.FromResult(RaceOpenReply.Empty)
                : new RaceOpenApi(identity: Identity, baseUrl: url).FetchAsync();
        };
        FetchDirectory = FetchDirectoryAsync;
        _timer.Interval = LobbyPollPlan.Base;
        _timer.Tick += (_, _) => { if (_watchers > 0) _ = RefreshAsync(); };
    }

    // WPF LobbyService.cs:53-83: each lease asks for a round; only the first starts the timer.
    internal IDisposable Watch()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LobbyService));
        if (++_watchers == 1) { _timer.Interval = LobbyPollPlan.Base; _timer.Start(); }
        _ = RefreshAsync();
        return new Lease(this);
    }

    private void Unwatch()
    {
        if (_watchers > 0 && --_watchers == 0) _timer.Stop();
    }

    internal void CancelDirectory() => _directoryRun?.Cancel(); // WPF StopPolling on every tab switch

    internal Task RefreshAsync()
    {
        if (_disposed) return Task.CompletedTask;
        if (_inflight != null) return _inflight;
        var run = RunAsync();
        _inflight = run.IsCompleted ? null : run;
        return run;
    }

    private async Task RunAsync()
    {
        bool ok = true;
        var snapshot = Snapshot;
        try
        {
            if (!SignedIn()) snapshot = LobbySnapshot.Empty;
            else
            {
                var chess = Safe(FetchChess, PbpLobbyReply.Empty);
                var goon = Safe(FetchGoon, OpenTablesReply.Empty);
                var race = Safe(FetchRace, RaceOpenReply.Empty);
                var remote = RefreshDirectoryAsync();
                await Task.WhenAll(chess, goon, race, remote);
                ok = chess.Result.Ok || _remoteOk || race.Result.Ok;
                IReadOnlyList<Friend> friends;
                try { friends = ReadFriends(); } catch { friends = Array.Empty<Friend>(); }
                snapshot = LobbyMerge.Build(chess.Result, goon.Result, _remote, friends, true,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), race.Result);
            }
        }
        catch (Exception ex) { Log.Debug("Lobby round: {E}", ex.Message); ok = false; }
        finally { _inflight = null; }
        if (_disposed) return;
        LastRoundOk = ok;
        _timer.Interval = LobbyPollPlan.Next(_timer.Interval, ok);
        Snapshot = snapshot;
        try { Changed?.Invoke(snapshot); } catch (Exception ex) { Log.Debug("Lobby.Changed: {E}", ex.Message); }
    }

    private static async Task<T> Safe<T>(Func<Task<T>> fetch, T fallback)
    {
        try { return await fetch(); } catch { return fallback; }
    }

    private async Task RefreshDirectoryAsync()
    {
        using var run = new CancellationTokenSource();
        _directoryRun = run;
        try
        {
            var seats = await FetchDirectory(run.Token);
            run.Token.ThrowIfCancellationRequested();
            _remote = seats;
            _remoteOk = true;
        }
        catch (OperationCanceledException) { } // WPF retains the last collection and error state on cancellation
        catch { _remoteOk = false; }
        finally { if (ReferenceEquals(_directoryRun, run)) _directoryRun = null; }
    }

    private async Task<IReadOnlyList<RemoteSeat>> FetchDirectoryAsync(CancellationToken ct)
    {
        var identity = Identity();
        var url = BaseUrl();
        if (identity == null || url == null) throw new InvalidOperationException("Directory unavailable");
        using var request = new HttpRequestMessage(HttpMethod.Get, url + "/v2/directory/list");
        request.Headers.Add("X-Auth-Token", identity.Value.Token);
        request.Headers.Add("X-Caller-Unified-Id", identity.Value.UnifiedId);
        request.Headers.TryAddWithoutValidation("X-Client-Version", CoreReleaseContent.AppVersion);
        request.Headers.TryAddWithoutValidation("User-Agent", "ConditioningControlPanel/" + CoreReleaseContent.AppVersion);
        using var response = await DirectoryHttp.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = JObject.Parse(await response.Content.ReadAsStringAsync(ct));
        var result = new List<RemoteSeat>();
        foreach (var item in body["entries"] as JArray ?? new JArray())
        {
            try
            {
                var tags = (item["tags"] as JArray)?.Select(x => x.ToString()).Where(x => x.Length > 0).ToList() ?? new List<string>();
                result.Add(new RemoteSeat(item["unified_id"]?.ToString() ?? "", item["display_name"]?.ToString() ?? "Anonymous",
                    item["level"]?.Value<int>() ?? 1, item["tier"]?.ToString() ?? "light", tags, item["claimed"]?.Value<bool>() ?? false));
            }
            catch { } // WPF drops a malformed entry and keeps the rest
        }
        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchers = 0;
        _timer.Stop();
        CancelDirectory();
    }

    private sealed class Lease : IDisposable
    {
        private LobbyService? _owner;
        internal Lease(LobbyService owner) => _owner = owner;
        public void Dispose() { Interlocked.Exchange(ref _owner, null)?.Unwatch(); }
    }
}
