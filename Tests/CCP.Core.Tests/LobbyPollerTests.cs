using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF Services/Lobby/LobbyService.cs semantics on the Core poller.</summary>
public sealed class LobbyPollerTests
{
    private sealed class FakeTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool Running;
        public event EventHandler? Tick;
        public void Start() => Running = true;
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }

    private static (LobbyPoller Poller, FakeTimer Timer, List<string> Calls) Make(bool signedIn = true, bool chessOk = true, bool remoteOk = true)
    {
        var timer = new FakeTimer();
        var calls = new List<string>();
        var p = new LobbyPoller(() => timer)
        {
            SignedIn = () => signedIn,
            NowMs = () => 1_000_000,
            FetchChess = _ => { calls.Add("chess"); return Task.FromResult(new PbpLobbyReply(Array.Empty<PbpOpenSeat>(), Array.Empty<PbpPlayingGame>(), chessOk)); },
            FetchGoon = () => { calls.Add("goon"); return Task.FromResult(OpenTablesReply.Empty); },
            FetchRemote = () =>
            {
                calls.Add("remote");
                IReadOnlyList<RemoteSeat> seats = new[] { new RemoteSeat("u1", "Kitten", 3, "light", Array.Empty<string>(), false) };
                return Task.FromResult((seats, remoteOk));
            },
            ReadFriends = () => Array.Empty<Friend>(),
        };
        return (p, timer, calls);
    }

    [Fact]
    public async Task WatchPollsUntilTheLastLeaseLeaves()
    {
        var (p, timer, calls) = Make();
        LobbySnapshot? seen = null;
        p.Changed += s => seen = s;
        var a = p.Watch();
        var b = p.Watch();
        await p.RefreshAsync();
        Assert.True(timer.Running);
        Assert.Equal(LobbyPollPlan.Base, timer.Interval);
        Assert.Contains("remote", calls);
        Assert.NotNull(seen);
        Assert.Equal("u1", Assert.Single(seen!.Open).Key);
        a.Dispose();
        a.Dispose();
        Assert.True(timer.Running);
        b.Dispose();
        Assert.False(timer.Running);
        Assert.False(p.IsWatched);
        calls.Clear();
        timer.Fire();
        Assert.Empty(calls);
    }

    [Fact]
    public async Task SignedOutAsksNobodyAndPaintsEmpty()
    {
        var (p, _, calls) = Make(signedIn: false);
        await p.RefreshAsync();
        Assert.Empty(calls);
        Assert.Same(LobbySnapshot.Empty, p.Snapshot);
        Assert.True(p.LastRoundOk);
    }

    [Fact]
    public async Task ARoundWhereChessAndRemoteBothFailBacksOff()
    {
        var (p, timer, _) = Make(chessOk: false, remoteOk: false);
        using var lease = p.Watch(); // Watch runs the first round itself (WPF LobbyService.Watch).
        await p.RefreshAsync();
        Assert.False(p.LastRoundOk);
        // Two failed rounds: 10 s -> 20 s -> 40 s, capped by the plan.
        Assert.Equal(LobbyPollPlan.Next(LobbyPollPlan.Next(LobbyPollPlan.Base, false), false), timer.Interval);
        Assert.NotEqual(LobbyPollPlan.Base, timer.Interval);
    }

    [Fact]
    public void DirectoryParseKeepsWpfDefaults()
    {
        var seats = RemoteDirectoryApi.Parse("{\"entries\":[{\"unified_id\":\"a\",\"tags\":[\"x\",\"\"],\"claimed\":true},{\"unified_id\":\"b\",\"display_name\":\"B\",\"level\":7,\"tier\":\"deep\"}]}");
        Assert.Equal(2, seats.Count);
        Assert.Equal(("a", "Anonymous", 1, "light", true), (seats[0].UnifiedId, seats[0].Name, seats[0].Level, seats[0].Tier, seats[0].Claimed));
        Assert.Equal(new[] { "x" }, seats[0].Tags);
        Assert.Equal(("B", 7, "deep", false), (seats[1].Name, seats[1].Level, seats[1].Tier, seats[1].Claimed));
        Assert.Empty(RemoteDirectoryApi.Parse("not json"));
    }
}
