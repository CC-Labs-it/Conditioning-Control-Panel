using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
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
    public Task ClickingAFeedLineRevealsTheCardAndScrollsToTheLaidOutRow() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var previous = (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode);
        (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode) = (MotionLevel.Full, false);
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var feed = new FriendsFeed(() => "account");
        feed.Add(new FriendEvent(FriendEventKind.PokeReceived, "u_on", "Friend", DateTime.UtcNow, "click", Detail: "hi"));
        var page = new FriendsTabView(svc);
        var d = page.Drawer;
        d.FeedSource = () => feed;
        var host = new Window { Content = page, Width = 800, Height = 700 };
        try
        {
            host.Show();
            page.OnShown();
            Step(d, 2000);
            Control? target = null;
            double height = -1;
            host.AddHandler(Control.RequestBringIntoViewEvent, (_, e) =>
            {
                if (e.TargetObject is Control c && Equals(c.Tag, "friends-row:u_on")) (target, height) = (c, c.Bounds.Height);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // hit testing reads the last rendered frame
            Dispatcher.UIThread.RunJobs();
            var line = Tagged<Border>(d, "friends-feed-line:click")!;
            var at = line.TranslatePoint(new Point(30, line.Bounds.Height / 2), host)!.Value;
            var hit = host.InputHitTest(at) as Visual;
            Assert.True(hit?.GetSelfAndVisualAncestors().Contains(line) == true,
                $"the feed line is not hit-testable at {at}");
            host.MouseDown(at, MouseButton.Left);
            host.MouseUp(at, MouseButton.Left);
            var row = Tagged<Border>(d, "friends-row:u_on")!;
            Assert.True(ReferenceEquals(row, target), $"target={target?.Tag} open={row.Opacity}");
            Assert.InRange(row.Opacity, 0, 0.5); // Toggle's card reveal (WPF OpenOn) has just started (no Toggle: 1)
            Assert.True(height > 0, "BringIntoView ran before the rebuilt row was laid out");
            Step(d, 400);
            Assert.Equal(1, row.Opacity);
        }
        finally { page.OnHidden(); host.Close(); (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode) = previous; }
    });

    [Fact]
    public Task TheLandingTickMovesThisPcsBlocksOnceTheServerListsThem() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var (store, account, oldSvc, oldWorld, oldAnchor) = (FriendsLocalBlocks.Shared, FriendsLocalBlocks.Account, FriendsHead.Service, FriendsLanding.World, FriendsLanding.Anchor);
        var saved = new List<LocalBlockedEntry>();
        var me = "tick-" + Guid.NewGuid().ToString("N"); // MovedAccounts is process-wide: a fresh account per run
        try
        {
            FriendsLocalBlocks.Shared = new(() => saved.ToArray(), rows => { saved.Clear(); saved.AddRange(rows); });
            FriendsLocalBlocks.Account = () => me;
            FriendsLocalBlocks.Shared.Add(me, "u_old", "Old", DateTimeOffset.UtcNow);
            FriendsLanding.World = () => new LandingWorld(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: false);
            FriendsLanding.Anchor = () => null;
            var (svc, _) = Service();
            await svc.RefreshAsync();
            Assert.NotNull(svc.Snapshot.Blocked); // the server lists blocks
            FriendsHead.Service = svc;
            FriendsLanding.Tick();
            Assert.Empty(saved);
        }
        finally
        {
            FriendsLanding.Stop();
            (FriendsLocalBlocks.Shared, FriendsLocalBlocks.Account, FriendsHead.Service, FriendsLanding.World, FriendsLanding.Anchor) = (store, account, oldSvc, oldWorld, oldAnchor);
        }
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
