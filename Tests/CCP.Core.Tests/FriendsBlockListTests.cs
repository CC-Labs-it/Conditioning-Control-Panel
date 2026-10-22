using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FriendsBlockListTests
{
    [Fact]
    public void FallbackIsScopedNewestFirstAndRepeatedBlocksRefreshTheEntry()
    {
        var entries = new List<BlockedEntry>();
        var list = new FriendsBlockList(() => entries, rows => entries = rows.ToList());
        var time = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        list.Add("me", "a", " Ann ", time);
        list.Add("other", "b", "Bo", time);
        list.Add("me", "c", "Cy", time.AddMinutes(1));
        list.Add("me", "a", "Ann B", time.AddMinutes(2));
        Assert.Equal(new[] { "a", "c" }, list.Rows("me", null).Select(b => b.Id));
        Assert.Equal("Ann B", list.For("me")[0].Name);
        Assert.Single(list.For("other"));
        Assert.Empty(list.For(null));
        list.Remove("me", "a");
        Assert.Equal("c", Assert.Single(list.For("me")).Id);
        Assert.Single(list.For("other"));
    }

    [Fact]
    public void AServerListRetiresOnlyThisAccountsFallbackWithoutReblocking()
    {
        var entries = new List<BlockedEntry>();
        var list = new FriendsBlockList(() => entries, rows => entries = rows.ToList());
        list.Add("me", "a", "Ann", DateTimeOffset.UtcNow);
        list.Add("other", "b", "Bo", DateTimeOffset.UtcNow);
        var server = new[] { new BlockedFriend("c", "Cy") };
        Assert.Equal("c", Assert.Single(list.Rows("me", server)).Id);
        Assert.Empty(list.Rows("me", Array.Empty<BlockedFriend>()));
        Assert.Single(list.For("me"));   // showing the server's list never writes (WPF)
        Assert.True(FriendsBlockList.MigrateAsync(null!, list, "me", server).Result);
        Assert.Empty(list.For("me"));
        Assert.Single(list.For("other"));
    }

    [Fact]
    public void ClosingASurfaceRestoresTheActivityUnderneathAndOutOfOrderClosesAreSafe()
    {
        var stack = new PresenceActivityStack();
        Assert.False(stack.Enter(PresenceActivity.Offline));
        Assert.False(stack.Enter(PresenceActivity.Panel));
        Assert.True(stack.Enter(PresenceActivity.Session));
        Assert.True(stack.Enter(PresenceActivity.Chess));
        Assert.True(stack.Enter(PresenceActivity.Race));
        Assert.False(stack.Leave(PresenceActivity.Chess));
        Assert.True(stack.Leave(PresenceActivity.Race));
        Assert.Equal(PresenceActivity.Session, stack.Top);
        Assert.False(stack.Enter(PresenceActivity.Session));
        Assert.Equal(1, stack.Count);
        Assert.True(stack.Leave(PresenceActivity.Session));
        Assert.Equal(PresenceActivity.Panel, stack.Top);
    }

    [Fact]
    public void TheCapDropsTheOldestAndAnUnreadableFileIsAnEmptyFallback()
    {
        var entries = new List<BlockedEntry>();
        var list = new FriendsBlockList(() => entries, rows => entries = rows.ToList());
        for (var i = 0; i <= FriendsBlockList.Cap; i++)
            list.Add("me", i.ToString(), " ", DateTimeOffset.UnixEpoch.AddMinutes(i));
        Assert.Equal(FriendsBlockList.Cap, list.For("me").Count);
        Assert.DoesNotContain(list.For("me"), e => e.Id == "0");
        Assert.All(list.For("me"), e => Assert.Equal("?", e.Name));
        Assert.Empty(new FriendsBlockList(() => throw new InvalidOperationException(), _ => { }).Rows("me", null));
    }
}
