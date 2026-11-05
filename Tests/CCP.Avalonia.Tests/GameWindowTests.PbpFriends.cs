using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed partial class GameWindowTests
{
    // WPF PieceByPieceHostService.Friends.cs:127-144. A slow OS query must not let
    // a drawer intent overtake identity on the base's asynchronous boot path.
    [Fact]
    public void PieceByPiece_FriendIntentWaitsForSlowIdentity() => RunBoard(() =>
    {
        var answer = new TaskCompletionSource<bool>();
        var w = GameWindow.Launch(GameWindow.PbpId)!;
        w.PbpOsAnimations = () => answer.Task;
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.HandleMessage("{\"type\":\"ready\"}");
        var pending = GameWindow.ChallengePbpFriend("friend", TimeSpan.FromSeconds(45));
        Assert.Empty(sent);
        answer.SetResult(true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "pbp:settings", "pbp:identity", "pbp:friend" }, sent.Select(o => (string?)o["type"]));
        w.Close();
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Null(pending.Result);
    });

    [Fact]
    public void PieceByPiece_FriendChallenge_QueuesAfterIdentity_Reuses_Replaces_Timeouts_AndPanic() => RunBoard(() =>
    {
        const string challenge = "c_0123456789abcdef";
        Assert.True(ConditioningControlPanel.Avalonia.Views.Controls.FriendsInviteCodes.HostsChess());
        var pending = ConditioningControlPanel.Avalonia.Views.Controls.FriendsInviteCodes.ChallengeFriend("friend", TimeSpan.FromSeconds(45));
        Assert.Same(pending, GameWindow.ChallengePbpFriend("friend", TimeSpan.FromSeconds(45)));
        var w = GameWindow.OpenForTests(GameWindow.PbpId)!;
        var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
        Assert.False(pending.IsCompleted);
        w.PbpOsAnimations = () => Task.FromResult(true);
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.True(sent.FindIndex(o => (string?)o["type"] == "pbp:friend") > sent.FindIndex(o => (string?)o["type"] == "pbp:identity"));
        Assert.Equal("friend", (string?)sent.Last()["friendId"]);
        w.HandleMessage("{\"type\":\"pbp:friend-challenge\",\"challengeId\":\"" + challenge + "\"}");
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal(challenge, pending.Result);
        pending = GameWindow.ChallengePbpFriend("other", TimeSpan.FromSeconds(45));
        Assert.Equal("other", (string?)sent.Last()["friendId"]);
        var next = GameWindow.ChallengePbpFriend("third", TimeSpan.FromSeconds(45));
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Null(pending.Result);
        w.HandleMessage("{\"type\":\"pbp:friend-challenge\",\"challengeId\":\"bad\"}");
        Assert.True(next.IsCompletedSuccessfully);
        Assert.Null(next.Result);
        var timeout = GameWindow.ChallengePbpFriend("timeout", TimeSpan.Zero);
        Assert.True(timeout.Wait(TimeSpan.FromSeconds(5))); Assert.Null(timeout.Result);
        GameWindow.JoinPbpChallenge(challenge);
        Assert.Equal("accept", (string?)sent.Last()["mode"]);
        Assert.Equal(challenge, (string?)sent.Last()["challengeId"]);
        Assert.True(FriendsLanding.CanJoin(new("invite", ConditioningControlPanel.Services.Friends.SendKind.Invite, "friend", "Friend", null, null, "chess", challenge, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1))));
        Assert.False(FriendsLanding.CanJoin(new("invite", ConditioningControlPanel.Services.Friends.SendKind.Invite, "friend", "Friend", null, null, "chess", "bad", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1))));
        pending = GameWindow.ChallengePbpFriend("panic", TimeSpan.FromSeconds(45));
        GameWindow.CloseAllForPanic();
        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Null(pending.Result);
        GameWindow.JoinPbpChallenge("bad"); Assert.False(GameWindow.IsAnyOpen());
        FriendsLanding.OnKnockDone(new("invite", ConditioningControlPanel.Services.Friends.SendKind.Invite, "friend", "Friend", null, null, "chess", challenge, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)), ConditioningControlPanel.Avalonia.Views.Overlays.KnockOutcome.Go);
        w = GameWindow.OpenForTests(GameWindow.PbpId)!;
        sent.Clear(); w.Posted += json => sent.Add(JObject.Parse(json));
        w.PbpOsAnimations = () => Task.FromResult(true);
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal("accept", (string?)sent.Last()["mode"]);
    });

}
