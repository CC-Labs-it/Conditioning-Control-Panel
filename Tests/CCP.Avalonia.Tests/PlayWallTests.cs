using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services.Race;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Play wall from the shell (WPF PlayTabView.xaml + MainWindow.PlayTab.cs): the GAMES
/// zone, the Graded Intake card's four pass states, and the two doors that land on a section.</summary>
public sealed class PlayWallTests
{
    [Theory]
    [InlineData("BtnPlayDtrh", "dtrh")]
    [InlineData("BtnPlayRacingThoughts", "glassroad")]
    [InlineData("BtnPlayRacingThoughtsClassic", "raceclassic")]
    public void AvailableGameCardUsesTheExistingLauncherDoor(string buttonName, string gameId)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var previous = PlayTabView.LaunchGame;
            var (oldLogin, oldLab, oldPremium) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.HasPremiumProvider);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasLabProvider = () => true;
            CoreEntitlement.HasPremiumProvider = () => true;   // DtRH is a Basic door (port/7.2 DtrhGate)
            var shell = new MainShellWindow();
            var launches = new System.Collections.Generic.List<string>();
            PlayTabView.LaunchGame = id => launches.Add(id);
            try
            {
                shell.Show();
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<Control>("PlayTab")!;
                var button = play.FindControl<Button>(buttonName)!;
                Assert.True(button.IsEnabled);
                Assert.Null(ToolTip.GetTip(button));
                // WPF PlayTabView.Cards.cs:59-66 forwards the click; the host runs the access gate.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(gameId, Assert.Single(launches));
            }
            finally { shell.Close(); PlayTabView.LaunchGame = previous; CoreAccount.IsLoggedInProvider = oldLogin; CoreEntitlement.HasLabProvider = oldLab; CoreEntitlement.HasPremiumProvider = oldPremium; }
        });
    }

    [Theory]
    [InlineData("BtnPlayDtrh")]
    [InlineData("BtnPlayRacingThoughts")]
    public void SignedOutGameCardOpensLoginWithoutLaunching(string buttonName)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var (oldLogin, oldLaunch) = (CoreAccount.IsLoggedInProvider, PlayTabView.LaunchGame);
            CoreAccount.IsLoggedInProvider = () => false;
            var launches = new System.Collections.Generic.List<string>();
            PlayTabView.LaunchGame = id => launches.Add(id);
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("play");
                shell.Named<Control>("PlayTab")!.FindControl<Button>(buttonName)!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Single(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.LoginDialog>());
                Assert.Empty(launches);
            }
            finally
            {
                foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
                shell.Close();
                (CoreAccount.IsLoggedInProvider, PlayTabView.LaunchGame) = (oldLogin, oldLaunch);
            }
        });
    }

    [Fact]
    public void RacingThoughtsBandAndBadgeFollowLabEntitlement()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var previous = CoreEntitlement.HasLabProvider;
            bool lab = false;
            CoreEntitlement.HasLabProvider = () => lab;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<Control>("PlayTab")!;
                var band = play.FindControl<Border>("PlayLockRacingThoughts");
                var badge = play.FindControl<ConditioningControlPanel.Avalonia.Controls.TierBadge>("PlayBadgeRacingThoughts");
                Assert.NotNull(band);
                Assert.NotNull(badge);
                // WPF PlayTabView.xaml:574-580 and MainWindow.PlayTab.cs:132.
                Assert.Equal(2, badge.Tier);
                Assert.True(band.IsVisible);
                lab = true;
                shell.UpdateQuickLoginUI();
                Assert.False(band.IsVisible);
                // The tier badge stays outside the band that entitlement hides (WPF PlayTabView.xaml:574-580).
                Assert.Same(band.Parent, badge.Parent);
                lab = false;
                shell.UpdateQuickLoginUI();
                Assert.True(band.IsVisible);
                Assert.True(play.FindControl<Button>("BtnPlayRacingThoughts")!.IsEnabled);
            }
            finally { shell.Close(); CoreEntitlement.HasLabProvider = previous; }
        });
    }

    [Fact]
    public void WebAppCardUsesBrowserFallbackThenRetiresBanner()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var seen = CoreSettings.Current.SeenFeatureIntros;
            bool hadKey = seen.Remove(MainShellWindow.WebBannerSeenKey);
            var previousOpen = MainShellWindow.OpenWebAppLink;
            var opened = new System.Collections.Generic.List<string>();
            bool browserTakesIt = false;
            MainShellWindow.OpenWebAppLink = (_, url) => { opened.Add(url); return Task.FromResult(browserTakesIt); };
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<Control>("PlayTab")!;
                var button = play.FindControl<Button>("BtnPlayWebApp")!;
                Assert.True(button.IsEnabled);
                // No browser takes the link: WPF OpenUrlOrPrompt copies it and says so.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "https://app.cclabs.app" }, opened);
                var prompt = Assert.Single(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>());
                Assert.Contains("https://app.cclabs.app", prompt.FindControl<TextBlock>("TxtMessage")!.Text);
                Assert.DoesNotContain(MainShellWindow.WebBannerSeenKey, seen);
                prompt.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(MainShellWindow.WebBannerSeenKey, seen);
                Assert.Equal("play", shell.CurrentTab);

                // The browser opens: no prompt, and the beat retires.
                seen.Remove(MainShellWindow.WebBannerSeenKey);
                browserTakesIt = true;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, opened.Count);
                Assert.Empty(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>());
                Assert.Contains(MainShellWindow.WebBannerSeenKey, seen);
                Assert.Equal("play", shell.CurrentTab);
            }
            finally
            {
                foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
                shell.Close();
                MainShellWindow.OpenWebAppLink = previousOpen;
                if (!hadKey) seen.Remove(MainShellWindow.WebBannerSeenKey);
                else if (!seen.Contains(MainShellWindow.WebBannerSeenKey)) seen.Add(MainShellWindow.WebBannerSeenKey);
            }
        });
    }

    [Fact]
    public void RestoredGameCardsMatchWpfOrderArtAndTierRims()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var view = new PlayTabView();
            T C<T>(string name) where T : Control => view.FindControl<T>(name)!;
            foreach (var name in new[] { "BackRoom", "Arcademy" })
            {
                var button = C<Button>("BtnPlay" + name);
                Assert.False(button.IsEnabled);
                Assert.Equal(Loc.Get("exclusives_not_on_this_build"), ToolTip.GetTip(button));
            }
            var games = (Panel)C<Grid>("SlotBackRoom").Parent!;
            Assert.Equal(3, ((global::Avalonia.Controls.Primitives.UniformGrid)games).Columns);
            Assert.Equal(new[] { "SlotBackRoom", "SlotDtrh", "SlotArcademy", "SlotRacingThoughts", "SlotRacingThoughtsClassic", "SlotWebApp" },
                games.Children.Skip(4).Select(c => c.Name));
            foreach (var name in new[] { "BackRoom", "Dtrh", "Arcademy", "RacingThoughts", "WebApp" })
                Assert.NotNull(C<Border>("Play" + name + "HeroPlate").Background);
            // WPF PlayCardT2 (PlayTabView.xaml:131-132): tier-2 living-metal rim, 3 px; DtRH is the Basic (tier-1) card on port/7.2.
            foreach (var (slot, tier) in new[] { ("SlotBreakout", 2), ("SlotDtrh", 1), ("SlotArcademy", 2) })
            {
                var card = (Border)C<Grid>(slot).Children[0];
                Assert.Equal(tier, ConditioningControlPanel.Avalonia.Controls.TierFxBorder.GetTier(card));
                Assert.Equal(3, ConditioningControlPanel.Avalonia.Controls.TierFxBorder.GetRimThickness(card));
            }

        });
    }


    [Fact]
    public void IntakeCardFollowsThePass_GamesAreHonest_DoorsLandOnTheirSection()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var prevSettings = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var (week, utc) = (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc);
            var (loggedIn, lab) = (CoreAccount.IsLoggedInProvider, CoreAccount.HasPremiumAccessProvider);
            bool isLoggedIn = false, isPaid = false;
            CoreAccount.IsLoggedInProvider = () => isLoggedIn;
            CoreAccount.HasPremiumAccessProvider = () => isPaid;
            (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc) = ("", null);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<Control>("PlayTab")!;
                T C<T>(string n) where T : Control => play.FindControl<T>(n)!;
                var band = C<Border>("PlayLockIntake");
                var line = C<TextBlock>("TxtPlayIntakeState");
                var where = C<Button>("BtnPlayIntakePassHome");

                // Signed out: band + the sign-in copy, no "where" button.
                Assert.True(band.IsVisible);
                Assert.True(line.IsVisible);
                Assert.Equal(Loc.Get("intake_gate_login_body"), line.Text);
                Assert.False(where.IsVisible);

                // Signed in, week unspent: no band, the announcement and the "where" button.
                isLoggedIn = true;
                AvApp.IntakePass.RaiseChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.False(band.IsVisible);
                Assert.Equal(Loc.Get("pl6_intake_state_available"), line.Text);
                Assert.True(where.IsVisible);

                // A completed run spends the week; the card repaints from the event alone.
                AvApp.IntakePass.ConsumeForCompletedIntake();
                Dispatcher.UIThread.RunJobs();
                Assert.True(band.IsVisible);
                var days = IntakePassService.DaysUntilNextPass;
                Assert.Equal(days == 1 ? Loc.Get("intake_gate_spent_body_one_day") : Loc.GetF("intake_gate_spent_body", days), line.Text);
                Assert.False(where.IsVisible);

                // Any paid tier (WPF 2a1fd1e88, was Prime only): no band, no line.
                isPaid = true;
                AvApp.IntakePass.RaiseChanged();
                Dispatcher.UIThread.RunJobs();
                Assert.False(band.IsVisible);
                Assert.False(line.IsVisible);

                // GAMES: Goon has no host on this head, so its Play button
                // refuses honestly; the Breakout demo and Piece by Piece open their game as the launcher does.
                foreach (var n in new[] { "BtnPlayGoon" })
                {
                    var b = C<Button>(n);
                    Assert.False(b.IsEnabled, n);
                    Assert.Equal(Loc.Get("exclusives_not_on_this_build"), ToolTip.GetTip(b));
                }
                var launched = new System.Collections.Generic.List<string>();
                var previousLaunch = PlayTabView.LaunchGame;
                PlayTabView.LaunchGame = launched.Add;
                try
                {
                    foreach (var n in new[] { "BtnPlayBreakoutDemo", "BtnPlayChess" })
                    {
                        var b = C<Button>(n);
                        Assert.True(b.IsEnabled, n);
                        Assert.Null(ToolTip.GetTip(b));
                        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                }
                finally { PlayTabView.LaunchGame = previousLaunch; }
                Assert.Equal(new[] { "breakoutdemo", "piecebypiece" }, launched);
                // Both ids are launcher destinations with a GameWindow host on this head.
                Assert.All(launched, id => Assert.True(LauncherWindow.CanStartGame(id) && GameWindow.Games.ContainsKey(id), id));
                Assert.NotNull(C<Image>("PlayBreakoutArt").Source);

                // "Configure in settings" lands on the Devices section (WPF OpenDeviceSettings).
                C<Button>("BtnOpenDeviceSettings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("appsettings", shell.CurrentTab);
                Assert.True(shell.AppSettingsPage!.FindControl<RadioButton>("SectionPillDevices")!.IsChecked);

                // Loom opens the Studio on the Spiral module (WPF OpenStudioModule("spiral")).
                shell.ShowTab("play");
                C<Button>("BtnPlayLoom").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("studio", shell.CurrentTab);
                Assert.Equal("spiral", shell.StudioRack!.SelectedRackKey);
            }
            finally
            {
                shell.Close();
                CoreAccount.IsLoggedInProvider = loggedIn;
                CoreAccount.HasPremiumAccessProvider = lab;
                (service.Current.IntakePassSpentWeek, service.Current.IntakePassSpentUtc) = (week, utc);
                service.SaveImmediate();
                CoreSettings.ServiceProvider = prevSettings;
            }
        });
    }

    [Theory]
    [InlineData("BtnPlayBreakoutDemo", "breakoutdemo", "/backroom/stations/breakout/play.html")]
    [InlineData("BtnPlayChess", "piecebypiece", "/piecebypiece/index.html")]
    public void FreeGameButton_OpensExistingHost_Refocuses_AndPanicCloses(string buttonName, string gameId, string page)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var previousSettings = CoreSettings.ServiceProvider;
            var previousAccount = CoreAccount.IsLoggedInProvider;
            var previousServer = GameWindow.Server;
            CoreSettings.ServiceProvider = () => service;
            CoreAccount.IsLoggedInProvider = () => false;
            var root = Directory.CreateTempSubdirectory("play-free-games-");
            using var server = new WebAssetServer(root.FullName);
            GameWindow.Server = () => server;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var button = shell.Named<Control>("PlayTab")!.FindControl<Button>(buttonName)!;
                Assert.True(button.IsEnabled);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var game = Assert.IsType<GameWindow>(GameWindow.OpenForTests(gameId));
                Assert.Equal(page, game.PageUrl!.AbsolutePath);
                if (gameId == "breakoutdemo")
                    Assert.True(game.PageUrl.Query.Contains("demo=1", StringComparison.Ordinal));

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Same(game, Assert.Single(GameWindow.OpenWindowsForTests()));
                Assert.True(game.IsVisible);
                var panic = PanicSurfaces.All.Single(s => s.Id == "games");
                Assert.True(panic.OwnsTheScreen!());
                panic.Stop(shell);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(GameWindow.OpenWindowsForTests());
                Assert.False(panic.OwnsTheScreen());
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                shell.Close();
                GameWindow.Server = previousServer;
                CoreAccount.IsLoggedInProvider = previousAccount;
                service.SealForReset();
                CoreSettings.ServiceProvider = previousSettings;
                root.Delete(true);
            }
        });
    }

    [Fact]
    public void NineGameWall_DoorsRunTheLauncherEntryAndTheWebApp()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var prevSettings = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var (loggedIn, lab, owns, prevServer, prevShell) = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider,
                RacingAccess.IsGrantedProvider, GameWindow.Server, ExternalOpener.Shell);
            var prevFree = CoreEntitlement.IsFreeTodayProvider;
            // The doors route to the Glassroad page; the Linux first-race card (B2) has GameWindowTests.FirstRace.
            var prevLinuxCard = GameWindow.UseLinuxRaceCard;
            GameWindow.UseLinuxRaceCard = () => false;
            bool isLoggedIn = false, isLab = false;
            string? freeKey = null;
            CoreAccount.IsLoggedInProvider = () => isLoggedIn;
            CoreEntitlement.HasLabProvider = () => isLab;
            // DtRH is Basic in WPF 7.2.2 (keyed premium gate): Lab implies Basic here, so its band follows the tier.
            var prevPremium = CoreEntitlement.HasPremiumProvider;
            CoreEntitlement.HasPremiumProvider = () => isLab;
            CoreEntitlement.IsFreeTodayProvider = k => k == freeKey;
            RacingAccess.IsGrantedProvider = id => id == "rt.original.03";
            string? opened = null;
            ExternalOpener.Shell = t => { opened = t; return true; };
            var root = Directory.CreateTempSubdirectory("play-nine-games-");
            var prevAssets = CorePaths.EffectiveAssetsProvider;
            CorePaths.EffectiveAssetsProvider = () => root.FullName;
            using var server = new WebAssetServer(root.FullName);
            GameWindow.Server = () => server;
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<Control>("PlayTab")!;
                T C<T>(string n) where T : Control => play.FindControl<T>(n)!;

                // Layout: WPF order, three columns, no "Play" H1 above the subtitle.
                var grid = C<global::Avalonia.Controls.Primitives.UniformGrid>("GamesGrid");
                Assert.Equal(3, grid.Columns);
                Assert.Equal(new[] { "SlotBreakoutDemo", "SlotBreakout", "SlotGoon", "SlotPieceByPiece", "SlotBackRoom",
                    "SlotDtrh", "SlotArcademy", "SlotRacingThoughts", "SlotRacingThoughtsClassic", "SlotWebApp" }, grid.Children.Select(c => c.Name));
                var wall = (StackPanel)C<ScrollViewer>("WallScroll").Content!;
                Assert.Equal(Loc.Get("pl6_play_subtitle"), Assert.IsType<TextBlock>(wall.Children[0]).Text);

                // Hostless doors refuse honestly; the T2 bands follow the tier.
                foreach (var n in new[] { "BtnPlayBackRoom", "BtnPlayArcademy" })
                {
                    Assert.False(C<Button>(n).IsEnabled, n);
                    Assert.Equal(Loc.Get("exclusives_not_on_this_build"), ToolTip.GetTip(C<Button>(n)));
                }
                Assert.True(C<Border>("PlayLockDtrh").IsVisible);
                Assert.True(C<Border>("PlayLockArcademy").IsVisible);
                Assert.True(C<Border>("PlayLockRacingThoughts").IsVisible);
                Assert.False(C<TierBadge>("PlayBadgeDtrh").FreeToday);
                // A DtRH drop day (WPF RequiresPremium(.., "dtrh") + SetFreeStamp): band off, FREE TODAY on.
                freeKey = "dtrh";
                ((global::ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView)play).RefreshPlayCards();
                Assert.False(C<Border>("PlayLockDtrh").IsVisible);
                Assert.True(C<TierBadge>("PlayBadgeDtrh").FreeToday);
                Assert.True(C<Border>("PlayLockArcademy").IsVisible);
                isLab = true;
                ((global::ConditioningControlPanel.Avalonia.Views.Tabs.PlayTabView)play).RefreshPlayCards();
                Assert.False(C<Border>("PlayLockDtrh").IsVisible);
                Assert.False(C<Border>("PlayLockArcademy").IsVisible);
                Assert.False(C<Border>("PlayLockRacingThoughts").IsVisible);
                Assert.False(C<TierBadge>("PlayBadgeDtrh").FreeToday); // an owner gets no gift

                // DtRH uses the existing launcher door, including its account check.
                Assert.True(C<Button>("BtnPlayDtrh").IsEnabled);
                C<Button>("BtnPlayDtrh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Single(shell.OwnedWindows.OfType<LoginDialog>()).Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(GameWindow.OpenForTests("dtrh"));

                // Racing Thoughts signed out: the login door, no race.
                var race = C<Button>("BtnPlayRacingThoughts");
                race.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Single(shell.OwnedWindows.OfType<LoginDialog>()).Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(GameWindow.OpenForTests("glassroad"));

                // Signed in with Lab access: the base's DtRH host, then Glassroad (WPF LauncherCatalogue.cs:235-238).
                isLoggedIn = true;
                C<Button>("BtnPlayDtrh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("/dtrh/index.html", GameWindow.OpenForTests("dtrh")!.PageUrl!.AbsolutePath);
                GameWindow.CloseAllForPanic();
                Dispatcher.UIThread.RunJobs();
                race.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("/glassroad/index.html", GameWindow.OpenForTests("glassroad")!.PageUrl!.AbsolutePath);
                GameWindow.CloseAllForPanic();
                Dispatcher.UIThread.RunJobs();

                // Web App: the rail door's URL (or its no-browser notice under the sandbox), then the beat retires.
                service.Current.SeenFeatureIntros.Remove(MainShellWindow.WebBannerSeenKey);
                C<Button>("BtnPlayWebApp").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                if (shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault() is { } notice)
                {
                    Assert.Contains("https://app.cclabs.app", notice.FindControl<TextBlock>("TxtMessage")!.Text);
                    notice.Close();
                    Dispatcher.UIThread.RunJobs();
                }
                else Assert.Equal("https://app.cclabs.app", opened);
                Assert.Contains(MainShellWindow.WebBannerSeenKey, service.Current.SeenFeatureIntros);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                shell.Close();
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, RacingAccess.IsGrantedProvider, CoreEntitlement.IsFreeTodayProvider) = (loggedIn, lab, owns, prevFree);
                CoreEntitlement.HasPremiumProvider = prevPremium;
                GameWindow.UseLinuxRaceCard = prevLinuxCard;
                (GameWindow.Server, ExternalOpener.Shell, CorePaths.EffectiveAssetsProvider) = (prevServer, prevShell, prevAssets);
                service.SealForReset();
                CoreSettings.ServiceProvider = prevSettings;
                root.Delete(true);
            }
        });
    }

    [Fact]
    public void PlayEntranceUsesShellTiming_AndCleansUpOnLeaveOrClose()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            LabWebcamStatusTests.Setup();
            var settings = CoreSettings.Current;
            var previous = settings.MotionLevel;
            settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            var shell = new MainShellWindow { TabClock = new FrozenEntranceClock() };
            try
            {
                shell.Show();
                shell.ShowTab("appsettings");
                shell.CompleteTabTransition();
                var play = shell.Named<PlayTabView>("PlayTab")!;
                Assert.False(play.IsVisible); // hidden startup attach starts no Play-local animation
                var targets = MainShellWindow.FindStaggerTargets(play).ToArray();
                Assert.Equal(8, targets.Length);
                Assert.Equal("GamesGrid", targets[2].Name);
                void AtRest(Control target)
                {
                    Assert.Equal(1, target.Opacity);
                    Assert.Equal(0, Assert.IsType<global::Avalonia.Media.TranslateTransform>(target.RenderTransform).Y);
                }

                shell.ShowTab("play");
                Assert.Same(play, shell.EnteringTab);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                shell.StepTabTransition(0);
                // WPF Services/MotionFx.cs:123-138: BeginTime leaves delayed cards at base values.
                // Rendering is essential: a competing backward-fill animation overrides local values.
                AtRest(targets[^1]);
                for (var i = 0; i < targets.Length; i++)
                {
                    var delay = 40 * Math.Min(i, 6);
                    if (delay > 0)
                    {
                        shell.StepTabTransition(delay - 1);
                        AtRest(targets[i]);
                    }
                    shell.StepTabTransition(delay);
                    Assert.Equal(0, targets[i].Opacity);
                    Assert.Equal(10, Assert.IsType<global::Avalonia.Media.TranslateTransform>(targets[i].RenderTransform).Y);
                    shell.StepTabTransition(delay + 110);
                    Assert.Equal(0.75, targets[i].Opacity, 5); // half of the 220 ms quadratic fade
                }
                shell.StepTabTransition(250);
                Assert.Equal(targets[6].Opacity, targets[7].Opacity); // six-slot cap
                Assert.Equal(0.62, play.FindControl<Border>("PlayFocusCard")!.Opacity);

                shell.ShowTab("appsettings");
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Assert.All(targets, AtRest); // WPF ChromeFx.cs:400 cleanup on a new entrance

                settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Reduced;
                shell.ShowTab("play");
                Assert.Same(play, shell.EnteringTab);
                Assert.All(targets, AtRest);
                shell.StepTabTransition(100);
                Assert.Equal(0.75, play.Opacity, 5); // WPF ChromeFx.cs:244 retains the crossfade
                settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
                shell.ShowTab("appsettings");
                shell.ShowTab("play");
                Assert.Null(shell.EnteringTab);
                Assert.Equal(1, play.Opacity);
                Assert.All(targets, AtRest);

                settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
                shell.ShowTab("appsettings");
                shell.ShowTab("play");
                shell.StepTabTransition(50);
                shell.Close();
                Assert.Null(shell.EnteringTab);
                Assert.All(targets, AtRest);
            }
            finally { shell.Close(); settings.MotionLevel = previous; }
        });
    }

    private sealed class FrozenEntranceClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    [Fact]
    public void LanguageChangeRepaintsTrackerText_OnlyWhileAttached()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var play = new PlayTabView();
            var host = new Window { Content = play };
            var language = LocalizationManager.Instance.CurrentLanguage;
            try
            {
                host.Show();
                LocalizationManager.Instance.SetLanguage("en");
                Dispatcher.UIThread.RunJobs();
                var text = play.FindControl<TextBlock>("TxtWebcamStatusChipPlay")!;
                text.Text = "repaint probe";
                LocalizationManager.Instance.SetLanguage("de");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("rf_webcam_stopped"), text.Text);
                host.Content = null;
                // The label is a loc binding (WebcamStatusPresentation.Paint binds the key), so it follows the
                // language on its own; detached, the view runs no repaint of its own.
                LocalizationManager.Instance.SetLanguage("en");
                Dispatcher.UIThread.RunJobs();
                host.Content = play;
                Assert.Equal(Loc.Get("rf_webcam_stopped"), text.Text);
            }
            finally { host.Close(); LocalizationManager.Instance.SetLanguage(language); }
        });
    }

    [Theory]
    [InlineData("PlayBackRoomHeroPlate", "features/backroom.png")]
    [InlineData("PlayDtrhHeroPlate", "features/dtrh.png")]
    [InlineData("PlayArcademyHeroPlate", "features/arcademy.png")]
    [InlineData("PlayRacingThoughtsClassicHeroPlate", "features/race_classic.png")]
    [InlineData("PlayRacingThoughtsHeroPlate", "features/race.png")]
    [InlineData("PlayWebAppHeroPlate", "billboard/webapp.png")]
    public void AddedGameArtUsesModOverrides_OnAttachAndModChange(string plate, string art)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            LabWebcamStatusTests.Setup();
            var dir = Directory.CreateTempSubdirectory("play-art-");
            var png = Path.Combine(dir.FullName, "override.png");
            using (var bitmap = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96)))
                bitmap.Save(png);
            var previous = CoreModArt.OverridePathProvider;
            CoreModArt.OverridePathProvider = key => key == art ? png : null;
            var play = new PlayTabView();
            var host = new Window { Content = play };
            try
            {
                host.Show();
                var border = play.FindControl<Border>(plate)!;
                var brush = Assert.IsType<global::Avalonia.Media.ImageBrush>(border.Background);
                Assert.Equal(global::Avalonia.Media.Stretch.UniformToFill, brush.Stretch);
                Assert.Equal(6, Assert.IsType<global::Avalonia.Media.Imaging.Bitmap>(brush.Source).PixelSize.Width);
                border.Background = null;
                CoreMods.RaiseModChanged(null, new ConditioningControlPanel.Models.ModPackage(new ConditioningControlPanel.Models.ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                Assert.IsType<global::Avalonia.Media.ImageBrush>(border.Background);
            }
            finally { host.Close(); CoreModArt.OverridePathProvider = previous; dir.Delete(true); }
        });
    }

    [Theory]
    [InlineData("BtnPlayDtrh", "game.dtrh")]
    [InlineData("BtnPlayRacingThoughts", "game.race")]
    [InlineData("BtnPlayRacingThoughtsClassic", "game.raceclassic")]
    public void GameCardsPinAndUnpinThroughTheirContextMenu(string buttonName, string id)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            LabWebcamStatusTests.Setup();
            var settings = CoreSettings.Current;
            var favorites = settings.RailFavorites.ToArray();
            settings.RailFavorites.Clear();
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("play");
                shell.CompleteTabTransition();
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<PlayTabView>("PlayTab")!;
                var button = play.FindControl<Button>(buttonName)!;
                var menu = Assert.IsType<ContextMenu>(button.ContextMenu);
                // WPF MainWindow/MainWindow.FavoritesRail.cs:78-79 maps the hostless cards too.
                foreach (var name in new[] { "BtnPlayBackRoom", "BtnPlayArcademy" })
                    Assert.IsType<ContextMenu>(play.FindControl<Button>(name)!.ContextMenu);

                // WPF FavoritesRail.cs:488-500: right-click opens the shared Pin/Unpin menu.
                button.RaiseEvent(new ContextRequestedEventArgs());
                Dispatcher.UIThread.RunJobs();
                Assert.True(menu.IsOpen);
                var pin = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
                Assert.Equal(Loc.Get("rail_pin"), pin.Header);
                Assert.True(pin.IsEnabled);
                pin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { id }, settings.RailFavorites);
                Assert.Equal(id.Substring("game.".Length), SettingsPaletteIndex.All.Single(e => e.Id == id).GameId);

                button.RaiseEvent(new ContextRequestedEventArgs());
                Dispatcher.UIThread.RunJobs();
                Assert.True(menu.IsOpen);
                var unpin = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
                Assert.Equal(Loc.Get("rail_unpin"), unpin.Header);
                unpin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(settings.RailFavorites);
            }
            finally
            {
                shell.Close();
                settings.RailFavorites.Clear();
                settings.RailFavorites.AddRange(favorites);
            }
        });
    }

}
