using System;
using ConditioningControlPanel;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Safety;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    public void RaceJoinValidatesGatesReusesWindowAndPanicCloses(string? preview) => RunGlassroad(preview, (shell, launcher, http) =>
    {
        var old = CoreEntitlement.HasLabProvider;
        var oldService = FriendsLanding.Service;
        var spy = new KnockCardTests.FakeFriends();
        var invite = new InboxItem(Guid.NewGuid().ToString("N").Substring(0, 16), SendKind.Invite,
            "f", "Friend", null, null, InviteDestination.Race, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(2)) { Room = "abcdefghijkl" };
        FriendsLanding.Service = () => spy;
        try
        {
            CoreEntitlement.HasLabProvider = () => false;
            Assert.False(GameWindow.JoinRace("abcdefghijkl"));
            Assert.False(GameWindow.IsAnyOpen());
            FriendsLanding.OnKnockDone(invite with { Id = Guid.NewGuid().ToString("N").Substring(0, 16) }, KnockOutcome.Go);
            Assert.Equal(ReceiptState.Declined, Assert.Single(spy.Receipts).State);
            Assert.False(GameWindow.IsAnyOpen());
            spy.Receipts.Clear();
            CoreEntitlement.HasLabProvider = () => true;
            Assert.False(GameWindow.JoinRace("bad"));
            FriendsLanding.OnKnockDone(invite, KnockOutcome.Go);
            Assert.Equal(ReceiptState.Joined, Assert.Single(spy.Receipts).State);
            Assert.True(GameWindow.JoinRace("abcdefghijkl"));
            var w = GameWindow.OpenForTests(GameWindow.GlassroadId)!;
            Assert.Contains("join=abcdefghijkl", w.PageUrl!.Query);
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnNavigationCompleted(w.PageUrl);
            Assert.True(GameWindow.JoinRace("123456abcdef"));
            Assert.Contains(sent, o => (string?)o["type"] == "join-room" && (string?)o["roomId"] == "123456abcdef");
            Assert.True((bool)GlassroadHostRules.InitFrame(null, null, 0, false, 1, 1)["caps"]!["friendInvite"]!);
            w.Web.OnWebMessage("{\"type\":\"friends-list\",\"id\":\"list\"}");
            Assert.Contains(sent, o => (string?)o["type"] == "friends-list-reply" && (string?)o["id"] == "list");
            w.Web.OnWebMessage("{\"type\":\"invite-friend\",\"id\":\"invite\",\"friendUid\":\"unknown\",\"roomId\":\"bad\"}");
            Assert.Contains(sent, o => (string?)o["type"] == "invite-friend-reply" && (bool?)o["sent"] == false);
            var item = new InboxItem("i", SendKind.Invite, "f", "Friend", null, null, InviteDestination.Race,
                null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(2)) { Track = "keep", Room = "abcdefghijkl" };
            Assert.Contains("The Keep", FriendsLanding.KnockLine(item));
            Assert.True(FriendsLanding.CanJoin(item));
            Assert.Same(w, GameWindow.OpenForTests(GameWindow.GlassroadId));
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally
        {
            FriendsLanding.Service = oldService;
            CoreEntitlement.HasLabProvider = old;
            GameWindow.CloseAllForPanic();
        }
    });
}
