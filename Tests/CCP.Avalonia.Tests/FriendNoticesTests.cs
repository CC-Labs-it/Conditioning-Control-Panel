using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Windows/Friends/FriendNotices.cs on this head: the corner notices FriendsLanding puts up for a
/// poke (Poke back), a watch and a friend request (Accept), routed by Core's FriendsLandingRouter.</summary>
public sealed class FriendNoticesTests
{
    private static InboxItem Poke(string id, string from, string poke) =>
        new(id, SendKind.Poke, from, "Mia", null, poke, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));

    /// <summary>The landing on <paramref name="svc"/>, panel up, motion off, bell on; every static put back (P02).</summary>
    private static Task Run(Func<FriendsDrawerTests.Wire, Window, List<string?>, Action<bool>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (motion, bell, volume) = (s.MotionLevel, s.FriendNotificationsEnabled, s.MasterVolume);
        var (world, anchor, active, says, service, open) = (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.EmiSays, FriendsLanding.Service, FriendsLanding.OpenDrawer);
        var (svc, wire) = FriendsDrawerTests.Service();
        var panel = new Window { Width = 1000, Height = 700 };
        var opened = new List<string?>();
        var holding = false;
        try
        {
            (s.MotionLevel, s.FriendNotificationsEnabled, s.MasterVolume) = (MotionLevel.Off, true, 0);
            StartupLadder.ResetForTests();
            FriendsSeen.Shared.Clear();
            panel.Show();
            FriendsLanding.World = () => new LandingWorld(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: false, SessionRunning: holding);
            FriendsLanding.Anchor = () => panel;
            FriendsLanding.ActiveGameWindow = () => null;
            FriendsLanding.EmiSays = (_, _) => { };
            FriendsLanding.Service = () => svc;
            FriendsLanding.OpenDrawer = id => opened.Add(id);
            FriendsLanding.Tick();
            await body(wire, panel, opened, h => holding = h);
        }
        finally
        {
            FriendsLanding.Stop();
            (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.EmiSays, FriendsLanding.Service, FriendsLanding.OpenDrawer) = (world, anchor, active, says, service, open);
            (s.MotionLevel, s.FriendNotificationsEnabled, s.MasterVolume) = (motion, bell, volume);
            StartupLadder.ResetForTests();
            FriendsSeen.Shared.Clear();
            foreach (var o in panel.OwnedWindows.ToArray()) o.Close();
            panel.Close();
        }
    });

    private static T? Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);

    private static string LineText(Control root) =>
        string.Concat(Tagged<TextBlock>(root, "notice-line")!.Inlines!.OfType<Run>().Select(r => r.Text));

    private static void Press(Control root, string tag) =>
        Tagged<Button>(root, tag)!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    private static bool Filed(string key) => StartupLadder.Inbox.Items.Any(i => i.Key == key);

    /// <summary>WPF Sink.Poke + FriendNotices: an unowned, non-activating corner notice "poked you: tut" whose
    /// Poke back sends the same preset on the wire; one that runs out unseen waits in the Inbox.</summary>
    [Fact]
    public Task APokePutsUpANoticeWhosePokeBackSendsAndAMissedOneWaitsInTheInbox() => Run(async (wire, panel, _, _) =>
    {
        FriendsLanding.Router!.OnDelivered(Poke("p1", "u_on", "tut"));
        var w = Assert.IsType<FriendNotices>(FriendNotices.Current);
        Assert.Null(w.Owner);
        Assert.False(w.ShowActivated);
        Assert.True(w.Topmost);
        Assert.Equal(Loc.Get("friends_notice_poked") + " " + Loc.Get("friends_poke_tut"), LineText(w));
        Assert.Equal(Loc.Get("friends_notice_poke_back"), ((TextBlock)Tagged<Button>(w, "notice-act")!.Content!).Text);
        Press(w, "notice-act");
        await FriendsDrawerTests.Until(() => wire.Sends.Count == 1);
        Assert.Contains("\"kind\":\"poke\"", wire.Sends[0]);
        Assert.Contains("\"poke\":\"tut\"", wire.Sends[0]);
        Assert.Null(FriendNotices.Current);
        Assert.False(Filed("friends:p1"));

        FriendsLanding.Router!.OnDelivered(Poke("p2", "u_on", "hi"));
        FriendNotices.Current!.Step(FriendNoticeRules.LifetimeMs(NoticeKind.Poke) + 1);
        Assert.Null(FriendNotices.Current);
        Assert.True(Filed("friends:p2"));
    });

    /// <summary>WPF FriendNotices stack: three cards newest on top, "+N more" under them, a repeat from the same
    /// friend folds into "x2", the bar runs down, and a click on the body opens the drawer on that friend.</summary>
    [Fact]
    public Task TheStackShowsThreeFoldsRepeatsAndTheBodyOpensTheDrawer() => Run(async (_, _, opened, _) =>
    {
        for (var i = 0; i < 4; i++) FriendsLanding.Router!.OnDelivered(Poke("s" + i, "u_" + i, "hi"));
        var w = FriendNotices.Current!;
        var cards = w.Column.Children.OfType<Border>().Where(b => b.Tag as string == "friend-notice").ToList();
        Assert.Equal(3, cards.Count);
        Assert.Equal(Loc.GetF("friends_notice_more", 1), Tagged<TextBlock>(w, "notice-more")!.Text);

        FriendsLanding.Router!.OnDelivered(Poke("s4", "u_3", "hi"));
        Assert.Equal(4, w.Notices.Count);
        Assert.Equal(2, w.Notices[0].Count);
        Assert.EndsWith(" x2", LineText(w));

        w.Step(3000);
        var bar = (ScaleTransform)Tagged<Border>(w, "notice-bar")!.RenderTransform!;
        Assert.Equal(0.5, bar.ScaleX, 2);

        w.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // input hit-tests the last rendered frame
        var card = w.Column.Children.OfType<Border>().First();
        var p = card.TranslatePoint(new Point(120, 14), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Assert.Equal(new string?[] { "u_3" }, opened);
        Assert.Equal(3, w.Notices.Count);
        await Task.CompletedTask;
    });

    /// <summary>WPF Sink.RequestAnnounce/RequestRow/RequestGone: a request files its Inbox row and a notice whose
    /// Accept goes to the wire; the row leaves when the request does.</summary>
    [Fact]
    public Task ARequestFilesARowAndANoticeWhoseAcceptGoesToTheWire() => Run(async (wire, _, _, _) =>
    {
        FriendsLanding.Router!.OnRequestArrived(new FriendRequest("u_req", "Ann", null, null, DateTimeOffset.UtcNow));
        Assert.True(Filed("friends-request:u_req"));
        var w = FriendNotices.Current!;
        Assert.Equal(Loc.Get("friends_land_request_line"), LineText(w));
        Press(w, "notice-act");
        await FriendsDrawerTests.Until(() => wire.Ops.Contains("accept"));
        Assert.Contains("accept", wire.Ops);
        FriendsLanding.Router!.OnRequestGone("u_req");
        Assert.False(Filed("friends-request:u_req"));
    });

    /// <summary>WPF FriendsLanding.Tick :66: a hold starting (a session) takes the notices down, and a poke
    /// that was up waits in the Inbox; the exit path closes the window.</summary>
    [Fact]
    public Task AHoldStartingFoldsTheNoticesIntoTheInbox() => Run(async (_, _, _, setHolding) =>
    {
        FriendsLanding.Router!.OnDelivered(Poke("h1", "u_on", "hi"));
        Assert.True(FriendNotices.AnyUp);
        setHolding(true);
        FriendsLanding.Tick();
        Assert.False(FriendNotices.AnyUp);
        Assert.Null(FriendNotices.Current);
        Assert.True(Filed("friends:h1"));
        await Task.CompletedTask;
    });

    /// <summary>WPF FriendsRailChip.OpenDrawer :395: the drawer opens on the friend's card.</summary>
    [Fact]
    public Task TheChipOpensItsDrawerOnAFriend() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var (svc, _) = FriendsDrawerTests.Service();
        await svc.RefreshAsync();
        var chip = new FriendsRailChip(svc);
        var host = new Window { Width = 800, Height = 700, Content = chip };
        try
        {
            host.Show();
            chip.OpenDrawer("u_on");
            Assert.True(chip.IsOpen);
            Assert.Equal("u_on", chip.Drawer.OpenId);
            chip.OpenDrawer("u_on");   // already on it: stays open on it (WPF OpenOn), not a toggle shut
            Assert.Equal("u_on", chip.Drawer.OpenId);
        }
        finally { host.Close(); }
    });
}
