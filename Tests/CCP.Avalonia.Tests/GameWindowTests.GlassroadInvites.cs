using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Glassroad_InviteCapabilityIsAdvertised()
    {
        var init = GlassroadHostRules.InitFrame(null, null, 0, false, 1, 1);
        Assert.True((bool)init["caps"]!["friendInvite"]!);
    }

    [Fact]
    public void Glassroad_FriendsFramesFilterAndInvite() => RunGlassroad("1", (_, _, _) =>
    {
        const string room = "abc123def456";
        var old = FriendsHead.Service;
        var (service, wire) = FriendsDrawerTests.Service();
        try
        {
            service.RefreshAsync().GetAwaiter().GetResult();
            FriendsHead.Service = service;
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"friends-list\",\"id\":\"list\"}");
            var list = sent.Single(o => (string?)o["type"] == "friends-list-reply");
            Assert.Equal("u_on", (string?)Assert.Single((JArray)list["friends"]!)["uid"]);
            w.HandleMessage("{\"type\":\"invite-friend\",\"id\":\"bad\",\"friendUid\":\"stranger\",\"roomId\":\"" + room + "\"}");
            Assert.False((bool)sent.Single(o => (string?)o["id"] == "bad")["sent"]!);
            Assert.Empty(wire.Sends);
            w.HandleMessage("{\"type\":\"invite-friend\",\"id\":\"ok\",\"friendUid\":\"u_on\",\"roomId\":\"" + room + "\"}");
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
            Assert.True((bool)sent.Single(o => (string?)o["id"] == "ok")["sent"]!);
            Assert.Contains(room, Assert.Single(wire.Sends));
            var init = GlassroadHostRules.InitFrame(null, null, 0, false, 1, 1);
            Assert.True((bool)init["caps"]!["friendInvite"]!);
        }
        finally { FriendsHead.Service = old; service.Dispose(); }
    });
}
