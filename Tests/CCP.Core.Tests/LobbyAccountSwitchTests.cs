using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;
namespace CCP.Core.Tests;
public sealed class LobbyAccountSwitchTests {
    private sealed class Timer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void OnUiThread(Action action) => action();
    }
    [Fact]
    public async Task SwitchingAccountsDiscardsOldRoundAndRefreshesNewAccount()
    {
        var account = "A";
        var friends = new FriendsService(new FriendsApi(null, () => null, "http://127.0.0.1:1"), () => account);
        await friends.RefreshAsync(); Assert.Equal("A", friends.SnapshotAccount);
        var oldRound = new TaskCompletionSource<PbpLobbyReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRound = new TaskCompletionSource<LobbySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new List<LobbySnapshot>();
        var calls = 0;
        using var poll = new LobbyPoll(new Timer(), () => account, () => Array.Empty<Friend>(),
            () => { calls++; return calls == 1 ? oldRound.Task : Task.FromResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_b", "B", 0, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)); },
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), false)),
            () => Task.FromResult(RaceOpenReply.Empty));
        poll.Changed += snapshot => { published.Add(snapshot); if (snapshot.OpenCount == 1) newRound.TrySetResult(snapshot); };
        var first = poll.RefreshAsync();
        account = "B";
        Assert.NotEqual(account, friends.SnapshotAccount);
        await friends.RefreshAsync(); Assert.Equal("B", friends.SnapshotAccount);
        oldRound.SetResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_a", "A", 0, 0, 0) }, Array.Empty<PbpPlayingGame>(), true));
        await first;
        var current = await newRound.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, calls);
        Assert.Equal("B", Assert.Single(current.Open).HostName);
        Assert.All(published, snapshot => Assert.DoesNotContain(snapshot.Open, row => row.HostName == "A"));
        Assert.Equal(0, published[0].OpenCount);
    }
}
