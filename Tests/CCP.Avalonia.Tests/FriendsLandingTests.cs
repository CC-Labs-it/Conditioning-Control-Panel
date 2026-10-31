using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Services/Friends/FriendsLanding.cs on this head: the words a delivery or a send throws
/// through FloatingWord, routed by the Core FriendsLandingRouter over the real FriendsService.</summary>
public sealed class FriendsLandingTests
{
    private static readonly Color Pink = Color.FromRgb(0xFF, 0x5F, 0xB4), Mint = Color.FromRgb(0x5F, 0xFF, 0xD0);

    private static void Platform()
    {
        if (global::Avalonia.Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    /// <summary>Points the landing at <paramref name="svc"/> with the panel up as <paramref name="panel"/>;
    /// the returned action puts every seam back (P02).</summary>
    private static Action Seams(IFriendsService svc, Window? panel, Window? game, List<(string Line, string Face)> emi, bool holding = false)
    {
        var (world, anchor, active, says, service) = (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.EmiSays, FriendsLanding.Service);
        var bell = CoreSettings.Current.FriendNotificationsEnabled;
        FriendsLanding.World = () => new LandingWorld(false, false, false, PanelVisible: true, LauncherVisible: false, GameHostActive: game != null, SessionRunning: holding);
        FriendsLanding.Anchor = () => panel;
        FriendsLanding.ActiveGameWindow = () => game;
        FriendsLanding.EmiSays = (l, f) => emi.Add((l, f));
        FriendsLanding.Service = () => svc;
        FriendsLanding.Tick();
        return () =>
        {
            FriendsLanding.Stop();
            (FriendsLanding.World, FriendsLanding.Anchor, FriendsLanding.ActiveGameWindow, FriendsLanding.EmiSays, FriendsLanding.Service) = (world, anchor, active, says, service);
            CoreSettings.Current.FriendNotificationsEnabled = bell;
        };
    }

    private static TextBlock Word(Window owner) =>
        Assert.Single(owner.OwnedWindows.OfType<FloatingWord>()).GetLogicalDescendants().OfType<TextBlock>().Single();

    private static void CloseOwned(params Window[] ws)
    {
        foreach (var w in ws) { foreach (var o in w.OwnedWindows.ToArray()) o.Close(); w.Close(); }
    }

    private static InboxItem Poke(string id, string poke) =>
        new(id, SendKind.Poke, "u_on", "Mia", null, poke, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1));

    /// <summary>WPF Sink.Poke: Emi says "{name} says {word}" with the poke's face and the word floats over
    /// the panel, pink for a tease; with the bell off, nothing.</summary>
    [Fact]
    public Task ALandedPokeThrowsItsWordAndEmisLineOverThePanel() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, _) = FriendsDrawerTests.Service();
        var panel = new Window { Width = 1000, Height = 700 };
        var emi = new List<(string Line, string Face)>();
        var restore = Seams(svc, panel, null, emi);
        try
        {
            panel.Show();
            CoreSettings.Current.FriendNotificationsEnabled = true;
            FriendsLanding.Router!.OnDelivered(Poke("p1", "tut"));
            var word = Word(panel);
            Assert.Equal(Loc.Get("friends_poke_tut"), word.Text);
            Assert.Equal(Pink, ((ISolidColorBrush)word.Foreground!).Color);
            Assert.Equal(34, word.FontSize);
            Assert.Equal((string.Format(Loc.Get("friends_land_emi_poke"), "Mia", word.Text), "\u00AC_\u00AC"), Assert.Single(emi));
            foreach (var o in panel.OwnedWindows.ToArray()) o.Close();

            FriendsLanding.Router!.OnDelivered(Poke("p2", "hi"));
            Assert.Equal(Mint, ((ISolidColorBrush)Word(panel).Foreground!).Color);
            foreach (var o in panel.OwnedWindows.ToArray()) o.Close();

            CoreSettings.Current.FriendNotificationsEnabled = false;
            FriendsLanding.Router!.OnDelivered(Poke("p3", "tut"));
            Assert.Empty(panel.OwnedWindows);
            Assert.Equal(2, emi.Count);
        }
        finally { restore(); CloseOwned(panel); }
        await Task.CompletedTask;
    });

    // WPF FriendsLanding.cs:230-257,475-490: game-only notice, missed Inbox, reopen and expiry.
    [Fact]
    public Task PokeNoticeOverGameFilesUnseenAndReopensFromInbox() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, _) = FriendsDrawerTests.Service();
        var panel = new Window();
        var game = new Window();
        var emi = new List<(string Line, string Face)>();
        var restore = Seams(svc, panel, game, emi);
        try
        {
            panel.Show(); game.Show();
            CoreSettings.Current.FriendNotificationsEnabled = true;
            FriendsLanding.Router!.OnDelivered(Poke("game-poke", "hi"));
            var w = FriendNotices.Current!;
            Assert.Equal(NoticeKind.Poke, Assert.Single(w.Stack.All).Kind);
            Assert.Empty(panel.OwnedWindows); Assert.Empty(game.OwnedWindows); Assert.Empty(emi);
            w.Advance(100000);
            var row = Assert.Single(StartupLadder.Inbox.Items, r => r.Key == "friends:game-poke");
            StartupLadder.Inbox.Open(row);
            Assert.NotNull(FriendNotices.Current);
            Assert.Single(emi);
            Assert.Equal(Loc.Get("friends_poke_hi"), Word(panel).Text);
            FriendNotices.CloseAll();
            CoreSettings.Current.FriendNotificationsEnabled = false;
            FriendsLanding.Router.OnDelivered(Poke("silent-poke", "hi"));
            Assert.Null(FriendNotices.Current);
            Assert.True(StartupLadder.Inbox.Contains("friends:silent-poke"));
            StartupLadder.Inbox.Open(StartupLadder.Inbox.Items.Single(r => r.Key == "friends:silent-poke"));
            Assert.Null(FriendNotices.Current); // WPF reopen uses the poke arrival path, including the bell.
            CoreSettings.Current.FriendNotificationsEnabled = true;
            var expired = Poke("expired-poke", "hi") with { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
            FriendsLanding.AnnouncePoke(expired, true);
            FriendNotices.Current!.Advance(100000);
            StartupLadder.Inbox.Open(StartupLadder.Inbox.Items.Single(r => r.Key == "friends:expired-poke"));
            Assert.Null(FriendNotices.Current);
            Assert.Equal((Loc.Get("friends_land_too_late"), ";_;"), emi.Last());
        }
        finally { restore(); CloseOwned(panel, game); }
        await Task.CompletedTask;
    });

    // WPF FriendsLanding.cs:245-248,419-433: real notice action and body navigation.
    [Fact]
    public Task PokeNoticeActionSendsSamePokeAndBodyOpensSender() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, wire) = FriendsDrawerTests.Service();
        var panel = new Window();
        var restore = Seams(svc, panel, null, new());
        var drawer = FriendsLanding.OpenDrawer;
        string? opened = null;
        try
        {
            panel.Show(); await svc.RefreshAsync();
            CoreSettings.Current.FriendNotificationsEnabled = true;
            FriendsLanding.OpenDrawer = id => opened = id;
            FriendsLanding.Router!.OnDelivered(Poke("action-poke", "tut"));
            var w = FriendNotices.Current!;
            w.Dismiss(Assert.Single(w.Stack.All), true);
            await FriendsDrawerTests.Until(() => wire.Sends.Count == 1);
            Assert.Contains("\"poke\":\"tut\"", Assert.Single(wire.Sends));
            Assert.Contains("\"to\":\"u_on\"", wire.Sends[0]);
            FriendsLanding.Router.OnDelivered(Poke("body-poke", "hi"));
            w = FriendNotices.Current!;
            w.Dismiss(Assert.Single(w.Stack.All), false, open: true);
            Assert.Equal("u_on", opened);
            Assert.False(StartupLadder.Inbox.Contains("friends:body-poke"));
        }
        finally { FriendsLanding.OpenDrawer = drawer; restore(); CloseOwned(panel); }
    });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public Task PokeBackDefaultsToHi(string? poke) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, wire) = FriendsDrawerTests.Service();
        var restore = Seams(svc, null, null, new());
        try
        {
            await svc.RefreshAsync();
            await FriendsLanding.PokeBack(Poke("fallback", poke!));
            Assert.Contains("\"poke\":\"hi\"", Assert.Single(wire.Sends));
            await FriendsLanding.PokeBack(Poke("no-sender", "hi") with { FromId = "" });
            Assert.Single(wire.Sends);
        }
        finally { restore(); }
    });

    // WPF FriendsLanding.cs:427-431: a refused send needs words; successful sends use SentBeat.
    [Fact]
    public Task PokeBackRefusalShowsTheCoreResult() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, wire) = FriendsDrawerTests.Service();
        var panel = new Window();
        var restore = Seams(svc, panel, null, new());
        try
        {
            panel.Show(); await svc.RefreshAsync();
            await FriendsLanding.PokeBack(Poke("first", "hi"));
            foreach (var o in panel.OwnedWindows.ToArray()) o.Close();
            await FriendsLanding.PokeBack(Poke("second", "hi"));
            Assert.Single(wire.Sends);
            Assert.Equal(Loc.Get(FriendsDrawerRules.SendResultKey(SendResult.TooFast)), Word(panel).Text);
            Assert.Equal(Pink, ((ISolidColorBrush)Word(panel).Foreground!).Color);
        }
        finally { restore(); CloseOwned(panel); }
    });

    /// <summary>WPF Sink.SentBeat: a good poke from the drawer leaves a small mint "sent" over the panel;
    /// nothing while a session holds the screen.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AGoodSendLeavesTheSentBeatOverThePanel(bool holding) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, wire) = FriendsDrawerTests.Service();
        var panel = new Window { Width = 1000, Height = 700 };
        var restore = Seams(svc, panel, null, new(), holding);
        try
        {
            panel.Show();
            await svc.RefreshAsync();
            var d = new FriendsRailChip(svc).Drawer;
            d.OnOpened();   // open: the row words the result, so the only word outside is the beat
            Assert.Equal(SendResult.Sent, await d.PokeAsync("u_on", PokeSet.Shipped[0]));
            Assert.Single(wire.Sends);
            if (holding) { Assert.Empty(panel.OwnedWindows); return; }
            var word = Word(panel);
            Assert.Equal(Loc.Get("friends_land_poke_sent"), word.Text);
            Assert.Equal(Mint, ((ISolidColorBrush)word.Foreground!).Color);
            Assert.Equal(22, word.FontSize);
        }
        finally { restore(); CloseOwned(panel); }
    });

    /// <summary>WPF App.xaml.cs:3168: the landing starts on the friends poll and stops on exit (P04, P23).</summary>
    [Fact]
    public void AppStartupStartsTheLandingOnTheFriendsPollAndStopsItOnExit() => ScanApp();

    private static void ScanApp([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", ".."));
        var src = System.Text.RegularExpressions.Regex.Replace(
            System.IO.File.ReadAllText(System.IO.Path.Combine(root, "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.Matches(@"friends\.Start\([^;]*;[^}]*\}[^}]*\}\s*Platform\.FriendsLanding\.Start\(\);", src);
        Assert.Contains("Platform.FriendsLanding.Stop()", src);
    }

    /// <summary>WPF FriendsLanding.Say: a word from outside the drawer goes over the active game first,
    /// pink when it is bad news.</summary>
    [Fact]
    public Task ATellGoesOverTheActiveGameBeforeThePanel() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Platform();
        var (svc, _) = FriendsDrawerTests.Service();
        var panel = new Window { Width = 1000, Height = 700 };
        var game = new Window { Width = 800, Height = 600 };
        var restore = Seams(svc, panel, game, new());
        try
        {
            panel.Show();
            game.Show();
            FriendsLanding.Tell("nope", good: false);
            Assert.Empty(panel.OwnedWindows);
            Assert.Equal(Pink, ((ISolidColorBrush)Word(game).Foreground!).Color);
        }
        finally { restore(); CloseOwned(panel, game); }
        await Task.CompletedTask;
    });
}
