using System;
using ConditioningControlPanel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendNoticesTests
{
    private static void Platform()
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
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
    public Task RequestAcceptAndSafetyTeardown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, wire) = FriendsDrawerTests.Service();
        var panel = new Window();
        try
        {
            panel.Show();
            await svc.RefreshAsync();
            FriendsLanding.Service = () => svc;
            FriendsLanding.Anchor = () => panel;
            FriendsLanding.World = () => new LandingWorld(false, false, false, true, false, false, false);
            CoreSettings.Current.FriendNotificationsEnabled = true;
            FriendsLanding.Tick();
            FriendNotices.CloseAll();
            var request = new FriendRequest("u_req", "Friend", null, null, DateTimeOffset.UtcNow);
            FriendsLanding.Router!.OnRequestArrived(request);
            Assert.True(StartupLadder.Inbox.Contains("friends-request:u_req"));
            var w = FriendNotices.Current!;
            Dispatcher.UIThread.RunJobs();
            w.Dismiss(Assert.Single(w.Stack.All), true);
            await FriendsDrawerTests.Until(() => wire.Ops.Contains("accept"));
            Assert.Contains("accept", wire.Ops);
            FriendsLanding.Router!.OnRequestGone(request.Id);
            Assert.False(StartupLadder.Inbox.Contains("friends-request:u_req"));
            FriendsLanding.AnnounceRequest(request, false);
            PanicSurfaces.All.Single(s => s.Id == "friend-notices").Stop(null);
            Assert.Null(FriendNotices.Current);
            FriendsLanding.AnnounceRequest(request, false);
            FriendsLanding.World = () => new LandingWorld(true, false, false, true, false, false, false);
            FriendsLanding.Tick();
            Assert.Null(FriendNotices.Current);
            FriendsLanding.AnnounceRequest(request, false);
            FriendsLanding.Stop();
            Assert.Null(FriendNotices.Current);
        }
        finally { FriendsLanding.Stop(); panel.Close(); }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OpenDrawerRaisesTheShellsRailChipOnTheFriend(bool fromLauncher) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Platform();
        CoreSettings.Current.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var launcher = fromLauncher ? LauncherWindow.OpenFor(shell) : null;
            if (fromLauncher) shell.Hide();
            FriendsLanding.OpenDrawerIn(shell, "u_on");
            Dispatcher.UIThread.RunJobs();
            var chip = shell.FindControl<FriendsRailChip>("FriendsChip")!;
            if (launcher != null)
            {
                Assert.True(launcher.IsVisible);
                Assert.False(shell.IsVisible);
                Assert.False(chip.IsOpen);
                for (int i = 0; i < 30; i++) launcher.StepFx(0.02);
                Dispatcher.UIThread.RunJobs();
                Assert.False(launcher.IsVisible);
                Assert.True(shell.IsVisible);
                Assert.True(chip.Bounds.Width > 0);
            }
            Assert.True(chip.IsOpen);
            Assert.Equal("u_on", chip.Drawer.OpenId);
        }
        finally { LauncherWindow.Instance?.Close(); shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });
}
