using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LobbyPageTests
{
    private static T Named<T>(Control parent, string name) where T : Control => parent.FindControl<T>(name)!;

    [Fact]
    public void EmptyLobbyHasNoFabricatedRowsAndNarrowButtonsSwitchColumns() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        Assert.Empty(Named<ItemsControl>(page, "AvailableSubjectsList").Items);
        Assert.Equal(Loc.Get("lobby_signed_out"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
        page.SetNarrow(true);
        Assert.True(Named<StackPanel>(page, "LobbyTabStrip").IsVisible);
        Named<Button>(page, "TabFriends").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, page.SelectedColumn);
        Assert.False(Named<Border>(page, "LobbyOpenColumn").IsVisible);
        Assert.True(Named<Border>(page, "LobbyFriendsSection").IsVisible);
        Assert.Equal(3, Grid.GetColumnSpan(Named<Border>(page, "LobbyFriendsSection")));
        page.SetNarrow(false);
        foreach (var (name, index) in new[] { ("LobbyOpenColumn", 0), ("LobbyPlayingSection", 1), ("LobbyFriendsSection", 2) })
        {
            var column = Named<Border>(page, name);
            Assert.True(column.IsVisible);
            Assert.Equal(index, Grid.GetColumn(column));
            Assert.Equal(1, Grid.GetColumnSpan(column));
        }
        Assert.Equal(0.6, Named<Button>(page, "TabOpen").Opacity);
        Assert.Equal(1, Named<Button>(page, "TabFriends").Opacity);
    });

    [Fact]
    public void EveryLobbyPillHasATemplateAndDrawsAtANonZeroSize() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        var window = new Window { Width = 900, Height = 700, Content = page };
        window.Show();
        try
        {
            page.SetNarrow(true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var names = new[] { "BtnHostChess", "BtnHostGoon", "BtnBecomeASubject", "TabOpen", "TabPlaying", "TabFriends", "BtnEmptyHostChess", "BtnEmptyHostGoon" };
            foreach (var name in names)
            {
                var pill = Named<Button>(page, name);
                Assert.True(pill.Template is not null, name + " has no Template");
                Assert.True(pill.Bounds.Width > 0 && pill.Bounds.Height > 0, name + " has zero size " + pill.Bounds);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void SnapshotPaintsAllThreeListsCountsErrorsAndRemoteGate() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        var open = new LobbyRow { Game = LobbyGame.Remote, Key = "r", HostName = "Remote", RemoteTier = "light" };
        var playing = new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Playing, HostName = "A", OpponentName = "B", InitialMs = 300000 };
        var friend = open with { Key = "friend", Friend = true };
        page.PaintSnapshot(new LobbySnapshot(new[] { open }, new[] { playing }, new[] { friend }, true), LobbyGates.From(true, false, false), true);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyOpenCount").Text);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyPlayingCount").Text);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyFriendsCount").Text);
        Assert.Equal(Loc.GetF("lobby_open_count", 2), Named<TextBlock>(page, "TxtLobbyCount").Text);
        Assert.False(Named<Border>(page, "AvailableSubjectsEmptyPanel").IsVisible);
        Assert.True(Named<Border>(page, "AvailableSubjectsErrorPanel").IsVisible);
        Assert.True(Named<TextBlock>(page, "TxtBecomeASubjectSubtitle").IsVisible);
        var row = Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "LobbyPlayingList").Items.Single());
        Assert.Equal("A vs B", row.Title);
        Assert.False(row.HasButton);
        Assert.Contains("5+0", row.Line);
        Assert.True(Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "LobbyFriendsList").Items.Single()).Friend);
    });

    [Fact]
    public void EntitlementEventRepaintsHostLocksAndSignOutClearsRoster() => Run(() =>
    {
        var shell = new MainShellWindow();
        try
        {
            var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
            page.ReadGates = () => LobbyGates.From(true, false, false);
            page.PaintSnapshot(new LobbySnapshot(new[] { new LobbyRow { Key = "r", HostName = "Subject" } },
                Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), page.ReadGates());
            Assert.True(Named<Control>(page, "GoonHostLock").IsVisible);
            page.ReadGates = () => LobbyGates.From(true, true, true);
            shell.RefreshEntitlementVeils();
            Assert.False(Named<Control>(page, "GoonHostLock").IsVisible);
            page.ReadGates = () => LobbyGates.From(false, false, false);
            shell.RefreshEntitlementVeils();
            Assert.Empty(Named<ItemsControl>(page, "AvailableSubjectsList").Items);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public void SignedOutChessRowAsksForSignIn() => Run(() =>
    {
        var shell = new MainShellWindow();
        shell.Show();
        var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
        page.ReadGates = () => LobbyGates.From(false, false, false);
        var button = new Button { DataContext = new LobbyCardView(new LobbyRow { Game = LobbyGame.Chess, Key = "p_table" }, page.ReadGates()) };
        try
        {
            typeof(AvailableSubjectsTabView).GetMethod("BtnLobbyRow_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, new object[] { button, new RoutedEventArgs(Button.ClickEvent) });
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(shell.OwnedWindows, w => w is LoginDialog);
        }
        finally { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); }
    });

    private sealed class LobbyTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick { add { } remove { } }
        public bool Running;
        public int Starts;
        public void Start() { Running = true; Starts++; }
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
    }

    [Fact]
    public void OpeningHidingAndDetachingTabOwnsDiscoveryLeaseAndPaintsRealSnapshot() => Run(() =>
    {
        var timer = new LobbyTimer();
        var calls = 0;
        var service = new LobbyService(timer, () => true, () => Array.Empty<Friend>(),
            () => { calls++; return Task.FromResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_table", "Live", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)); },
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((System.Collections.Generic.IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        var page = new AvailableSubjectsTabView { CreateLobby = () => service, IsVisible = false };
        page.ReadGates = () => LobbyGates.From(true, false, false);
        var window = new Window { Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(timer.Running);
            page.IsVisible = true;
            Assert.True(timer.Running);
            Assert.Equal("Live", Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "AvailableSubjectsList").Items.Single()).Title);
            Assert.Equal(1, calls);
            page.IsVisible = false; Assert.False(timer.Running);
            page.IsVisible = true; Assert.Equal(2, calls);
            window.Content = null; Assert.False(timer.Running);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ReopeningTheTabAfterAFailedRoundKeepsTheErrorPanel() => Run(() =>
    {
        var timer = new LobbyTimer();
        var service = new LobbyService(timer, () => true, () => Array.Empty<Friend>(),
            () => Task.FromResult(new PbpLobbyReply(Array.Empty<PbpOpenSeat>(), Array.Empty<PbpPlayingGame>(), false)),
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((System.Collections.Generic.IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), false)));
        var page = new AvailableSubjectsTabView { CreateLobby = () => service, IsVisible = false };
        page.ReadGates = () => LobbyGates.From(true, false, false);
        var window = new Window { Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            page.IsVisible = true;
            Assert.False(service.LastRoundOk);
            page.Repaint(); // tier change / reopen path: repaints without a new round

            Assert.True(Named<Border>(page, "AvailableSubjectsErrorPanel").IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void HostLocksFollowTheirOwnGates() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        foreach (var signedIn in new[] { false, true })
        foreach (var premium in new[] { false, true })
        {
            var gates = LobbyGates.From(signedIn, premium, premium);
            page.PaintSnapshot(LobbySnapshot.Empty, gates);
            foreach (var game in new[] { LobbyGame.Chess, LobbyGame.Goon, LobbyGame.Remote })
            {
                var name = game.ToString();
                Assert.Equal(!gates.CanHost(game), Named<Control>(page, name + "HostLock").IsVisible);
                Assert.Equal(gates.CanHost(game) ? 1 : 0.7,
                    Named<Button>(page, game == LobbyGame.Remote ? "BtnBecomeASubject" : "BtnHost" + name).Opacity);
            }
        }
    });

    [Fact]
    public void ShowingTheTabAgainRedrawsSignInGateAndLanguage() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        page.ReadGates = () => LobbyGates.From(false, false, false);
        page.IsVisible = false;
        page.IsVisible = true;
        Assert.Equal(Loc.Get("lobby_signed_out"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
        page.IsVisible = false;
        page.ReadGates = () => LobbyGates.From(true, false, false);
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            page.IsVisible = true;
            Assert.Equal(Loc.Get("lobby_empty_open"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
            Assert.True(Named<TextBlock>(page, "TxtBecomeASubjectSubtitle").IsVisible);
            Assert.Equal(Loc.Get("lobby_list_open") + "  0", Named<Button>(page, "TabOpen").Content);
        }
        finally { LocalizationManager.Instance.SetLanguage("en"); }
    });

    [Theory]
    [InlineData("BtnBecomeASubject")]
    [InlineData("BtnHostChess")]
    [InlineData("BtnEmptyHostChess")]
    public void SignedOutHostButtonOpensSignIn(string button) => Run(() =>
    {
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
            page.ReadGates = () => LobbyGates.From(false, false, false);
            shell.ShowTab("availablesubjects");
            Named<Button>(page, button).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(shell.OwnedWindows, w => w is LoginDialog);
            Assert.True(page.IsVisible);
        }
        finally
        {
            foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
            shell.Close();
        }
    });

    [Fact]
    public void RemoteHostButtonChecksGateThenOpensConsentPageWithoutStartingSession() => Run(() =>
    {
        var premium = CoreEntitlement.HasPremiumProvider;
        var free = CoreEntitlement.IsFreeTodayProvider;
        var refusal = CoreEntitlement.ShowDeniedHandler;
        var denied = 0;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
            page.ReadGates = () => LobbyGates.From(true, false, false);
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreEntitlement.ShowDeniedHandler = _ => denied++;
            shell.ShowTab("availablesubjects");
            Named<Button>(page, "BtnBecomeASubject").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, denied);
            Assert.True(page.IsVisible);
            CoreEntitlement.IsFreeTodayProvider = key => key == "remote";
            Named<Button>(page, "BtnBecomeASubject").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Named<RemoteControlTabView>(shell, "RemoteControlTab").IsVisible);
            Assert.False(page.IsVisible);
            Assert.False(Named<CheckBox>(Named<RemoteControlTabView>(shell, "RemoteControlTab"), "ChkRemoteControlEnabled").IsChecked == true);
        }
        finally
        {
            foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
            shell.Close();
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.IsFreeTodayProvider = free;
            CoreEntitlement.ShowDeniedHandler = refusal;
        }
    });

    [Fact]
    public void ProductionPageAndShellResolveTheSameAppOwnedService() => Run(() =>
    {
        var shell = new MainShellWindow();
        try
        {
            var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
            var shared = shell.GetLobby();
            Assert.Same(shared, page.GetLobby());
            LobbyHead.Stop();
            Assert.NotSame(shared, page.GetLobby());
            Assert.Same(page.GetLobby(), shell.GetLobby());
        }
        finally { shell.Close(); LobbyHead.Stop(); }
    });

    // WPF MainWindow.Lobby.cs:38-68,84-102: both consumers lease the same service.
    [Fact]
    public void SharedPageAndRailKeepOneTimerAndReleaseOnlyTheirOwnLeases() => Run(() =>
    {
        var timer = new LobbyTimer();
        var signedIn = true;
        using var service = new LobbyService(timer, () => signedIn, () => Array.Empty<Friend>(),
            () => Task.FromResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_shared", "Shared", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)),
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((System.Collections.Generic.IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        var shell = new MainShellWindow { GetLobby = () => service };
        var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
        page.GetLobby = () => service;
        page.ReadGates = () => LobbyGates.From(signedIn, false, false);
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(timer.Running); // badge runs while a different tab is selected
            Assert.Equal(1, timer.Starts);
            Assert.Equal(1, ConditioningControlPanel.Services.UI.NavBadges.Get(ConditioningControlPanel.Services.UI.NavSections.Social));
            shell.ShowTab("availablesubjects");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Shared", Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "AvailableSubjectsList").Items.Single()).Title);
            Assert.Equal(1, timer.Starts); // page did not create a second timer
            shell.WindowState = WindowState.Minimized;
            Assert.True(timer.Running); // page still owns its independent lease, as in WPF
            shell.ShowTab("settings");
            Assert.False(timer.Running);
            shell.WindowState = WindowState.Normal;
            Assert.True(timer.Running);
            Assert.Equal(2, timer.Starts);
            shell.Hide();
            Assert.False(timer.Running);
            shell.Show();
            Assert.True(timer.Running);
            signedIn = false;
            service.RefreshAsync().GetAwaiter().GetResult();
            Assert.Equal(0, ConditioningControlPanel.Services.UI.NavBadges.Get(ConditioningControlPanel.Services.UI.NavSections.Social));
            shell.Close();
            Assert.False(timer.Running);
            ConditioningControlPanel.Services.UI.NavBadges.Set(ConditioningControlPanel.Services.UI.NavSections.Social, 7);
            service.RefreshAsync().GetAwaiter().GetResult();
            Assert.Equal(7, ConditioningControlPanel.Services.UI.NavBadges.Get(ConditioningControlPanel.Services.UI.NavSections.Social)); // closed shell unsubscribed
        }
        finally
        {
            shell.Close();
            ConditioningControlPanel.Services.UI.NavBadges.Set(ConditioningControlPanel.Services.UI.NavSections.Social, 0);
        }
    });

    [Fact]
    public void DetachingSharedPageKeepsRailServiceAliveAndReattachPaintsCachedSnapshot() => Run(() =>
    {
        var timer = new LobbyTimer();
        var pending = new TaskCompletionSource<PbpLobbyReply>();
        var first = true;
        using var service = new LobbyService(timer, () => true, () => Array.Empty<Friend>(),
            () => { if (!first) return pending.Task; first = false; return Task.FromResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_cached", "Cached", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)); },
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((System.Collections.Generic.IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        using var rail = service.Watch();
        var page = new AvailableSubjectsTabView { GetLobby = () => service };
        page.ReadGates = () => LobbyGates.From(true, false, false);
        var window = new Window { Content = page };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cached", Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "AvailableSubjectsList").Items.Single()).Title);
            window.Content = null;
            Assert.True(timer.Running); // page must not dispose the app-owned service
            window.Content = page;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Cached", Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "AvailableSubjectsList").Items.Single()).Title);
            Assert.Equal(1, timer.Starts);
        }
        finally { window.Close(); pending.TrySetResult(PbpLobbyReply.Empty); }
    });

    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        body();
        Dispatcher.UIThread.RunJobs();
    });
}
