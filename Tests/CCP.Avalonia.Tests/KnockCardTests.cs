using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Windows/Friends/KnockCard.cs + Services/Friends/FriendsLanding.cs (invites): a delivery raised by a
/// fake service (no network) goes through Core's router and the head's landing tick to a card over the panel.</summary>
public sealed class KnockCardTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static InboxItem Invite(string id, string dest = InviteDestination.BackRoom) =>
        new(id, SendKind.Invite, "u_mia", "Mia", null, null, dest, null, null, T0, T0.AddMinutes(5));

    /// <summary>Raises what the real service would; records the receipts it is asked to report.</summary>
    private sealed class FakeFriends : IFriendsService
    {
        public readonly List<ReceiptReport> Receipts = new();
        public bool Available => true;
        public FriendsSnapshot Snapshot => FriendsSnapshot.Empty;
        public bool PresenceShared { get; set; }
        public event Action<FriendsSnapshot>? SnapshotChanged { add { } remove { } }
        public event Action<InboxItem>? Delivered;
        public event Action<SendKind, Friend>? Sent { add { } remove { } }
        public event Action<FriendRequest>? RequestArrived { add { } remove { } }
        public event Action<string>? RequestGone { add { } remove { } }
        public void Deliver(InboxItem item) => Delivered?.Invoke(item);
        public void ReportReceipt(ReceiptReport report) => Receipts.Add(report);
        public Task<SendResult> PokeAsync(string friendId, string pokeId) => throw new NotSupportedException();
        public Task<SendResult> InviteAsync(string friendId, string destination, string? code) => throw new NotSupportedException();
        public Task<SendResult> SendWatchAsync(string friendId, WatchRef watch) => throw new NotSupportedException();
        public Task<AddResult> AddByCodeAsync(string code) => throw new NotSupportedException();
        public Task<ActResult> AcceptAsync(string requesterId) => throw new NotSupportedException();
        public Task<ActResult> DeclineAsync(string requesterId) => throw new NotSupportedException();
        public Task<ActResult> CancelRequestAsync(string targetId) => throw new NotSupportedException();
        public Task<ActResult> RemoveAsync(string friendId) => throw new NotSupportedException();
        public Task<ActResult> BlockAsync(string friendId) => throw new NotSupportedException();
        public Task<ActResult> UnblockAsync(string friendId) => throw new NotSupportedException();
        public Task<ActResult> SetSquelchAsync(string friendId, bool on) => throw new NotSupportedException();
        public Task<ActResult> ReportAsync(string friendId, string reason) => throw new NotSupportedException();
        public void SetActivity(PresenceActivity activity) { }
        public void SetDrawerOpen(bool open) { }
        public Task RefreshAsync() => Task.CompletedTask;
    }

    /// <summary>Runs <paramref name="body"/> with a fake service attached through the landing tick (the startup
    /// path), the panel up as the anchor, a stepped server clock, motion off and every static restored (P02/P08).</summary>
    private static Task Run(Action<FakeFriends, Window, Action<DateTimeOffset>, Func<LandingWorld, LandingWorld>> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (motion, bell, volume) = (s.MotionLevel, s.FriendNotificationsEnabled, s.MasterVolume);
        var (oldSvc, oldClock, oldWorld, oldAnchor) = (FriendsHead.Service, KnockCard.Clock, FriendsLanding.World, FriendsLanding.Anchor);
        var oldOpenTab = FriendsLanding.OpenPanelTab;
        var oldFetch = KnockCard.FetchPicture;
        var now = T0;
        var world = new LandingWorld(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: false);
        var host = new Window { Width = 1000, Height = 700 };
        var svc = new FakeFriends();
        try
        {
            s.MotionLevel = MotionLevel.Off;
            s.FriendNotificationsEnabled = true;
            s.MasterVolume = 0;
            StartupLadder.ResetForTests();
            FriendsSeen.Shared.Clear();
            host.Show();
            KnockCard.Clock = () => now;
            KnockCard.FetchPicture = _ => throw new InvalidOperationException("no network in tests");
            FriendsLanding.World = () => world;
            FriendsLanding.Anchor = () => host;
            FriendsHead.Service = svc;
            FriendsLanding.Tick();
            body(svc, host, t => { now = t; foreach (var c in KnockCard.Up.ToArray()) c.Advance(t); }, w => world = w);
        }
        finally
        {
            FriendsLanding.Stop();
            (FriendsHead.Service, KnockCard.Clock, FriendsLanding.World, FriendsLanding.Anchor) = (oldSvc, oldClock, oldWorld, oldAnchor);
            FriendsLanding.OpenPanelTab = oldOpenTab;
            KnockCard.FetchPicture = oldFetch;
            (s.MotionLevel, s.FriendNotificationsEnabled, s.MasterVolume) = (motion, bell, volume);
            StartupLadder.ResetForTests();
            FriendsSeen.Shared.Clear();
            foreach (var w in host.OwnedWindows.ToArray()) w.Close();
            host.Close();
        }
        return Task.CompletedTask;
    });

    private static void Press(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Text(Control c) => string.Join("|", global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(c).OfType<TextBlock>().Select(t => t.Text));

    [Fact]
    public Task AnInviteKnocksWithANonActivatingCardThatFoldsToTheInboxAndReopens() => Run((svc, host, _, _) =>
    {
        svc.Deliver(Invite("0123456789abcdef"));
        var card = Assert.Single(KnockCard.Up);
        Assert.Contains(card, host.OwnedWindows);
        Assert.True(card.Topmost);
        Assert.False(card.ShowActivated);   // never steals focus
        Assert.Contains("Mia", Text(card));
        Assert.Contains(FriendsLanding.KnockLine(card.Item), Text(card));
        Assert.Equal("5:00", card.Countdown.Text);
        // On screen = seen (WPF :304).
        Assert.Contains(svc.Receipts, r => r.State == ReceiptState.Seen);

        // The x: put away unanswered, it waits in the Inbox; opening the row brings the card back.
        Press(card.LaterButton);
        Assert.Empty(KnockCard.Up);
        var row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "friends:0123456789abcdef");
        StartupLadder.Inbox.Open(row);
        card = Assert.Single(KnockCard.Up);

        // Not now: the sender hears it, the row is gone.
        Press(card.NotNowButton);
        Assert.Empty(KnockCard.Up);
        Assert.Contains(svc.Receipts, r => r.State == ReceiptState.Declined);
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key.StartsWith("friends:"));
    });

    [Fact]
    public Task TheCardTucksAfterItsRingAndRunsOutAtTheInvitesExpiry() => Run((svc, _, step, _) =>
    {
        svc.Deliver(Invite("00000000000000aa"));
        var card = Assert.Single(KnockCard.Up);
        step(T0.AddSeconds(LandingRules.KnockRingSeconds - 1));
        Assert.False(card.IsTucked);
        step(T0.AddSeconds(LandingRules.KnockRingSeconds));
        Assert.True(card.IsTucked);
        step(T0.AddMinutes(5).AddSeconds(-20));
        Assert.Equal("0:20", card.Countdown.Text);
        Assert.Same(FriendsDrawerGold, card.Countdown.Foreground);
        step(T0.AddMinutes(5));
        Assert.Empty(KnockCard.Up);
        // An invite that ran out is not filed: the server tells the sender "no answer".
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key.StartsWith("friends:"));
    });

    private static global::Avalonia.Media.IBrush FriendsDrawerGold => ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer.Gold;

    [Fact]
    public Task CardsStackDownTheTopRightAndPanicOrAHoldFoldsThemIntoTheInbox() => Run((svc, host, _, setWorld) =>
    {
        svc.Deliver(Invite("00000000000000b1"));
        svc.Deliver(Invite("00000000000000b2", InviteDestination.Goon));
        Assert.Equal(2, KnockCard.Up.Count);
        var (a, b) = (KnockCard.Up[0], KnockCard.Up[1]);
        Assert.Equal(a.Position.X, b.Position.X);
        // WPF's 12 px card margin is the gap between windows here, not transparent window area (no click-through).
        Assert.Equal(a.Position.Y + (int)Math.Round(a.ClientSize.Height + 12), b.Position.Y);
        Assert.Equal(232, a.ClientSize.Width);
        Assert.Equal(((Control)a.Content!).Bounds.Height, a.ClientSize.Height);   // no transparent strip in the window
        Assert.True(a.Position.X > host.Position.X + 500, $"right-hand side: {a.Position}");

        PanicSurfaces.All.Single(x => x.Id == "knock-cards").Stop(null);
        Assert.Empty(KnockCard.Up);
        Assert.Equal(2, StartupLadder.Inbox.Items.Count(i => i.Key.StartsWith("friends:")));

        // A session starting folds a card already up on the next landing tick; a delivery during it is held.
        StartupLadder.Inbox.Open(StartupLadder.Inbox.Items.First(i => i.Key == "friends:00000000000000b1"));
        Assert.Single(KnockCard.Up);
        setWorld(new LandingWorld(false, false, false, true, false, false, SessionRunning: true));
        FriendsLanding.Tick();
        Assert.Empty(KnockCard.Up);
        svc.Deliver(Invite("00000000000000b3"));
        Assert.Empty(KnockCard.Up);
        setWorld(new LandingWorld(false, false, false, true, false, false));
        FriendsLanding.Tick();
        Assert.Equal("00000000000000b3", Assert.Single(KnockCard.Up).Item.Id);
    });

    /// <summary>Join only where this head has a host: a Join that opened nothing would tell the sender "joined".
    /// Ramp's Join opens the panel on the Studio tab (WPF Join :545 OpenTab) and reports Joined.</summary>
    [Fact]
    public Task JoinOnlyWhereThisHeadCanOpenTheDestination() => Run((svc, _, _, _) =>
    {
        var tabs = new List<string>();
        FriendsLanding.OpenPanelTab = tabs.Add;
        svc.Deliver(Invite("00000000000000c1"));
        var backRoom = Assert.Single(KnockCard.Up);
        Assert.Null(backRoom.GoButton);
        // Even a Go that reaches it (Enter on a stale card) never says joined: it waits in the Inbox.
        backRoom.Fold(KnockOutcome.Go);
        Assert.DoesNotContain(svc.Receipts, r => r.State == ReceiptState.Joined);
        Assert.Contains(StartupLadder.Inbox.Items, i => i.Key == "friends:00000000000000c1");
        Assert.Empty(tabs);

        svc.Deliver(Invite("00000000000000c2", InviteDestination.Ramp));
        var ramp = Assert.Single(KnockCard.Up);
        Press(ramp.GoButton!);
        Assert.Empty(KnockCard.Up);
        Assert.Contains(svc.Receipts, r => r.State == ReceiptState.Joined && r.Key.Contains("00000000000000c2"));
        Assert.Equal(new[] { "studio" }, tabs);
    });

    /// <summary>WPF LandingChrome.Avatar: a proxy path ("/...") is fetched from the proxy and laid over the
    /// initials; anything else (an absolute URL a sender could point anywhere) keeps the initials and fetches nothing.</summary>
    [Fact]
    public Task TheSendersProxyPictureCoversTheInitialsAndNothingElseIsFetched() => Run((svc, _, _, _) =>
    {
        byte[] png;
        using (var s = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + LandingRules.KnockArt(Invite("x", InviteDestination.Ramp)))))
        using (var ms = new MemoryStream()) { s.CopyTo(ms); png = ms.ToArray(); }
        var urls = new List<string>();
        KnockCard.FetchPicture = url => { urls.Add(url); return Task.FromResult(png); };

        svc.Deliver(Invite("00000000000000d1") with { FromAvatarUrl = "/friends/avatar/u_mia.png" });
        var card = Assert.Single(KnockCard.Up);
        Assert.Equal(new[] { "https://codebambi-proxy.vercel.app/friends/avatar/u_mia.png" }, urls);
        Assert.NotNull(card.AvatarFace);
        Assert.Equal(1, card.AvatarFace!.Opacity);
        Assert.IsType<global::Avalonia.Media.ImageBrush>(card.AvatarFace.Fill);
        Assert.Contains("M", Text(card));   // the initials stay underneath
        card.Fold(KnockOutcome.Later);

        svc.Deliver(Invite("00000000000000d2") with { FromAvatarUrl = "https://elsewhere.example/a.png" });
        Assert.Null(Assert.Single(KnockCard.Up).AvatarFace);
        Assert.Single(urls);
    });

    /// <summary>WPF MakeButton :210: every card button lifts to 1.02 under the pointer and settles back.</summary>
    [Fact]
    public Task TheCardsButtonsLiftUnderThePointer() => Run((svc, _, _, _) =>
    {
        svc.Deliver(Invite("00000000000000e1", InviteDestination.Ramp));
        var card = Assert.Single(KnockCard.Up);
        card.UpdateLayout();
        foreach (var b in new[] { card.GoButton!, card.NotNowButton, card.LaterButton })
        {
            var scale = Assert.IsType<global::Avalonia.Media.ScaleTransform>(b.RenderTransform);
            var at = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), card)!.Value;
            card.MouseMove(at);
            Assert.Equal(1.02, scale.ScaleX, 3);
            Assert.Equal(1.02, scale.ScaleY, 3);
            card.MouseMove(new Point(2, 60));
            Assert.Equal(1, scale.ScaleX, 3);
            Assert.Null(scale.Transitions);   // Motion Off snaps
        }

        // Motion on: the lift eases over WPF's 150 ms quad-out (MotionFx.cs:74-85); Run restores the level.
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var go = card.GoButton!;
        var lift = Assert.IsType<global::Avalonia.Media.ScaleTransform>(go.RenderTransform);
        card.MouseMove(go.TranslatePoint(new Point(go.Bounds.Width / 2, go.Bounds.Height / 2), card)!.Value);
        Assert.NotNull(lift.Transitions);
        Assert.Equal(2, lift.Transitions!.Count);
        Assert.All(lift.Transitions.OfType<global::Avalonia.Animation.DoubleTransition>(), t =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(150), t.Duration);
            Assert.IsType<global::Avalonia.Animation.Easings.QuadraticEaseOut>(t.Easing);
        });
    });

    /// <summary>P04/P11: startup starts the landing on the friends service; exit stops it before the poll goes.</summary>
    [Fact]
    public void AppStartsTheLandingWithTheFriendsServiceAndStopsItOnExit()
    {
        var src = Regex.Replace(File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);
        Assert.Matches(@"friends\.Start\([\s\S]{0,900}Platform\.FriendsLanding\.Start\(\)", src);
        Assert.Matches(@"Platform\.FriendsLanding\.Stop\(\)[^\n]*\n[^\n]*FriendsHead\.Service as IDisposable", src);
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
