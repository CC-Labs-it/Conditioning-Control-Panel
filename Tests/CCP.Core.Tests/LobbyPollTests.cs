using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;
using Xunit;
namespace CCP.Core.Tests;
public sealed class LobbyPollTests
{
    [Fact]
    public void TableIdsMatchWpfBoundaries()
    {
        foreach (var id in new[] { "p_a", "p_Az09_-", "p_" + new string('a', 62) }) Assert.True(PbpWatchRules.IsTableId(id));
        foreach (var id in new string?[] { null, "", "p_", "P_a", "m_a", "p_é", "p_a/", "p_a b", "p_a\n", "p_" + new string('a', 63) }) Assert.False(PbpWatchRules.IsTableId(id));
    }
    private sealed class Timer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public bool Running;
        public void Start() => Running = true;
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
        public void Fire() { if (Running) Tick?.Invoke(this, EventArgs.Empty); }
    }
    [Fact]
    public async Task SignedOutDoesNotFetchAndLastLeaseStopsTimer()
    {
        var timer = new Timer();
        var calls = 0;
        using var lobby = new LobbyPoll(timer, () => false, () => Array.Empty<Friend>(),
            () => { calls++; return Task.FromResult(PbpLobbyReply.Empty); },
            () => throw new Exception(), () => throw new Exception());
        var a = lobby.Watch(); var b = lobby.Watch();
        Assert.True(timer.Running);
        await lobby.RefreshAsync();
        Assert.Equal(0, calls);
        Assert.Equal(LobbySnapshot.Empty, lobby.Snapshot);
        a.Dispose(); a.Dispose(); Assert.True(timer.Running);
        b.Dispose(); Assert.False(timer.Running);
    }
    [Fact]
    public async Task ConcurrentWatchersShareRoundMergeAllSourcesAndBackoffResets()
    {
        var timer = new Timer();
        var pending = new TaskCompletionSource<PbpLobbyReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var fail = false;
        using var lobby = new LobbyPoll(timer, () => true, () => Array.Empty<Friend>(),
            () => { calls++; return fail ? Task.FromResult(PbpLobbyReply.Empty) : pending.Task; },
            () => Task.FromResult(new OpenTablesReply(false, true, new[] { new OpenTable("CODE", "Goon", 1, null, false, null, false, 0, false, 0) }, null)),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)new[] { new RemoteSeat("r", "Remote", 1, "light", Array.Empty<string>(), true) }, !fail)));
        using var a = lobby.Watch(); using var b = lobby.Watch();
        var round = lobby.RefreshAsync();
        Assert.Same(round, lobby.RefreshAsync()); Assert.Equal(1, calls);
        pending.SetResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p", "Chess", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true));
        await round;
        Assert.Equal(2, lobby.Snapshot.Open.Count);
        Assert.Single(lobby.Snapshot.Playing);
        Assert.True(lobby.LastRoundOk);
        fail = true;
        foreach (var seconds in new[] { 20, 40, 60, 60 })
        {
            await lobby.RefreshAsync(); Assert.Equal(TimeSpan.FromSeconds(seconds), timer.Interval);
        }
        Assert.False(lobby.LastRoundOk);
        fail = false; await lobby.RefreshAsync(); Assert.Equal(LobbyPollPlan.Base, timer.Interval);
    }
    [Fact]
    public async Task SignOutDuringRoundClearsRowsAndDisposedRoundCannotPublish()
    {
        var timer = new Timer(); var signedIn = true; var changed = 0;
        var pending = new TaskCompletionSource<PbpLobbyReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lobby = new LobbyPoll(timer, () => signedIn, () => Array.Empty<Friend>(), () => pending.Task,
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        lobby.Changed += _ => changed++;
        var round = lobby.RefreshAsync(); signedIn = false;
        pending.SetResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p", "Chess", 1, 0, 0) }, Array.Empty<PbpPlayingGame>(), true));
        await round; Assert.Equal(LobbySnapshot.Empty, lobby.Snapshot);
        lobby.Dispose(); timer.Fire(); await lobby.RefreshAsync(); Assert.Equal(1, changed);
    }
    private sealed class DirectoryHandler : HttpMessageHandler
    {
        public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("/v2/directory/list", request.RequestUri!.AbsolutePath);
            Assert.Equal("caller", Assert.Single(request.Headers.GetValues("X-Caller-Unified-Id")));
            Assert.Equal("test-token", Assert.Single(request.Headers.GetValues("X-Auth-Token")));
            return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent("{\"entries\":[{\"unified_id\":\"subject\",\"claimed\":true,\"tags\":[\"tag\",\"\"]},{\"level\":\"invalid\"}]}")
            });
        }
    }
    [Fact]
    public async Task DirectoryUsesCallerHeadersDefaultsSkipsMalformedEntriesAndRetainsLastGoodOnError()
    {
        var handler = new DirectoryHandler(); using var http = new HttpClient(handler);
        var api = new RemoteLobbyApi(http, () => ("caller", "test-token"), () => "https://example.invalid");
        var good = await api.FetchAsync(); Assert.True(good.Ok);
        var seat = Assert.Single(good.Seats);
        Assert.Equal("subject", seat.UnifiedId); Assert.Equal("Anonymous", seat.Name);
        Assert.Equal(1, seat.Level); Assert.Equal("light", seat.Tier); Assert.True(seat.Claimed);
        Assert.Equal("tag", Assert.Single(seat.Tags));
        handler.Fail = true;
        var bad = await api.FetchAsync(); Assert.False(bad.Ok); Assert.Same(good.Seats, bad.Seats);
        var signedOut = new RemoteLobbyApi(http, () => null, () => "https://example.invalid");
        Assert.Empty((await signedOut.FetchAsync()).Seats);
    }
}
