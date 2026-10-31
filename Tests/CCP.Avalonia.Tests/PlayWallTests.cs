using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
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
    [InlineData("BtnPlayRacingThoughts", "race")]
    public void AvailableGameCardUsesTheExistingLauncherDoor(string buttonName, string gameId)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var previous = PlayTabView.LaunchGame;
            var launches = new System.Collections.Generic.List<string>();
            PlayTabView.LaunchGame = id => launches.Add(id);
            try
            {
                var play = new PlayTabView();
                var button = play.FindControl<Button>(buttonName)!;
                Assert.True(button.IsEnabled);
                Assert.Null(ToolTip.GetTip(button));
                // WPF PlayTabView.Cards.cs:59-66 forwards the click; the host runs the access gate.
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(gameId, Assert.Single(launches));
            }
            finally { PlayTabView.LaunchGame = previous; }
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
            Assert.Equal(new[] { "SlotBackRoom", "SlotDtrh", "SlotArcademy", "SlotRacingThoughts", "SlotWebApp" },
                games.Children.Skip(4).Select(c => c.Name));
            foreach (var name in new[] { "BackRoom", "Dtrh", "Arcademy", "RacingThoughts", "WebApp" })
                Assert.NotNull(C<Border>("Play" + name + "HeroPlate").Background);
            // WPF PlayCardT2 (PlayTabView.xaml:131-132): tier-2 living-metal rim, 3 px.
            foreach (var slot in new[] { "SlotBreakout", "SlotDtrh", "SlotArcademy" })
            {
                var card = (Border)C<Grid>(slot).Children[0];
                Assert.Equal(2, ConditioningControlPanel.Avalonia.Controls.TierFxBorder.GetTier(card));
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

                // GAMES: the full Breakout and Goon have no host on this head, so their Play button
                // refuses honestly; the Breakout demo and Piece by Piece open their game as the launcher does.
                foreach (var n in new[] { "BtnPlayBreakout", "BtnPlayGoon" })
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

}
