using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;
namespace CCP.Core.Tests;
/// <summary>One account at a time through the Lobby feed and the Remote directory wire.</summary>
public sealed class LobbyAccountSwitchTests {
    private sealed class Timer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() { } public void Stop() { }
        public void OnUiThread(Action action) => action();
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal string? Claimed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.NotEmpty(request.Headers.GetValues("X-Caller-Unified-Id"));
            var claim = request.RequestUri!.AbsolutePath == "/v2/directory/claim";
            if (claim) Claimed = await request.Content!.ReadAsStringAsync(ct);
            else Assert.Equal("/v2/directory/list", request.RequestUri.AbsolutePath);
            return new HttpResponseMessage(Status) { Content = new StringContent(claim ? "{\"session_url\":\"https://s/#pin\"}"
                : "{\"entries\":[{\"unified_id\":\"r\",\"display_name\":\"Host\"}]}") };
        }
    }
    [Fact]
    public async Task DirectoryKeepsLastGoodRowsOnlyForTheSameAccountAndClaimsReturnTheSession()
    {
        var wire = new Wire();
        using var http = new HttpClient(wire);
        var caller = "a";
        var api = new RemoteLobbyApi(http, () => (caller, "token"), () => "https://example.invalid");
        Assert.Equal("Host", Assert.Single((await api.FetchAsync()).Seats).Name);
        Assert.Equal("https://s/#pin", await api.ClaimAsync("r"));
        Assert.Equal("{\"unified_id\":\"r\"}", wire.Claimed);
        wire.Status = HttpStatusCode.ServiceUnavailable;
        var failed = await api.FetchAsync();
        Assert.True(!failed.Ok && failed.Seats.Count == 1); // the last good directory stays
        Assert.Null(await api.ClaimAsync("r"));
        wire.Status = HttpStatusCode.Conflict; // someone claimed first: no session, the next round shows it taken
        Assert.Null(await api.ClaimAsync("r"));
        caller = "b";
        Assert.Empty((await api.FetchAsync()).Seats);
    }
    private static PbpLobbyReply Seat(string host) => new(new[] { new PbpOpenSeat("p_" + host, host, 0, 0, 0) }, Array.Empty<PbpPlayingGame>(), true);
    private static LobbyPoll Poll(Func<Task<PbpLobbyReply>> chess, Func<string?> account, RaceOpenReply? race = null) =>
        new(new Timer(), () => account() != null, () => Array.Empty<Friend>(), chess, () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), false)), () => Task.FromResult(race ?? RaceOpenReply.Empty), account);
    [Fact]
    public async Task SwitchingAccountsDiscardsOldRoundAndRefreshesNewAccount()
    {
        var account = "A";
        var friends = new FriendsService(new FriendsApi(null, () => null, "http://127.0.0.1:1"), () => account);
        await friends.RefreshAsync(); Assert.Equal("A", friends.SnapshotAccount);
        var oldRound = new TaskCompletionSource<PbpLobbyReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRound = new TaskCompletionSource<LobbySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (published, calls) = (new List<LobbySnapshot>(), 0);
        using var poll = Poll(() => ++calls == 1 ? oldRound.Task : Task.FromResult(Seat("B")), () => account);
        poll.Changed += snapshot => { published.Add(snapshot); if (snapshot.OpenCount == 1) newRound.TrySetResult(snapshot); };
        var first = poll.RefreshAsync();
        account = "B";
        Assert.NotEqual(account, friends.SnapshotAccount);
        await friends.RefreshAsync(); Assert.Equal("B", friends.SnapshotAccount);
        oldRound.SetResult(Seat("A"));
        await first;
        var current = await newRound.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.Equal("B", Assert.Single(current.Open).HostName);
        Assert.All(published, snapshot => Assert.DoesNotContain(snapshot.Open, row => row.HostName == "A"));
        Assert.Equal(0, published[0].OpenCount);
    }
    [Fact]
    public async Task ACompletedRoundIsNeverReadByTheNextAccountAndRacesRideTheFeed()
    {
        string? account = "A";
        var race = new RaceOpenReply(new[] { new RaceOpenRoom("room", "track", "Race", 2, 1, 1, false, null, false) }, true);
        using var poll = Poll(() => Task.FromResult(Seat(account!)), () => account, race);
        await poll.RefreshAsync();
        Assert.Equal(new[] { LobbyGame.Chess, LobbyGame.Race }, poll.Snapshot.Joinable.Select(r => r.Game).OrderBy(g => g));
        account = "B";
        // The launcher and the Lobby page paint Snapshot on open, before B's first round lands.
        Assert.False(poll.SnapshotIsCurrent);
        Assert.Same(LobbySnapshot.Empty, poll.Snapshot);
        await poll.RefreshAsync();
        Assert.Equal("B", poll.Snapshot.Open.Single(r => r.Game == LobbyGame.Chess).HostName);
        account = "A";
        Assert.Same(LobbySnapshot.Empty, poll.Snapshot);
    }
}
