using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    [Fact]
    public Task FeedReadsOnlyVisibleLinesAndKeepsDotsUntilFolded() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var feed = new FriendsFeed(() => "account");
        for (int i = 0; i < 12; i++) feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Friend", DateTime.UtcNow.AddMinutes(i), "event" + i, Detail: "hi"));
        var d = new FriendsDrawer(svc, asPage: true) { FeedSource = () => feed };
        d.Render();
        Assert.Equal(12, feed.Unread);
        d.OnOpened();
        int dots() => d.GetLogicalDescendants().OfType<Control>().Count(c => Equals(c.Tag, "friends-feed-new"));
        Assert.Equal(4, feed.Unread);
        Assert.Equal(8, dots());
        Click(d, "friends-feed-more");
        Assert.Equal(0, feed.Unread);
        Assert.Equal(12, dots());
        var row = Tagged<Border>(d, "friends-row:u_on");
        feed.Add(new FriendEvent(FriendEventKind.RequestAccepted, "u_on", "Friend", DateTime.UtcNow.AddHours(1), "new"));
        Assert.Same(row, Tagged<Border>(d, "friends-row:u_on")); // incoming feed must not rebuild the friend list
        d.OnClosed();
        d.OnOpened();
        Assert.Equal(0, dots());
        Assert.Equal(3, d.GetLogicalDescendants().OfType<Border>().Count(b => b.Tag is string s && s.StartsWith("friends-feed-line:")));
        d.OnClosed();
        feed.Add(new FriendEvent(FriendEventKind.RequestAccepted, "u_on", "Friend", DateTime.UtcNow.AddHours(2), "closed"));
        Assert.Equal(1, feed.Unread);
    });

    [Fact]
    public Task BlockFallbackRemembersOnlySuccessAndServerListWins() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var store = FriendsLocalBlocks.Shared;
        var account = FriendsLocalBlocks.Account;
        var saved = new List<LocalBlockedEntry>();
        try
        {
            FriendsLocalBlocks.Shared = new(() => saved.ToArray(), rows => { saved.Clear(); saved.AddRange(rows); });
            FriendsLocalBlocks.Account = () => "account";
            var (svc, wire) = Service();
            wire.StateBody = State.Replace(",\"blocked\":[{\"id\":\"u_bad\",\"name\":\"Bob\"}]", "");
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc, asPage: true);
            wire.MutationBody = "{\"ok\":false}";
            await d.ConfirmAsync("u_on", "Friend", "block");
            Assert.Empty(saved);
            wire.MutationBody = "{\"ok\":true}";
            await d.BlockRequesterAsync(svc.Snapshot.Incoming.Single());
            Assert.Single(saved);
            d.ToggleBlocked();
            Assert.NotNull(Tagged<Border>(d, "friends-blocked:u_req"));
            wire.MutationBody = "{\"ok\":false}";
            await d.UnblockAsync(new BlockedFriend("u_req", "Friend"));
            Assert.Single(saved);
            wire.MutationBody = "{\"ok\":true}";
            await d.UnblockAsync(new BlockedFriend("u_req", "Friend"));
            Assert.Empty(saved);
            FriendsLocalBlocks.Shared.Add("account", "local", "Local", DateTimeOffset.UtcNow);
            wire.StateBody = State;
            await svc.RefreshAsync();
            d.Render();
            Assert.Null(Tagged<Border>(d, "friends-blocked:local"));
            Assert.NotNull(Tagged<Border>(d, "friends-blocked:u_bad"));
            d.OnClosed();
        }
        finally { FriendsLocalBlocks.Shared = store; FriendsLocalBlocks.Account = account; }
    });

    [Fact]
    public Task FeedTitlesKeepLiteralPlaceholdersAndTrailsShowAfterResultHold() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        var text = new TextBlock();
        FriendsDrawer.FillFeedLine(text, new FriendEvent(FriendEventKind.WatchReceived, "u_on", "Friend", DateTime.UtcNow, "watch", Detail: "title {0}"), svc.Snapshot.Friends, true);
        Assert.Contains("title {0}", string.Concat(text.Inlines!.OfType<Run>().Select(r => r.Text)));
        var d = new FriendsDrawer(svc, asPage: true);
        d.OnOpened();
        await svc.PokeAsync("u_on", "hi");
        Assert.NotNull(Tagged<TextBlock>(d, "friends-trail:sent"));
        var timers = new StepTimers();
        d.After = timers.After;
        await d.ReportAsync("u_on", ReportReason.All[0]);
        Assert.Null(Tagged<TextBlock>(d, "friends-trail:sent"));
        timers.Step(5000);
        Assert.NotNull(Tagged<TextBlock>(d, "friends-trail:sent"));
        d.OnClosed();
    });
}
