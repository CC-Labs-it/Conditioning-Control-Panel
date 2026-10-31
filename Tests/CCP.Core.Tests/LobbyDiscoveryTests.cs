using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace CCP.Core.Tests;

[Collection(ConditioningControlPanel.Tests.GoonOpenTablesCollection.Name)]
public sealed class LobbyDiscoveryTests
{
    private sealed class Timer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool Running;
        public int Starts;
        public event EventHandler? Tick;
        public void Start() { Running = true; Starts++; }
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public async Task LobbyAndDrawerShareGoonRequestsAndPublishTheSameCache()
    {
        var fetch = GoonOpenTables.Fetch;
        var clock = GoonOpenTables.UtcNow;
        GoonOpenTables.ResetForTests();
        try
        {
            var now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
            GoonOpenTables.UtcNow = () => now;
            var pending = new TaskCompletionSource<OpenTablesReply>();
            int requests = 0;
            GoonOpenTables.Fetch = _ => { requests++; return pending.Task; };
            OpenTablesReply? published = null;
            GoonOpenTables.Changed += reply => published = reply;
            var drawer = GoonOpenTables.RefreshAsync();
            using var service = new LobbyService(new Timer()) { SignedIn = () => true };
            service.FetchChess = _ => Task.FromResult(PbpLobbyReply.Empty);
            var started = new TaskCompletionSource();
            service.FetchRace = _ => { started.TrySetResult(); return Task.FromResult(RaceOpenReply.Empty); };
            var lobby = service.RefreshAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var reply = new OpenTablesReply(true, true,
                new[] { new OpenTable("ABCD", "Shared table", 0, null, false, null, false, 60, false, 0) }, null);
            pending.SetResult(reply);
            await lobby;
            Assert.Same(reply, await drawer);
            Assert.Equal("ABCD", Assert.Single(service.Snapshot.Open).Key);
            Assert.Equal(1, requests);
            Assert.Same(reply, GoonOpenTables.Latest);
            Assert.Same(reply, published);
            await service.RefreshAsync();
            Assert.Equal(1, requests); // another caller inside MinGap consumes the same cache
            now += GoonOpenTables.MinGap;
            GoonOpenTables.Fetch = _ => { requests++; return Task.FromResult(OpenTablesReply.Empty); };
            await service.RefreshAsync();
            Assert.Equal(2, requests);
            Assert.Empty(service.Snapshot.Open);
            Assert.Same(OpenTablesReply.Empty, GoonOpenTables.Latest);
            Assert.Same(OpenTablesReply.Empty, published);
        }
        finally
        {
            GoonOpenTables.Fetch = fetch;
            GoonOpenTables.UtcNow = clock;
            GoonOpenTables.ResetForTests();
        }
    }

    [Fact]
    public async Task SignedOutDoesNotFetchAndLeasesShareAndStopTheTimer()
    {
        var timer = new Timer();
        using var service = new LobbyService(timer) { SignedIn = () => false };
        int requests = 0, paints = 0;
        service.FetchChess = _ => { requests++; return Task.FromResult(PbpLobbyReply.Empty); };
        service.Changed += _ => paints++;
        var a = service.Watch();
        var b = service.Watch();
        await service.RefreshAsync();
        Assert.Equal(0, requests);
        Assert.Equal(1, paints);
        Assert.False(service.Snapshot.SignedIn);
        Assert.Equal(LobbyPollPlan.Base, timer.Interval);
        Assert.Equal(1, timer.Starts);
        a.Dispose(); a.Dispose();
        Assert.True(timer.Running);
        b.Dispose();
        Assert.False(timer.Running);
        Assert.False(service.IsWatched);
        timer.Fire();
        Assert.Equal(1, paints);
    }

    [Fact]
    public async Task InflightRoundIsSharedAndRealWireRowsReachTheSnapshotWithFriends()
    {
        var reply = new TaskCompletionSource<PbpLobbyReply>();
        using var service = new LobbyService(new Timer()) { SignedIn = () => true };
        int requests = 0;
        service.FetchChess = _ => { requests++; return reply.Task; };
        service.FetchGoon = _ => throw new InvalidOperationException();
        service.FetchRace = _ => Task.FromResult(RaceOpenReply.Empty);
        service.ReadFriends = () => new[] { new Friend("friend", "Ann", null, 0, true, new FriendPresence(PresenceActivity.Panel, null, DateTimeOffset.UtcNow), false) };
        var first = service.RefreshAsync();
        Assert.Same(first, service.RefreshAsync());
        reply.SetResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_a", "Ann", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true));
        await first;
        Assert.Equal(1, requests);
        Assert.True(service.LastRoundOk);
        Assert.Empty(service.Snapshot.Open);
        Assert.Equal("p_a", Assert.Single(service.Snapshot.Friends).Key);
        Assert.True(service.Snapshot.SignedIn);
    }

    [Fact]
    public async Task FaultsBackOffToSixtySecondsAndRecoveryResetsCadence()
    {
        var timer = new Timer();
        using var service = new LobbyService(timer) { SignedIn = () => true };
        service.FetchChess = _ => throw new InvalidOperationException();
        service.FetchGoon = _ => throw new InvalidOperationException();
        service.FetchRace = _ => Task.FromResult(RaceOpenReply.Empty);
        using var lease = service.Watch();
        await service.RefreshAsync();
        Assert.False(service.LastRoundOk);
        Assert.Equal(TimeSpan.FromSeconds(20), timer.Interval);
        await service.RefreshAsync(); await service.RefreshAsync();
        Assert.Equal(LobbyPollPlan.Max, timer.Interval);
        service.FetchChess = _ => Task.FromResult(new PbpLobbyReply(Array.Empty<PbpOpenSeat>(), Array.Empty<PbpPlayingGame>(), true));
        await service.RefreshAsync();
        Assert.True(service.LastRoundOk);
        Assert.Equal(LobbyPollPlan.Base, timer.Interval);
        service.SignedIn = () => false;
        await service.RefreshAsync();
        Assert.False(service.Snapshot.SignedIn);
        Assert.True(service.Snapshot.IsEmpty);
    }
}
