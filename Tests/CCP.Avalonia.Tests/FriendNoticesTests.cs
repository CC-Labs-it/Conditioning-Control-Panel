using System;
using ConditioningControlPanel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendNoticesTests
{
    private static void Platform()
    {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task RepeatsActOnNewestPayloadAndNeverFileMissed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        object? acted = null;
        int missed = 0;
        try
        {
            var at = DateTimeOffset.UtcNow;
            FriendNotices.Present(null, new FriendNotice(NoticeKind.Poke, "f", "Friend", at, 6000, "old"), new NoticeLook());
            FriendNotices.Present(null, new FriendNotice(NoticeKind.Poke, "f", "Friend", at.AddSeconds(1), 6000, "new"),
                new NoticeLook { Act = p => acted = p, Missed = _ => missed++ });
            var w = FriendNotices.Current!;
            var n = Assert.Single(w.Stack.All);
            Assert.Equal(2, n.Count);
            Assert.False(w.ShowActivated);
            w.Dismiss(n, true);
            Assert.Equal("new", acted);
            Assert.Equal(0, missed);
            Assert.Null(FriendNotices.Current);
        }
        finally { FriendNotices.CloseAll(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task HoverPausesAndOnlyUnseenDeparturesFileInbox(bool hold) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        int missed = 0, left = 0;
        try
        {
            FriendNotices.Present(null, new FriendNotice(NoticeKind.Poke, "f", "Friend", DateTimeOffset.UtcNow, 6000),
                new NoticeLook { Missed = _ => missed++, Left = _ => left++ });
            var w = FriendNotices.Current!;
            w.Stack.Paused = true;
            w.Advance(10000);
            Assert.Single(w.Stack.All);
            Assert.Equal(0, missed);
            w.Stack.Paused = false;
            if (hold) FriendNotices.CloseAll(fold: true); else w.Advance(10000);
            Assert.Equal(1, missed);
            Assert.Equal(1, left);
            Assert.Null(FriendNotices.Current);
        }
        finally { FriendNotices.CloseAll(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CloseAndBodyClickAreSeenDepartures() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        int opened = 0, missed = 0;
        try
        {
            FriendNotices.Present(null, new FriendNotice(NoticeKind.Poke, "f", "Friend", DateTimeOffset.UtcNow, 6000),
                new NoticeLook { Open = _ => opened++, Missed = _ => missed++ });
            var w = FriendNotices.Current!;
            w.Dismiss(w.Stack.All.Single(), false, open: true);
            Assert.Equal(1, opened);
            Assert.Equal(0, missed);
        }
        finally { FriendNotices.CloseAll(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PokeBackAndAcceptUseTheRealService() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, wire) = FriendsDrawerTests.Service();
        var service = FriendsLanding.Service;
        var anchor = FriendsLanding.Anchor;
        var world = FriendsLanding.World;
        var bell = CoreSettings.Current.FriendNotificationsEnabled;
        var panel = new global::Avalonia.Controls.Window();
        try
        {
            panel.Show();
            await svc.RefreshAsync();
            FriendsLanding.Service = () => svc;
            FriendsLanding.Anchor = () => panel;
            FriendsLanding.World = () => new LandingWorld(false, false, false, true, false, false, false);
            CoreSettings.Current.FriendNotificationsEnabled = true;
            FriendsLanding.Tick();
            FriendNotices.CloseAll(); // the service fixture also has an existing waiting request
            var at = DateTimeOffset.UtcNow;
            FriendsLanding.Router!.OnDelivered(new InboxItem("poke-action", SendKind.Poke, "u_on", "Friend", null,
                "hi", null, null, null, at, at.AddMinutes(2)));
            var w = FriendNotices.Current!;
            w.Dismiss(w.Stack.All.Single(), true);
            await FriendsDrawerTests.Until(() => wire.Sends.Count > 0);
            Assert.Contains("\"poke\":\"hi\"", Assert.Single(wire.Sends));
            FriendsLanding.AnnounceRequest(new FriendRequest("u_req", "Friend", null, null, at), false);
            w = FriendNotices.Current!;
            w.Dismiss(w.Stack.All.Single(), true);
            await FriendsDrawerTests.Until(() => wire.Ops.Contains("accept"));
            Assert.Contains("accept", wire.Ops);
        }
        finally
        {
            FriendsLanding.Stop();
            StartupLadder.Inbox.Remove("friends-requests-waiting");
            FriendsLanding.Service = service;
            FriendsLanding.Anchor = anchor;
            FriendsLanding.World = world;
            CoreSettings.Current.FriendNotificationsEnabled = bell;
            panel.Close();
        }
    });

    [Fact]
    public Task OverflowFilesTheOldestAndOnlyThreeTimersRun() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        int missed = 0;
        try
        {
            for (int i = 0; i < 13; i++) FriendNotices.Present(null,
                new FriendNotice(NoticeKind.Poke, i.ToString(), "Friend", DateTimeOffset.UtcNow, 6000),
                new NoticeLook { Missed = _ => missed++ });
            var w = FriendNotices.Current!;
            Assert.Equal(12, w.Stack.All.Count);
            Assert.Equal(9, w.Stack.Hidden);
            Assert.Equal(1, missed);
            var hidden = w.Stack.All[3];
            w.Advance(1000);
            Assert.Equal(1, hidden.Fraction);
            Assert.True(w.Stack.All[0].Fraction < 1);
        }
        finally { FriendNotices.CloseAll(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OpeningANoticedFriendKeepsItOpenAndClearsAnotherFriendsPicker() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, _) = FriendsDrawerTests.Service();
        await svc.RefreshAsync();
        var drawer = new ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer(svc);
        drawer.OpenPickerFor("u_on", "poke");
        drawer.OpenOn("u_on");
        Assert.Equal("poke", drawer.OpenPicker);
        drawer.OpenOn("u_off");
        Assert.Null(drawer.OpenPicker);
        drawer.OpenOn("u_off");
        Assert.Contains(drawer.GetLogicalDescendants().OfType<global::Avalonia.Controls.StackPanel>(), p => Equals(p.Tag, "friends-card"));
    });

    [Fact]
    public Task RequestBellAndWaitingSummaryFollowWpf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var bell = CoreSettings.Current.FriendNotificationsEnabled;
        try
        {
            var request = new FriendRequest("request", "Friend", null, null, DateTimeOffset.UtcNow);
            CoreSettings.Current.FriendNotificationsEnabled = false;
            FriendsLanding.FileRequest(request);
            Assert.True(StartupLadder.Inbox.Contains("friends-request:request"));
            FriendsLanding.AnnounceRequest(request, false);
            Assert.Null(FriendNotices.Current);
            FriendsLanding.Waiting(new[] { request }, false, false);
            Assert.Equal("Friend", StartupLadder.Inbox.Items.Single(i => i.Key == "friends-requests-waiting").Title);
            FriendsLanding.Waiting(new[] { request, request with { Id = "other" } }, false, false);
            Assert.Equal(string.Format(ConditioningControlPanel.Localization.Loc.Get("friends_land_waiting_many"), 2),
                StartupLadder.Inbox.Items.Single(i => i.Key == "friends-requests-waiting").Title);
            FriendsLanding.Waiting(Array.Empty<FriendRequest>(), false, false);
            Assert.False(StartupLadder.Inbox.Contains("friends-requests-waiting"));
        }
        finally
        {
            CoreSettings.Current.FriendNotificationsEnabled = bell;
            StartupLadder.Inbox.Remove("friends-request:request");
            StartupLadder.Inbox.Remove("friends-requests-waiting");
            FriendNotices.CloseAll();
        }
        return Task.CompletedTask;
    });
}
