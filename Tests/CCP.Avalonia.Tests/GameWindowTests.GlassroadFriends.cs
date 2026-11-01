using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void GlassroadFriendInvite_CapabilityIsAdvertised()
    {
        var init = GlassroadHostRules.InitFrame("u", "Name", 2, false, 0.6, 1);
        Assert.True((bool)init["caps"]!["friendInvite"]!);
    }

    [Fact]
    public void GlassroadFriendRows_AreOnlineNamedAndLimited()
    {
        Friend Friend(string id, string name, bool online) => new(id, name, null, 0, online, FriendPresence.None, false);
        var people = new[] { Friend("off", "Off", false), Friend("", "No id", true), Friend("blank", " ", true) }
            .Concat(Enumerable.Range(0, 60).Select(i => Friend("u" + i, "Name " + i, true)));
        var rows = GlassroadHostRules.FriendRows(people);
        Assert.Equal(50, rows.Count);
        Assert.Equal("u0", (string?)rows[0]!["uid"]);
        Assert.Equal("Name 49", (string?)rows[49]!["name"]);
        Assert.Empty(GlassroadHostRules.FriendRows(null));
    }

    [Fact]
    public void GlassroadFriendInvite_ResultUsesFixedLocalizedReasons()
    {
        Assert.Equal("friends_race_room_full", GlassroadHostRules.InviteNoteKey(new SendOutcome(SendResult.Refused, null) { Reason = "room_full" }));
        Assert.Equal("friends_result_refused", GlassroadHostRules.InviteNoteKey(new SendOutcome(SendResult.Refused, null) { Reason = "untrusted" }));
        Assert.Equal("Hello", GlassroadHostRules.Sentence("hello"));
    }

    [Fact]
    public void GlassroadFriendInvite_ListSendRefuseAndClose() => RunGlassroad("1", (_, _, _) =>
    {
        var (svc, wire) = FriendsDrawerTests.Service();
        var oldService = FriendsHead.Service;
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.Initialize("en");
            var refresh = svc.RefreshAsync();
            PumpUntil(() => refresh.IsCompleted);
            refresh.GetAwaiter().GetResult();
            FriendsHead.Service = svc;
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"friends-list\",\"id\":\"list\"}");
            var list = sent.Single(x => (string?)x["id"] == "list");
            var friend = Assert.Single((JArray)list["friends"]!);
            Assert.Equal("u_on", (string?)friend["uid"]);
            Assert.Equal("Mia", (string?)friend["name"]);

            w.HandleMessage("{\"type\":\"invite-friend\",\"id\":\"bad-friend\",\"friendUid\":\"stranger\",\"roomId\":\"abcdefghijkl\"}");
            w.HandleMessage("{\"type\":\"invite-friend\",\"id\":\"bad-room\",\"friendUid\":\"u_on\",\"roomId\":\"../bad\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(wire.Sends);
            Assert.False((bool)sent.Single(x => (string?)x["id"] == "bad-friend")["sent"]!);
            Assert.False((bool)sent.Single(x => (string?)x["id"] == "bad-room")["sent"]!);

            w.HandleMessage("{\"type\":\"invite-friend\",\"id\":\"ok\",\"friendUid\":\"u_on\",\"roomId\":\"abcdefghijkl\"}");
            PumpUntil(() => sent.Any(x => (string?)x["id"] == "ok"));
            var body = JObject.Parse(Assert.Single(wire.Sends));
            Assert.Equal("u_on", (string?)body["to"]);
            Assert.Equal("race", (string?)body["destination"]);
            Assert.Equal("abcdefghijkl", (string?)body["room"]);
            var reply = sent.Single(x => (string?)x["id"] == "ok");
            Assert.True((bool)reply["sent"]!);
            Assert.Equal(GlassroadHostRules.Sentence(Loc.Get("friends_result_sent")), (string?)reply["note"]);

            wire.HoldSends = new TaskCompletionSource();
            w.HandleMessage("{\"type\":\"invite-friend\",\"id\":\"late\",\"friendUid\":\"u_on\",\"roomId\":\"abcdefghijkl\"}");
            w.Close();
            wire.HoldSends.SetResult();
            PumpUntil(() => w.GlassroadInvitePending.IsCompleted);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(sent, x => (string?)x["id"] == "late");
        }
        finally { FriendsHead.Service = oldService; LocalizationManager.Instance.Initialize(oldLanguage); }
    });

    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs();
        Assert.True(done());
    }
}
