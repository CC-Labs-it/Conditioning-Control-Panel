using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Launcher slice 1: the shell's back-to-client door, the cards this head can open, and
/// the close rule (WPF LauncherHost, decided by Core LauncherRules).</summary>
public sealed class LauncherWindowTests
{
    private static void Run(Action<MainShellWindow> body, bool pumpUntilIdle = true)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            // A welcomed, 18+-accepted install: the age gate would exit a panel it finds hidden.
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            // Routing tests: motion off, so a hide is not held for the exit beat and the fade (LauncherFxTests cover those).
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            var (oldIn, oldLab) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider);
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                if (pumpUntilIdle) Dispatcher.UIThread.RunJobs();
                body(shell);
            }
            finally
            {
                LauncherWindow.Instance?.Close();
                LauncherWindow.Boot = BootDecision.PanelFirst;
                ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
                LockdownService.Current = null;
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider) = (oldIn, oldLab);
                shell.Close();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private sealed class LobbyTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void OnUiThread(Action action) => action();
    }

    private static Button JoinButton(MenuItem row) =>
        (Button)((Grid)((Border)row.Header!).Child!).Children[2];

    [Fact]
    public void LobbyChipRefreshesOnShowAndOpensLobby() => Run(shell =>
    {
        var calls = 0;
        var launcher = new LauncherWindow();
        typeof(LauncherWindow).GetField("_panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(launcher, shell);
        launcher.CreateLobby = () => new LobbyPoll(new LobbyTimer(), () => true, () => Array.Empty<Friend>(),
            () => { calls++; return Task.FromResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_table", "Host", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)); },
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        try
        {
            launcher.Show();
            Assert.Equal(1, calls);
            Assert.Equal(Loc.GetF("launcher_lobby_open", 1), launcher.FindControl<TextBlock>("LobbyChipText")!.Text);
            launcher.Hide();
            launcher.Show();
            Assert.Equal(2, calls);
            launcher.FindControl<Button>("LobbyChip")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(launcher.IsVisible);
            var menu = launcher.FindControl<Button>("LobbyChip")!.ContextMenu!;
            Assert.True(menu.IsOpen);
            var card = Assert.IsType<Border>(((MenuItem)menu.Items[0]!).Header);
            var grid = Assert.IsType<Grid>(card.Child);
            Assert.NotNull(Assert.IsType<Image>(grid.Children[0]).Source);
            var words = Assert.IsType<StackPanel>(grid.Children[1]);
            Assert.Contains(words.Children.OfType<TextBlock>(), text => text.Text!.Contains("Host"));
            Assert.IsType<Button>(grid.Children[2]);
            Assert.True(calls >= 3);
            ((MenuItem)menu.Items[1]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("availablesubjects", shell.CurrentTab);
            Assert.False(launcher.IsVisible);
        }
        finally { launcher.Close(); }
    });

    [Fact]
    public void LobbyCardsHaveGlassShellHoverLiftAndJoinTravel() => Run(shell =>
    {
        var launcher = new LauncherWindow();
        launcher.CreateLobby = () => new LobbyPoll(new LobbyTimer(), () => true, () => Array.Empty<Friend>(),
            () => Task.FromResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_table", "Host", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)),
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        try
        {
            launcher.Show();
            var chip = launcher.FindControl<Button>("LobbyChip")!;
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = chip.ContextMenu!;
            Assert.Equal(global::Avalonia.Media.Color.Parse("#F2160B22"), Assert.IsType<global::Avalonia.Media.SolidColorBrush>(menu.Background).Color);
            var row = (MenuItem)menu.Items[0]!;
            var frame = (Border)row.Header!;
            var scale = Assert.IsType<global::Avalonia.Media.ScaleTransform>(frame.RenderTransform);
            var pointer = new Pointer(1, PointerType.Mouse, true);
            frame.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, frame, pointer, launcher, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(1.02, scale.ScaleX);
            frame.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, frame, pointer, launcher, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(1, scale.ScaleX);
            var join = JoinButton(row);
            var travel = Assert.IsType<global::Avalonia.Media.TranslateTransform>(join.RenderTransform);
            join.RaiseEvent(new PointerPressedEventArgs(join, pointer, launcher, default, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            Assert.Equal(3, travel.Y);
            join.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, join, pointer, launcher, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(0, travel.Y);
        }
        finally { launcher.Close(); }
    });

    [Fact]
    public void LobbyDropClosesOnLockdownAndStalePageActionCannotOpenPanel() => Run(shell =>
    {
        var ld = LockdownService.Current = new LockdownService();
        var launcher = new LauncherWindow();
        typeof(LauncherWindow).GetField("_panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(launcher, shell);
        launcher.CreateLobby = () => new LobbyPoll(new LobbyTimer(), () => false, () => Array.Empty<Friend>(),
            () => Task.FromResult(PbpLobbyReply.Empty), () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        try
        {
            shell.Hide(); launcher.Show();
            var chip = launcher.FindControl<Button>("LobbyChip")!;
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = chip.ContextMenu!;
            var page = (MenuItem)menu.Items[^1]!;
            Assert.True(menu.IsOpen);
            ld.Activate(TimeSpan.FromMinutes(30));
            Assert.False(menu.IsOpen);
            page.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(launcher.IsVisible); Assert.False(shell.IsVisible);
        }
        finally { ld.Deactivate(); launcher.Close(); }
    });

    [Fact]
    public void LobbyPageHandoffRechecksLockdownAfterFade() => Run(shell =>
    {
        var ld = LockdownService.Current = new LockdownService();
        var launcher = new LauncherWindow();
        typeof(LauncherWindow).GetField("_panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(launcher, shell);
        launcher.CreateLobby = () => new LobbyPoll(new LobbyTimer(), () => false, () => Array.Empty<Friend>(),
            () => Task.FromResult(PbpLobbyReply.Empty), () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        try
        {
            shell.Hide(); launcher.Show();
            var chip = launcher.FindControl<Button>("LobbyChip")!;
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            ((MenuItem)chip.ContextMenu!.Items[^1]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(launcher.IsVisible);
            ld.Activate(TimeSpan.FromMinutes(30));
            launcher.FinishExit();
            Assert.True(launcher.IsVisible); Assert.False(shell.IsVisible);
        }
        finally { ld.Deactivate(); launcher.Close(); }
    }, pumpUntilIdle: false);

    [Fact]
    public void LobbyChessJoinHonorsLeashAndReturnsAfterGameCloses() => Run(shell =>
    {
        var oldDue = MainShellWindow.LeashGateDueProvider;
        var oldPresent = MainShellWindow.PresentLeashGateProvider;
        var due = true; var presented = 0; var launches = 0;
        MainShellWindow.LeashGateDueProvider = () => due;
        MainShellWindow.PresentLeashGateProvider = _ => presented++;
        var launcher = new LauncherWindow();
        typeof(LauncherWindow).GetField("_panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(launcher, shell);
        launcher.ReadLobbyGates = () => LobbyGates.From(true, true, true);
        launcher.CreateLobby = () => new LobbyPoll(new LobbyTimer(), () => true, () => Array.Empty<Friend>(),
            () => Task.FromResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_table", "Host", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)),
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), true)));
        var game = new Window();
        launcher.OpenLobbyGame = _ => { launches++; game.Show(); return game; };
        void Join()
        {
            var chip = launcher.FindControl<Button>("LobbyChip")!;
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            JoinButton((MenuItem)chip.ContextMenu!.Items[0]!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        try
        {
            shell.Hide(); launcher.Show(); Join();
            Assert.Equal(1, presented); Assert.Equal(0, launches);
            due = false; shell.Hide(); launcher.Show(); Join();
            Assert.Equal(1, launches); Assert.False(launcher.IsVisible);
            game.Close(); Assert.True(launcher.IsVisible);
        }
        finally
        {
            MainShellWindow.LeashGateDueProvider = oldDue;
            MainShellWindow.PresentLeashGateProvider = oldPresent;
            game.Close(); launcher.Close();
        }
    });

    [Fact]
    public void LobbyRemoteJoinClaimsAndOpensTheReturnedSession() => Run(shell =>
    {
        var launcher = new LauncherWindow();
        typeof(LauncherWindow).GetField("_panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(launcher, shell);
        launcher.ReadLobbyGates = () => LobbyGates.From(true, true, true);
        launcher.CreateLobby = () => new LobbyPoll(new LobbyTimer(), () => true, () => Array.Empty<Friend>(),
            () => Task.FromResult(PbpLobbyReply.Empty), () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)new[] { new RemoteSeat("subject", "Host", 1, "light", Array.Empty<string>(), false) }, true)));
        string? claimed = null; string? opened = null;
        launcher.ClaimLobbySubject = (key, _) => { claimed = key; return Task.FromResult<string?>("https://example.invalid/#pin"); };
        launcher.OpenLobbyUrl = url => { opened = url; return true; };
        try
        {
            launcher.Show();
            var chip = launcher.FindControl<Button>("LobbyChip")!;
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var row = (MenuItem)chip.ContextMenu!.Items[0]!;
            var join = JoinButton(row);
            Assert.True(join.IsEnabled);
            join.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("subject", claimed);
            Assert.Equal("https://example.invalid/#pin", opened);
        }
        finally { launcher.Close(); }
    });

    [Fact]
    public void LobbyRemoteClaimInFlightWhenLockdownStarts_IsCancelledAndNeverOpens() => Run(shell =>
    {
        var launcher = new LauncherWindow();
        typeof(LauncherWindow).GetField("_panel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(launcher, shell);
        launcher.ReadLobbyGates = () => LobbyGates.From(true, true, true);
        launcher.CreateLobby = () => new LobbyPoll(new LobbyTimer(), () => true, () => Array.Empty<Friend>(),
            () => Task.FromResult(PbpLobbyReply.Empty), () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)new[] { new RemoteSeat("subject", "Host", 1, "light", Array.Empty<string>(), false) }, true)));
        var reply = new TaskCompletionSource<string?>();
        var token = System.Threading.CancellationToken.None; string? opened = null;
        launcher.ClaimLobbySubject = (_, ct) => { token = ct; return reply.Task; };
        launcher.OpenLobbyUrl = url => { opened = url; return true; };
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            launcher.Show();
            var chip = launcher.FindControl<Button>("LobbyChip")!;
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            JoinButton((MenuItem)chip.ContextMenu!.Items[0]!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(token.IsCancellationRequested);
            ld.Activate(TimeSpan.FromMinutes(30));
            Dispatcher.UIThread.RunJobs();
            Assert.True(token.IsCancellationRequested);
            reply.SetResult("https://example.invalid/#pin");
            Dispatcher.UIThread.RunJobs();
            Assert.Null(opened);
        }
        finally { ld.Deactivate(); launcher.Close(); }
    });

    [Fact]
    public void PanicCancelsInFlightLobbyClaimAndIgnoresLateReply() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        var launcher = LauncherWindow.Instance!;
        launcher.ReadLobbyGates = () => LobbyGates.From(true, true, true);
        var reply = new TaskCompletionSource<string?>();
        var token = System.Threading.CancellationToken.None; string? opened = null;
        launcher.ClaimLobbySubject = (_, ct) => { token = ct; return reply.Task; };
        launcher.OpenLobbyUrl = url => { opened = url; return true; };
        var row = new LobbyRow { Game = LobbyGame.Remote, State = LobbyRowState.Open, Key = "subject" };
        typeof(LauncherWindow).GetMethod("JoinLobbyRow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(launcher, new object[] { row });
        Assert.True(token.CanBeCanceled);
        PanicSurfaces.StopAll("lobby test", shell);
        Assert.True(token.IsCancellationRequested);
        reply.SetResult("https://example.invalid/#pin");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(opened);
        launcher.ClaimLobbySubject = (_, ct) => { token = ct; return Task.FromResult<string?>("https://example.invalid/#new"); };
        typeof(LauncherWindow).GetMethod("JoinLobbyRow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(launcher, new object[] { row });
        Assert.False(token.IsCancellationRequested);
        Assert.Equal("https://example.invalid/#new", opened);
    });

    [Fact]
    public void HiddenBoot_ConcealsTheWholeWindowUntilRoutingCompletes() => Run(shell =>
    {
        shell.Hide();
        LauncherWindow.Boot = new BootDecision(BootSurface.Launcher, null);
        bool openedConcealed = false;
        shell.Opened += (_, _) => openedConcealed = shell.Opacity == 0
            && !shell.ShowActivated && !shell.ShowInTaskbar && shell.BuildingHiddenForBoot;
        shell.PrepareLauncherBoot();
        shell.Show();
        Assert.True(openedConcealed);
        Assert.False(shell.IsVisible);
        Assert.False(shell.BuildingHiddenForBoot);
        Assert.Equal(1, shell.Opacity);
        Assert.True(shell.ShowInTaskbar);
        LauncherWindow.Instance!.OpenPanel();
        Assert.True(shell.IsVisible);
        Assert.Equal(1, shell.Opacity);
    }, pumpUntilIdle: false);

    [Fact]
    public void LauncherBoot_ParksPassiveOffersInInbox_AndPanelReleasesHold() => Run(shell =>
    {
        ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
        LauncherWindow.Boot = new BootDecision(BootSurface.Launcher, null);
        LauncherWindow.RouteBoot(shell);
        bool opened = false;
        ConditioningControlPanel.Avalonia.Platform.StartupLadder.PresentOrInbox(new()
        {
            Key = "launcher-hold-test", Open = () => opened = true,
        });
        Assert.False(opened);
        Assert.Single(ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox.Items);
        LauncherWindow.Instance!.OpenPanel();
        Assert.False(ConditioningControlPanel.Avalonia.Platform.StartupLadder.LauncherHolding);
        ConditioningControlPanel.Avalonia.Platform.StartupLadder.PresentOrInbox(new()
        {
            Key = "panel-offer-test", Open = () => opened = true,
        });
        Assert.True(opened);
    }, pumpUntilIdle: false);

    private sealed class HideThrowsShell : MainShellWindow
    {
        internal bool ThrowOnHide = true;
        public override void Hide()
        {
            if (ThrowOnHide) { ThrowOnHide = false; throw new InvalidOperationException("hide failed"); }
            base.Hide();
        }
    }

    [Fact]
    public void HiddenBoot_RoutingThrows_ReleasesTheStartupLadder() => Run(_ =>
    {
        LauncherWindow.Boot = new BootDecision(BootSurface.Launcher, null);
        var panel = new HideThrowsShell();
        try
        {
            panel.PrepareLauncherBoot();
            Assert.True(ConditioningControlPanel.Avalonia.Platform.StartupLadder.LauncherHolding);
            Assert.Throws<InvalidOperationException>(() => panel.Show());
            Assert.False(ConditioningControlPanel.Avalonia.Platform.StartupLadder.LauncherHolding);
            Assert.False(panel.BuildingHiddenForBoot);
            Assert.Equal(1, panel.Opacity);
        }
        finally { panel.Close(); }
    }, pumpUntilIdle: false);

    [Fact]
    public void LockdownStartingDuringHiddenBoot_KeepsPanelHiddenAndLauncherVeiled() => Run(shell =>
    {
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            // WPF MainWindow.Launcher.cs:74 hides before App.xaml.cs:4290 routes the surface.
            shell.Hide();
            LauncherWindow.Boot = new BootDecision(BootSurface.Launcher, null);
            shell.Opened += (_, _) => ld.Activate(TimeSpan.FromMinutes(30));
            shell.PrepareLauncherBoot();
            shell.Show();
            Assert.False(shell.IsVisible);
            Assert.Equal(1, shell.Opacity);
            Assert.False(shell.BuildingHiddenForBoot);
            Assert.True(LauncherWindow.Instance!.IsVisible);
            Assert.True(LauncherWindow.Instance.FindControl<Border>("LockdownVeil")!.IsVisible);
            Assert.True(ConditioningControlPanel.Avalonia.Platform.StartupLadder.LauncherHolding);
            bool offered = false;
            ConditioningControlPanel.Avalonia.Platform.StartupLadder.PresentOrInbox(new()
            {
                Key = "lockdown-boot-hold-test", Open = () => offered = true,
            });
            Assert.False(offered);
            Assert.Single(ConditioningControlPanel.Avalonia.Platform.StartupLadder.Inbox.Items);
        }
        finally { ld.Deactivate(); }
    }, pumpUntilIdle: false);

    [Fact]
    public void LauncherHide_BootIsQuiet_AndUserTuckNotifiesOnlyOnce() => Run(shell =>
    {
        int notices = 0;
        var oldSink = ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => notices++;
        shell.TrayHostPresent = () => true;
        try
        {
            shell.HideForLauncher(quiet: true);
            Assert.Equal(0, notices);
            shell.ShowFromTray();
            LauncherWindow.BackToLauncher(shell);
            Assert.Equal(1, notices);
            LauncherWindow.Instance!.OpenPanel();
            LauncherWindow.BackToLauncher(shell);
            Assert.Equal(1, notices);
        }
        finally { ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = oldSink; }
    }, pumpUntilIdle: false);

    private static string[] Tiles(LauncherWindow w) =>
        w.FindControl<UniformGrid>("GamesGrid")!.Children.Select(c => (string)c.Tag!).ToArray();

    private static string?[] Texts(Control c) =>
        c.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();

    // Finite routing/opacity checks drive their own clock; they do not pump the
    // unrelated headless renderer until idle (Skia frames can stay queued under load).
    [Fact]
    public void LockedTile_HasTierBand_SignedOutTileDoesNot() => Run(shell =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => false;
        var oldPass = CoreEntitlement.IntakePassAvailableProvider;
        try
        {
            CoreEntitlement.IntakePassAvailableProvider = () => false;
            LauncherWindow.BackToLauncher(shell);
            shell.StepLauncherFade(1);
            Dispatcher.UIThread.RunJobs();
            var w = LauncherWindow.Instance!;
            var tile = (Border)w.FindControl<UniformGrid>("GamesGrid")!.Children[^1];
            Assert.True(global::ConditioningControlPanel.Avalonia.Controls.TierFxBorder.HasBand(tile));
            CoreAccount.IsLoggedInProvider = () => false;
            w.BuildTiles();
            Dispatcher.UIThread.RunJobs();
            tile = (Border)w.FindControl<UniformGrid>("GamesGrid")!.Children[^1];
            Assert.False(global::ConditioningControlPanel.Avalonia.Controls.TierFxBorder.HasBand(tile));
        }
        finally { CoreEntitlement.IntakePassAvailableProvider = oldPass; }
    });

    [Fact]
    public void FriendsIdentity_RefreshesAfterSignIn_AndAccountSwitchOnActivation() => Run(shell =>
    {
        var oldName = CoreAccount.DisplayNameProvider;
        string name = "";
        try
        {
            CoreAccount.DisplayNameProvider = () => name;
            CoreAccount.IsLoggedInProvider = () => name.Length > 0;
            LauncherWindow.BackToLauncher(shell);
            var w = LauncherWindow.Instance!;
            var chip = w.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.FriendsRailChip>("FriendsChip")!;
            w.OpenSignIn();
            Assert.True(LoginOpen(w));
            name = "Alice";
            foreach (var dialog in w.OwnedWindows.ToArray()) dialog.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Alice", Texts(chip)); Assert.Contains("A", Texts(chip));
            name = "Bob";
            Assert.IsType<Action>(typeof(global::Avalonia.Platform.IWindowBaseImpl).GetProperty("Activated")!.GetValue(w.PlatformImpl))();
            Assert.Contains("Bob", Texts(chip)); Assert.Contains("B", Texts(chip));
        }
        finally { CoreAccount.DisplayNameProvider = oldName; }
    });

    [Fact]
    public void WhatsNew_OpensCurrentNotesOverLauncher_AndPanicClosesIt() => Run(shell =>
    {
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        var oldNotes = CoreReleaseContent.PatchNotesProvider;
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            CoreReleaseContent.AppVersionProvider = () => "7.1.5";
            CoreReleaseContent.PatchNotesProvider = () => "Launcher release notes";
            LauncherWindow.BackToLauncher(shell);
            Dispatcher.UIThread.RunJobs();
            var w = LauncherWindow.Instance!;
            Raise(w, "WhatsNewLink");
            Dispatcher.UIThread.RunJobs();
            var notes = Assert.Single(w.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.WhatsNewDialog>());
            Assert.Equal("Was ist neu in v7.1.5", notes.FindControl<TextBlock>("TxtTitle")!.Text);
            Assert.Equal("Launcher release notes", notes.FindControl<TextBlock>("TxtNotes")!.Text);
            Assert.False(shell.IsVisible);
            PanicSurfaces.All.Single(s => s.Id == "launcher-notes").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(notes.IsVisible);
        }
        finally { CoreReleaseContent.AppVersionProvider = oldVersion; CoreReleaseContent.PatchNotesProvider = oldNotes; LocalizationManager.Instance.SetLanguage(oldLanguage); }
    });

    [Fact]
    public void FriendsChip_OpensDrawer_SettingsWakePanel_AndPanicFoldsIt() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var chip = w.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.FriendsRailChip>("FriendsChip")!;
        chip.Toggle();
        Dispatcher.UIThread.RunJobs();
        Assert.True(chip.IsOpen);
        PanicSurfaces.All.Single(s => s.Id == "launcher-friends").Stop(shell);
        Assert.False(chip.IsOpen);
        chip.Toggle();
        Dispatcher.UIThread.RunJobs();
        chip.Drawer.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "friends-settings"))
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(chip.IsOpen);
        Assert.False(w.IsVisible);
        Assert.True(shell.IsVisible);
        Assert.Equal("appsettings", shell.CurrentTab);
    });

    [Fact]
    public void FriendsDrawer_SignInStaysOverLauncher_InvitesOpenPanel() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => false;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var chip = w.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.FriendsRailChip>("FriendsChip")!;
        chip.Toggle();
        Dispatcher.UIThread.RunJobs();
        chip.Drawer.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "friends-sign-in"))
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(LoginOpen(w));
        Assert.False(chip.IsOpen);
        Assert.False(shell.IsVisible);
        foreach (var dialog in w.OwnedWindows.ToArray()) dialog.Close();
        Dispatcher.UIThread.RunJobs();
        chip.Toggle();
        Dispatcher.UIThread.RunJobs();
        // The subscriber-only link exists in the drawer's add box; its handler uses the same route.
        chip.Drawer.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "friends-invite-link"))
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(chip.IsOpen);
        Assert.False(w.IsVisible);
        Assert.True(shell.IsVisible);
        // WPF 7.2.2 MainWindow.InviteTicket.cs:200 OpenInvitesCard: the drawer's invite link opens Settings > Account.
        Assert.Equal("appsettings", shell.CurrentTab);
        Assert.Equal("account", shell.AppSettingsPage?.CheckedSectionKey);
    });

    [Fact]
    public void SignIn_RepeatedPressesKeepOneDialog() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => false;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        Raise(w, "SignInPill");
        Raise(w, "SignInPill");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(w.OwnedWindows.Where(o => o is ConditioningControlPanel.Avalonia.Views.Dialogs.LoginDialog));
        foreach (var dialog in w.OwnedWindows.ToArray()) dialog.Close();
        Dispatcher.UIThread.RunJobs();
        Raise(w, "SignInPill");
        Dispatcher.UIThread.RunJobs();
        Assert.True(LoginOpen(w));
        foreach (var dialog in w.OwnedWindows.ToArray()) dialog.Close();
    });

    [Fact]
    public void BackToLauncher_WithMotion_HoldsTheVisiblePanelForItsFade() => Run(shell =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        Assert.True(LauncherWindow.BackToLauncher(shell));
        Assert.True(shell.IsVisible);
        Assert.Null(LauncherWindow.Instance);
        var root = shell.FindControl<Grid>("RootGrid")!;
        shell.StepLauncherFade(0.09);
        Assert.Equal(0.75, root.Opacity, 6);
        Assert.True(LauncherWindow.BackToLauncher(shell)); // a repeated press must not restart it
        shell.StepLauncherFade(0.09);
        Assert.False(shell.IsVisible);
        Assert.True(LauncherWindow.Instance!.IsVisible);
        Assert.Equal(1, root.Opacity);
    }, pumpUntilIdle: false);

    [Fact]
    public void OpenPanel_WithMotion_FadesThePanelUp_ThenParksOpaque() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell); // motion off for initial routing
        var launcher = LauncherWindow.Instance!;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        launcher.OpenPanel();
        launcher.StepFx(0.3);
        Assert.True(shell.IsVisible);
        Assert.False(launcher.IsVisible);
        var root = shell.FindControl<Grid>("RootGrid")!;
        Assert.Equal(0, root.Opacity);
        shell.StepLauncherFade(0.13);
        Assert.Equal(0.75, root.Opacity, 6);
        shell.StepLauncherFade(0.13);
        Assert.Equal(1, root.Opacity);
    }, pumpUntilIdle: false);

    [Fact]
    public void TrayShow_InterruptsAPendingLauncherFade_AndRestoresOpacity() => Run(shell =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        LauncherWindow.BackToLauncher(shell);
        shell.StepLauncherFade(0.09);
        shell.ShowFromTray();
        shell.StepLauncherFade(1);
        Assert.True(shell.IsVisible);
        Assert.Null(LauncherWindow.Instance);
        Assert.Equal(1, shell.FindControl<Grid>("RootGrid")!.Opacity);
    }, pumpUntilIdle: false);

    [Fact]
    public void HideDuringPanelFade_StillOpensTheLauncher() => Run(shell =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        LauncherWindow.BackToLauncher(shell);
        shell.StepLauncherFade(0.09);
        shell.Hide(); // WPF guard: the pending step still runs
        Assert.True(LauncherWindow.Instance!.IsVisible);
        Assert.Equal(1, shell.FindControl<Grid>("RootGrid")!.Opacity);
    }, pumpUntilIdle: false);

    [Fact]
    public void LockdownStartingDuringPanelFade_KeepsThePanelVisible() => Run(shell =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        LauncherWindow.BackToLauncher(shell);
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            shell.StepLauncherFade(1);
            Assert.True(shell.IsVisible);
            Assert.Null(LauncherWindow.Instance);
            Assert.Equal(1, shell.FindControl<Grid>("RootGrid")!.Opacity);
        }
        finally { ld.Deactivate(); }
    }, pumpUntilIdle: false);

    [Fact]
    public void BackToLauncher_TucksThePanel_AndIntakeOpensItOnTheTab() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;

        Assert.True(LauncherWindow.BackToLauncher(shell));
        Dispatcher.UIThread.RunJobs();
        var launcher = LauncherWindow.Instance!;
        Assert.True(launcher.IsVisible);
        Assert.False(shell.IsVisible);

        // Only the destinations this head has: the Breakout demo, Piece by Piece, Racing Thoughts and Down the Rabbit Hole game hosts,
        // then the intake tab.
        Assert.Equal(new[] { "breakoutdemo", "piecebypiece", "race", "dtrh", "intake" }, Tiles(launcher));
        var tile = launcher.FindControl<UniformGrid>("GamesGrid")!.Children[^1];
        Assert.Contains(Loc.Get("launcher_game_intake_title"), Texts(tile));
        Assert.Contains(Loc.Get("launcher_play"), Texts(tile));

        ClickPlay(tile);
        Dispatcher.UIThread.RunJobs();
        Assert.False(launcher.IsVisible);
        Assert.True(shell.IsVisible);
        Assert.Equal("gradedintake", shell.CurrentTab);
    });

    /// <summary>WPF 7.1.5 Tiles.cs:220: a press and release anywhere on the card runs Play; a press
    /// that ends off the card does not.</summary>
    [Fact]
    public void WholeCard_PressAndReleaseOnTheArt_Plays_ReleaseOffTheCardDoesNot() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var tile = w.FindControl<UniformGrid>("GamesGrid")!.Children[^1];
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var art = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 4), w)!.Value;
        var off = tile.TranslatePoint(new Point(-40, tile.Bounds.Height / 4), w)!.Value;

        w.MouseDown(art, MouseButton.Left);
        w.MouseUp(off, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.IsVisible);
        Assert.False(shell.IsVisible);

        w.MouseDown(art, MouseButton.Left);
        w.MouseUp(art, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);
        Assert.True(shell.IsVisible);
        Assert.Equal("gradedintake", shell.CurrentTab);
    });

    /// <summary>WPF 7.1.5 GrowToFitTiles: overflowing tiles grow the window once, within the work area.</summary>
    [Fact]
    public void OverflowingTiles_GrowTheWindowOnce_WithinTheWorkArea() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var grid = w.FindControl<UniformGrid>("GamesGrid")!;
        // One tile fits: the block sits centred (WPF FitTiles).
        Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, grid.VerticalAlignment);
        for (int i = 0; i < 8; i++) grid.Children.Add(new Border());
        double old = w.Bounds.Height;
        w.SeatGrid();
        double grown = w.Height;
        Assert.True(grown > old + 1, $"{old} -> {grown}");
        var wa = w.Screens.ScreenFromWindow(w)!;
        Assert.True(grown <= wa.WorkingArea.Height / wa.Scaling + 0.5);
        Assert.InRange(w.Position.Y, wa.WorkingArea.Y, wa.WorkingArea.Bottom - (int)Math.Ceiling(grown * wa.Scaling));
        Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Top, grid.VerticalAlignment);

        w.Height = old;
        Dispatcher.UIThread.RunJobs();
        w.SeatGrid();
        Assert.Equal(old, w.Height);
    });

    private static void ClickPlay(Control tile) =>
        tile.GetVisualDescendants().OfType<Button>().Single(b => !Equals(b.Tag, "tile-shortcut"))
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void Lockdown_TilePlayRefuses_LauncherStaysAndPanelStaysTucked() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var launcher = LauncherWindow.Instance!;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            ClickPlay(launcher.FindControl<UniformGrid>("GamesGrid")!.Children[^1]);
            Dispatcher.UIThread.RunJobs();
            Assert.True(launcher.IsVisible);
            Assert.False(shell.IsVisible);
        }
        finally { ld.Deactivate(); }
    });

    private static bool LoginOpen(LauncherWindow w) =>
        w.OwnedWindows.Any(o => o is ConditioningControlPanel.Avalonia.Views.Dialogs.LoginDialog { IsVisible: true });

    private static void Raise(LauncherWindow w, string name) =>
        w.FindControl<Button>(name)!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void Lockdown_SignedOutCardPress_AndSignInPillOpenNoLogin() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => false;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var tile = w.FindControl<UniformGrid>("GamesGrid")!.Children[^1];
        // A real press/release on the art plate (not the Play button), as a user taps the card.
        void Press()
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var p = tile.TranslatePoint(new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 4), w)!.Value;
            w.MouseDown(p, MouseButton.Left);
            w.MouseUp(p, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            Press();
            Assert.False(LoginOpen(w));
            Raise(w, "SignInPill");
            Dispatcher.UIThread.RunJobs();
            Assert.False(LoginOpen(w));
            // Control: the same press reaches the card once Lockdown ends.
            ld.Deactivate();
            Press();
            Assert.True(LoginOpen(w));
        }
        finally
        {
            ld.Deactivate();
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Theory]
    [InlineData("PanelCta")]
    [InlineData("AccountChipButton")]
    public void Lockdown_PanelCtaAndAccountChip_LeaveThePanelTucked(string button) => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            Raise(w, button);
            Dispatcher.UIThread.RunJobs();
            Assert.True(w.IsVisible);
            Assert.False(shell.IsVisible);
        }
        finally { ld.Deactivate(); }
    });

    [Fact]
    public void SignedOut_TheCardAsksForSignIn_AndPlayLeavesThePanelTucked() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => false;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var tile = LauncherWindow.Instance!.FindControl<UniformGrid>("GamesGrid")!.Children[^1];
        Assert.Contains(Loc.Get("launcher_sign_in"), Texts(tile));
        Assert.Contains(Loc.Get("launcher_pill_sign_in"), Texts(tile));
        Assert.False(shell.IsVisible);
    });

    [Fact]
    public void Lockdown_RefusesTheDoor() => Run(shell =>
    {
        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        Assert.False(LauncherWindow.BackToLauncher(shell));
        Assert.True(shell.IsVisible);
        Assert.Null(LauncherWindow.Instance);
        ld.Deactivate();
    });

    [Fact]
    public void Close_HidesWhileThePanelIsUp_ExitsWhenNothingIsLeft() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var launcher = LauncherWindow.Instance!;

        shell.Show();                               // the panel came back from the tray
        launcher.RequestClose();
        Assert.False(launcher.IsVisible);           // hidden, not exiting
        Assert.True(shell.IsVisible);

        var closed = false;
        shell.Closed += (_, _) => closed = true;
        LauncherWindow.Open();
        shell.Hide();
        launcher.RequestClose();                    // nothing running, panel tucked: leave
        Dispatcher.UIThread.RunJobs();
        Assert.True(closed);
    });
    [Fact]
    public void ClosingTheShell_ClosesTheHiddenLauncher() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        var launcher = LauncherWindow.Instance!;
        launcher.Hide();
        shell.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(LauncherWindow.Instance);       // gone, not a hidden window keeping the process up
    });

    [Fact]
    public void Close_HidesWhileTheEngineOrASessionRuns_MinimizesWithNoTray() => Run(shell =>
    {
        var oldSession = CoreSession.IsSessionRunningProvider;
        try
        {
            CoreSettings.Current.FlashEnabled = true;
            shell.StartEngine();
            LauncherWindow.BackToLauncher(shell);
            var launcher = LauncherWindow.Instance!;
            shell.TrayHostPresent = () => true;
            launcher.RequestClose();
            Assert.False(launcher.IsVisible);           // engine running: hide, not exit
            Assert.True(CoreEngine.IsRunning);

            CoreEngine.Stop();
            CoreSession.IsSessionRunningProvider = () => true;
            LauncherWindow.Open();
            launcher.RequestClose();
            Assert.False(launcher.IsVisible);           // session running: hide, not exit
            Assert.Same(launcher, LauncherWindow.Instance);   // hidden, not closed

            LauncherWindow.Open();
            shell.TrayHostPresent = () => false;
            launcher.RequestClose();
            Assert.True(launcher.IsVisible);            // no tray: the way back stays on the taskbar
            Assert.Equal(WindowState.Minimized, launcher.WindowState);
        }
        finally
        {
            CoreSession.IsSessionRunningProvider = oldSession;
            CoreEngine.Stop();
        }
    });

    // ---- slice 2: boot surface, second-instance handoff, tray row, skip box

    [Fact]
    public void BootIntoTheLauncher_TucksThePanel() => Run(shell =>
    {
        LauncherWindow.Boot = BootDecision.LauncherFirst;
        LauncherWindow.RouteBoot(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);
        Assert.True(LauncherWindow.Instance!.IsVisible);
        Assert.True(global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.LauncherHolding);
        // restack b07 (#4413): a panel shown by a door other than the launcher's own OpenPanel still lets go.
        LauncherWindow.Instance!.Hide();
        shell.ShowFromTray();
        Assert.False(global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.LauncherHolding);
    });

    [Fact]
    public void BootIntoAGame_OpensItsDestination_UnknownHereShowsTheTiles() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;
        LauncherWindow.Boot = BootDecision.GameFirst("intake");
        LauncherWindow.RouteBoot(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        Assert.Equal("gradedintake", shell.CurrentTab);
        Assert.False(LauncherWindow.Instance!.IsVisible);

        LauncherWindow.Boot = BootDecision.GameFirst("arcademy");   // a WPF game with no host on this head
        LauncherWindow.RouteBoot(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);
        Assert.True(LauncherWindow.Instance!.IsVisible);
    });

    [Fact]
    public void SecondInstance_RoutesTheSurfaceItNames() => Run(shell =>
    {
        // SkipToPanelBox saves true to this run's settings.json, which Run's fresh SettingsService reloads.
        CoreSettings.Current.LauncherSkipToPanel = false;
        LauncherWindow.RouteHandoff(shell, LauncherHandoff.Encode(new[] { "--launcher" }));
        Dispatcher.UIThread.RunJobs();
        var launcher = LauncherWindow.Instance!;
        Assert.True(launcher.IsVisible);
        Assert.False(shell.IsVisible);

        LauncherWindow.RouteHandoff(shell, LauncherHandoff.Encode(new[] { "--panel" }));
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        Assert.False(launcher.IsVisible);

        LauncherWindow.RouteHandoff(shell, "game:nope");
        Dispatcher.UIThread.RunJobs();
        Assert.True(launcher.IsVisible);

        // Bare relaunch with the panel tucked: the launcher, unless "open the panel directly".
        shell.Hide();
        LauncherWindow.RouteHandoff(shell, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);
        Assert.True(launcher.IsVisible);
        CoreSettings.Current.LauncherSkipToPanel = true;
        LauncherWindow.RouteHandoff(shell, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        Assert.False(launcher.IsVisible);
    });

    [Fact]
    public void SecondInstance_UnderLockdown_NeverTucksThePanel() => Run(shell =>
    {
        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        LauncherWindow.RouteHandoff(shell, "launcher");
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.IsVisible);
        ld.Deactivate();
    });

    private static NativeMenuItem BackRow(MainShellWindow shell) => shell.BuildTrayMenu().Items.OfType<NativeMenuItem>()
        .Single(i => i.Header == Loc.Get("launcher_back_to_client"));

    [Fact]
    public void TrayBackRow_OnlyWhileTheLauncherIsInPlay_GreyedUnderLockdown() => Run(shell =>
    {
        Assert.False(BackRow(shell).IsVisible);             // a panel boot, launcher never built
        LauncherWindow.Boot = BootDecision.LauncherFirst;
        var row = BackRow(shell);
        Assert.True(row.IsVisible);
        Assert.True(row.IsEnabled);

        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        Assert.False(BackRow(shell).IsEnabled);
        ld.Deactivate();

        row.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.IsVisible);
        Assert.True(LauncherWindow.Instance!.IsVisible);
    });

    [Fact]
    public void SkipToPanelBox_ShowsAndSavesTheSetting() => Run(shell =>
    {
        CoreSettings.Current.LauncherSkipToPanel = false;
        var box = LauncherWindow.Open().FindControl<CheckBox>("SkipToPanel")!;
        Assert.False(box.IsChecked);
        Assert.Contains(Loc.Get("launcher_skip_to_panel"), Texts(box));
        box.IsChecked = true;
        box.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.True(CoreSettings.Current.LauncherSkipToPanel);
        // The click saved true to the run's settings.json; put it back so no later test inherits it.
        CoreSettings.Current.LauncherSkipToPanel = false;
        (CoreSettings.ServiceProvider?.Invoke() as SettingsService)?.SaveImmediate();
    });

    [Fact]
    public void BootHiddenPanel_KeepsTheCompanionTubeDown() => Run(_ =>
    {
        var old = CoreSettings.Current.AvatarEnabled;
        CoreSettings.Current.AvatarEnabled = true;
        var panel = new MainShellWindow { BuildingHiddenForBoot = true };   // as App's launcher boot
        try
        {
            panel.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(panel.Tube);                 // built, as WPF does on load
            Assert.False(panel.Tube!.IsVisible);        // but never on screen before the launcher
        }
        finally
        {
            panel.Close();
            CoreSettings.Current.AvatarEnabled = old;
        }
    });

    // ---- slice 3: account chip, mod pill, panel card status/stats (WPF LauncherWindow.xaml.cs:220-606)

    private static string? Text(LauncherWindow w, string name) => w.FindControl<TextBlock>(name)!.Text;

    [Fact]
    public void AccountChip_SignedInShowsNameInitialAndSp_SignedOutShowsThePill() => Run(shell =>
    {
        var s = CoreSettings.Current;
        s.UserDisplayName = "  bambi ";
        s.SkillPoints = 1234;
        CoreAccount.IsLoggedInProvider = () => true;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        Assert.True(w.FindControl<Button>("AccountChipButton")!.IsVisible);
        Assert.False(w.FindControl<Button>("SignInPill")!.IsVisible);
        Assert.Equal("bambi", Text(w, "AccountName"));
        Assert.Equal("B", Text(w, "AvatarInitial"));
        Assert.True(w.FindControl<Button>("SpChip")!.IsVisible);
        Assert.Equal(1234.ToString("N0"), Text(w, "SpReadout"));
        Assert.False(w.FindControl<Image>("TierBadge")!.IsVisible);   // no Patreon tier here

        CoreAccount.IsLoggedInProvider = () => false;
        w.RefreshAccount();
        Assert.False(w.FindControl<Button>("AccountChipButton")!.IsVisible);
        Assert.True(w.FindControl<Button>("SignInPill")!.IsVisible);
        Assert.False(w.FindControl<Button>("SpChip")!.IsVisible);
    });

    [Fact]
    public void WalletChip_TracksBalanceAndSignIn() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => false;
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var chip = w.FindControl<Button>("SpChip")!;
        Assert.False(chip.IsVisible);
        CoreSettings.Current.SkillPoints = 1234;
        CoreAccount.IsLoggedInProvider = () => true;
        w.RefreshAccount();
        Assert.True(chip.IsVisible);
        Assert.Equal(1234.ToString("N0"), Text(w, "SpReadout"));
        CoreSettings.Current.SkillPoints = 5678;
        w.RefreshAccount();
        Assert.Equal(5678.ToString("N0"), Text(w, "SpReadout"));
        CoreAccount.IsLoggedInProvider = () => false;
        w.RefreshAccount();
        Assert.False(chip.IsVisible);
    });

    [Fact]
    public void WalletReadout_AnimatesOnShowAndChange_AndParks() => Run(shell =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        CoreSettings.Current.SkillPoints = 1000;
        LauncherWindow.BackToLauncher(shell);
        var w = LauncherWindow.Instance!;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        w.Hide();
        w.Show();
        Assert.Equal(0.ToString("N0"), Text(w, "SpReadout"));
        w.StepFx(0.45);
        Assert.Equal(750.ToString("N0"), Text(w, "SpReadout"));
        w.StepFx(0.45);
        Assert.Equal(1000.ToString("N0"), Text(w, "SpReadout"));
        CoreSettings.Current.SkillPoints = 2000;
        w.RefreshAccount();
        w.StepFx(0.3);
        Assert.Equal(1750.ToString("N0"), Text(w, "SpReadout"));
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        w.StepFx(0.01);
        Assert.Equal(2000.ToString("N0"), Text(w, "SpReadout"));
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        CoreSettings.Current.SkillPoints = 3000;
        w.RefreshAccount();
        w.StepFx(0.3);
        Assert.Equal(2750.ToString("N0"), Text(w, "SpReadout"));
        w.Hide();
        Assert.Equal(3000.ToString("N0"), Text(w, "SpReadout"));
        Assert.False(w.FxTicking);
    }, pumpUntilIdle: false);

    [Fact]
    public void PanelCard_ShowsStatsAndRunningStatus_StopLinkStopsTheEngine() => Run(shell =>
    {
        var s = CoreSettings.Current;
        (s.PlayerLevel, s.PlayerXP, s.SkillPoints, s.TotalConditioningMinutes) = (3, 50, 7, 125);
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        Assert.Equal("3", Text(w, "StatLevel"));
        Assert.Equal("7", Text(w, "StatSparkles"));
        Assert.Equal("2h 05m", Text(w, "StatTime"));
        var need = XpCurve.GetXPForLevel(3, XpCurve.EpochOf(s));
        Assert.Equal(Loc.GetF("launcher_stat_xp", "50", ((int)need).ToString("N0")), Text(w, "XpCaption"));
        Assert.Equal(Loc.Get("launcher_panel_idle"), Text(w, "StatusText"));
        Assert.Equal(Loc.Get("launcher_panel_launch"), Text(w, "PanelCtaText"));
        var stop = w.FindControl<Button>("StopLink")!;
        Assert.False(stop.IsVisible);

        try
        {
            s.FlashEnabled = true;
            shell.StartEngine();
            w.RefreshStatus();
            Assert.True(CoreEngine.IsRunning);
            Assert.Equal(Loc.GetF("launcher_panel_running", CoreEngine.StartedUtc!.Value.ToLocalTime().ToString("HH:mm")),
                Text(w, "StatusText"));
            Assert.Equal(Loc.Get("launcher_panel_open"), Text(w, "PanelCtaText"));
            Assert.True(stop.IsVisible);

            stop.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(CoreEngine.IsRunning);
            Assert.Null(CoreEngine.StartedUtc);
            Assert.False(stop.IsVisible);
            Assert.Equal(Loc.Get("launcher_panel_idle"), Text(w, "StatusText"));
        }
        finally { CoreEngine.Stop(); }
    });

    [Fact]
    public void EngineStoppedOutsideLauncher_ImmediatelyRefreshesThePanelCard() => Run(shell =>
    {
        var oldHook = CoreEngine.StoppedHook;
        CoreEngine.StoppedHook = shell.OnEngineStopped;
        try
        {
            shell.StartEngine();
            Assert.True(CoreEngine.IsRunning);
            LauncherWindow.BackToLauncher(shell);
            var w = LauncherWindow.Instance!;
            Assert.True(w.FindControl<Button>("StopLink")!.IsVisible);
            Assert.Equal(Loc.Get("launcher_panel_open"), Text(w, "PanelCtaText"));

            CoreEngine.Stop(); // session or panic path, not the launcher's Stop button
            Assert.False(w.FindControl<Button>("StopLink")!.IsVisible);
            Assert.Equal(Loc.Get("launcher_panel_idle"), Text(w, "StatusText"));
            Assert.Equal(Loc.Get("launcher_panel_launch"), Text(w, "PanelCtaText"));
        }
        finally { CoreEngine.StoppedHook = oldHook; CoreEngine.Stop(); }
    });

    [Fact]
    public void BreakoutDemoTile_UsesTheBuiltInCover() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        var w = LauncherWindow.Instance!;
        var tile = w.FindControl<UniformGrid>("GamesGrid")!.Children.Single(c => (string?)c.Tag == "breakoutdemo");
        // The art sits in the tile's sliding art host, under the tilt and sheen layers.
        var cover = Assert.Single(global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants((Border)tile).OfType<Image>());
        Assert.Same(ConditioningControlPanel.Avalonia.Helpers.BreakoutCardArt.Demo, cover.Source);
        Assert.Equal(global::Avalonia.Media.Stretch.UniformToFill, cover.Stretch);
    });

    [Fact]
    public void ModPill_ReadsTheActiveMod_ASwitchRepaintsThePillAndRedrawsTheTiles() => Run(shell =>
    {
        var snapshot = new CoreModsSnapshot();
        // Values too, not only keys: an earlier repaint may already own PinkColor at the top level, and the
        // switch below would leave the Dronification green there for every later test.
        var oldResources = Application.Current!.Resources.ToDictionary(x => x.Key, x => x.Value);
        try
        {
            CoreSettings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
            global::ConditioningControlPanel.Avalonia.App.StartMods();
            var mods = global::ConditioningControlPanel.Avalonia.App.Mods!;
            LauncherWindow.BackToLauncher(shell);
            Dispatcher.UIThread.RunJobs();
            var w = LauncherWindow.Instance!;
            string Label() => LauncherModMenu.Label(Loc.Get("launcher_mod_label"), mods.ActiveMod.Name, "-");
            Assert.Equal(Label(), Text(w, "ModPillText"));
            var tileBefore = w.FindControl<UniformGrid>("GamesGrid")!.Children[^1];

            w.SwitchMod(BuiltInMods.DronificationId);
            Dispatcher.UIThread.RunJobs();   // the tile redraw is posted, as WPF's
            Assert.Equal(BuiltInMods.DronificationId, mods.ActiveModId);
            Assert.Equal(Label(), Text(w, "ModPillText"));
            Assert.NotSame(tileBefore, w.FindControl<UniformGrid>("GamesGrid")!.Children[^1]);
        }
        finally
        {
            foreach (var key in Application.Current.Resources.Keys.Where(k => !oldResources.ContainsKey(k)).ToList())
                Application.Current.Resources.Remove(key);
            foreach (var entry in oldResources) Application.Current.Resources[entry.Key] = entry.Value;
            snapshot.Dispose();
            global::ConditioningControlPanel.Avalonia.App.ResetReleaseContent();
            CoreSettings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
        }
    });
    [Fact]
    public void Lockdown_StopLinkRefuses_EngineKeepsRunning() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            CoreSettings.Current.FlashEnabled = true;
            shell.StartEngine();
            ld.Activate(TimeSpan.FromMinutes(30));
            w.FindControl<Button>("StopLink")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(CoreEngine.IsRunning);
        }
        finally { ld.Deactivate(); CoreEngine.Stop(); }
    });

    [Fact]
    public void Lockdown_ModPillOpensNoMenu_AndSwitchModRefuses() => Run(shell =>
    {
        var snapshot = new CoreModsSnapshot();
        // Values too, not only keys: an earlier repaint may already own PinkColor at the top level, and the
        // switch below would leave the Dronification green there for every later test.
        var oldResources = Application.Current!.Resources.ToDictionary(x => x.Key, x => x.Value);
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            CoreSettings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
            global::ConditioningControlPanel.Avalonia.App.StartMods();
            LauncherWindow.BackToLauncher(shell);
            Dispatcher.UIThread.RunJobs();
            var w = LauncherWindow.Instance!;
            ld.Activate(TimeSpan.FromMinutes(30));
            var pill = w.FindControl<Button>("ModPill")!;
            pill.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Null(pill.ContextMenu);
            w.SwitchMod(BuiltInMods.DronificationId);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BuiltInMods.CCPDefaultId, global::ConditioningControlPanel.Avalonia.App.Mods!.ActiveModId);
        }
        finally
        {
            ld.Deactivate();
            foreach (var key in Application.Current.Resources.Keys.Where(k => !oldResources.ContainsKey(k)).ToList())
                Application.Current.Resources.Remove(key);
            foreach (var entry in oldResources) Application.Current.Resources[entry.Key] = entry.Value;
            snapshot.Dispose();
            global::ConditioningControlPanel.Avalonia.App.ResetReleaseContent();
            CoreSettings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
        }
    });

    [Fact]
    public void StatusTimer_StopsWhenTheLauncherHides() => Run(shell =>
    {
        LauncherWindow.BackToLauncher(shell);
        Dispatcher.UIThread.RunJobs();
        var w = LauncherWindow.Instance!;
        var timer = (DispatcherTimer)typeof(LauncherWindow)
            .GetField("_statusTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(w)!;
        Assert.True(timer.IsEnabled);
        w.Hide();
        Assert.False(timer.IsEnabled);
    });
}
