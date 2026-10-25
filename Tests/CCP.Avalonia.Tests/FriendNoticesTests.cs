using System;
using ConditioningControlPanel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
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

    private static Point Centre(Control c, Window w) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;

    private static Control Tagged(Window w, string tag) => w.GetVisualDescendants().OfType<Control>().First(c => Equals(c.Tag, tag));

    private static string PillText(Control pill) => ((TextBlock)((Button)pill).Content!).Text!;

    /// <summary>Real pointer input: hit testing reads the last rendered frame, so render first.</summary>
    private static void Press(Window w, Point p, MouseButton button)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        w.MouseMove(p, RawInputModifiers.None);
        w.MouseDown(p, button);
        w.MouseUp(p, button);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task RealInputActsOpensOnLeftOnlyAndHoverPauses() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        int acted = 0, opened = 0;
        try
        {
            NoticeLook Look() => new() { Line = "poked you:", ActionLabel = "Poke back", Act = _ => acted++, Open = _ => opened++ };
            FriendNotices.Present(null, new FriendNotice(NoticeKind.Poke, "a", "Friend", DateTimeOffset.UtcNow, 6000), Look());
            var w = FriendNotices.Current!;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("friends_notice_close"), AutomationProperties.GetName(Tagged(w, "friend-notice-close")));
            var onName = Centre(w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Friend"), w);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            w.MouseMove(onName, RawInputModifiers.None);
            Assert.True(w.Stack.Paused);
            w.MouseMove(new Point(-40, -40), RawInputModifiers.None);
            Assert.False(w.Stack.Paused);
            Press(w, onName, MouseButton.Right);
            Assert.Equal(0, opened);
            Assert.Single(w.Stack.All);
            Press(w, onName, MouseButton.Left);
            Assert.Equal(1, opened);
            Assert.Null(FriendNotices.Current);
            FriendNotices.Present(null, new FriendNotice(NoticeKind.Poke, "b", "Friend", DateTimeOffset.UtcNow, 6000), Look());
            w = FriendNotices.Current!;
            Dispatcher.UIThread.RunJobs();   // laid out before the point is read
            Press(w, Centre(Tagged(w, "friend-notice-action"), w), MouseButton.Left);
            Assert.Equal(1, acted);
            Assert.Equal(1, opened);
            Assert.Null(FriendNotices.Current);
        }
        finally { FriendNotices.CloseAll(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WatchNoticesDispatchHtCatalogueAndFlavourAndExpiredReopenSaysSo() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, _) = FriendsDrawerTests.Service();
        var (service, anchor, world) = (FriendsLanding.Service, FriendsLanding.Anchor, FriendsLanding.World);
        var (tab, browser, says) = (FriendsLanding.OpenPanelTab, FriendsLanding.OpenInBrowser, FriendsLanding.EmiSays);
        var s = CoreSettings.Current;
        var (bell, source, ratio, niches) = (s.FriendNotificationsEnabled, s.MediaSource, s.RemoteMediaRatio, s.FypOnlineNiches);
        var (room, subs, off) = (s.BackRoomMediaSource, s.BackRoomMediaSubs, s.BackRoomMediaSubsOff);
        var panel = new Window();
        string? opened = null, tabbed = null, said = null;
        var at = DateTimeOffset.UtcNow;
        InboxItem Item(string id, WatchKind kind, string what, DateTimeOffset expires) =>
            new(id, SendKind.Watch, "u_on", "Friend", null, null, null, null, new WatchRef(kind, what, null), at, expires);
        void Act(string label)
        {
            var w = FriendNotices.Current!;
            Dispatcher.UIThread.RunJobs();
            var act = Tagged(w, "friend-notice-action");
            Assert.Equal(Loc.Get(label), PillText(act));
            Press(w, Centre(act, w), MouseButton.Left);
        }
        try
        {
            panel.Show();
            await svc.RefreshAsync();
            FriendsLanding.Service = () => svc;
            FriendsLanding.Anchor = () => panel;
            FriendsLanding.World = () => new LandingWorld(false, false, false, true, false, false, false);
            FriendsLanding.OpenPanelTab = t => tabbed = t;
            FriendsLanding.OpenInBrowser = u => opened = u;
            FriendsLanding.EmiSays = (line, _) => said = line;
            s.FriendNotificationsEnabled = true;
            FriendsLanding.Tick();
            FriendNotices.CloseAll();
            FriendsLanding.Router!.OnDelivered(Item("w-ht", WatchKind.Ht, "123", at.AddDays(1)));
            Assert.Equal(Loc.Get("friends_land_emi_present"), said);
            Act("friends_land_watch");
            Assert.Equal("https://hypnotube.com/video/123", opened);

            FriendsLanding.Reopen(Item("w-cat", WatchKind.Catalogue, "no_such_file", at.AddDays(1)));
            Act("friends_land_watch");
            Assert.Equal("deeper", tabbed);

            s.MediaSource = LauncherMediaSettings.SourceLocal;
            FriendsLanding.Reopen(Item("w-fl", WatchKind.Flavour, "pink", at.AddDays(1)));
            Act("friends_land_try");
            Assert.Equal(new[] { "bimbo" }, s.FypOnlineNiches.ToArray());
            Assert.Equal(s.HasRemoteMediaConsent ? LauncherMediaSettings.SourceMixed : LauncherMediaSettings.SourceLocal, s.MediaSource);

            said = null;
            FriendsLanding.Reopen(Item("w-old", WatchKind.Ht, "9", at.AddMinutes(-1)));
            Assert.Equal(Loc.Get("friends_land_too_late"), said);
            Assert.Null(FriendNotices.Current);
        }
        finally
        {
            FriendsLanding.Stop();
            (FriendsLanding.Service, FriendsLanding.Anchor, FriendsLanding.World) = (service, anchor, world);
            (FriendsLanding.OpenPanelTab, FriendsLanding.OpenInBrowser, FriendsLanding.EmiSays) = (tab, browser, says);
            (s.FriendNotificationsEnabled, s.MediaSource, s.RemoteMediaRatio, s.FypOnlineNiches) = (bell, source, ratio, niches);
            (s.BackRoomMediaSource, s.BackRoomMediaSubs, s.BackRoomMediaSubsOff) = (room, subs, off);
            foreach (var id in new[] { "w-ht", "w-cat", "w-fl", "w-old" }) StartupLadder.Inbox.Remove("friends:" + id);
            panel.Close();
        }
    });

    [Fact]
    public Task OpenDrawerRaisesTheShellsRailChipOnTheFriend() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            FriendsLanding.OpenDrawerIn(shell, "u_on");
            Dispatcher.UIThread.RunJobs();
            var chip = shell.FindControl<FriendsRailChip>("FriendsChip")!;
            Assert.True(chip.IsOpen);
            Assert.Equal("u_on", chip.Drawer.OpenId);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });
}
