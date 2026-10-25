using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FriendsLocalBlocksTests
{
    [Fact]
    public void AccountsAreIsolatedAndRepeatedBlocksRefreshRatherThanDuplicate()
    {
        var rows = new List<LocalBlockedEntry>();
        var list = new FriendsLocalBlocks(() => rows.ToArray(), next => { rows.Clear(); rows.AddRange(next); });
        var at = DateTimeOffset.UtcNow;
        list.Add("a", "one", " First ", at);
        list.Add("b", "one", "Other", at);
        list.Add("a", "two", "Second", at.AddMinutes(1));
        list.Add("a", "one", "Updated", at.AddMinutes(2));
        Assert.Equal(new[] { "Updated", "Second" }, list.For("a").Select(e => e.Name));
        Assert.Equal("Other", Assert.Single(list.For("b")).Name);
        list.Remove("a", "one");
        Assert.Single(list.For("b"));
        Assert.Empty(list.For(null));
        for (int i = 0; i < FriendsLocalBlocks.Cap; i++) list.Add("c", i.ToString(), null, at.AddDays(i + 1));
        Assert.Equal(FriendsLocalBlocks.Cap, rows.Count);
        Assert.Empty(list.For("a"));
        Assert.All(list.For("c"), e => Assert.Equal("?", e.Name));
    }

    [Fact]
    public async Task MigrationDropsOnlyThisAccountAndNeverReblocks()
    {
        var rows = new List<LocalBlockedEntry>();
        var list = new FriendsLocalBlocks(() => rows.ToArray(), next => { rows.Clear(); rows.AddRange(next); });
        list.Add("a", "removed-on-another-device", "Friend", DateTimeOffset.UtcNow);
        list.Add("b", "other", "Friend", DateTimeOffset.UtcNow);
        // No service is needed: migration must never call BlockAsync on the server.
        Assert.True(await FriendsLocalBlocks.MigrateAsync(null!, list, "a", Array.Empty<BlockedFriend>()));
        Assert.Empty(list.For("a"));
        Assert.Single(list.For("b"));
    }

    [Fact]
    public void UnreadableStoreActsEmpty()
    {
        var list = new FriendsLocalBlocks(() => throw new InvalidOperationException(), _ => { });
        Assert.Empty(list.For("account"));
    }
}
