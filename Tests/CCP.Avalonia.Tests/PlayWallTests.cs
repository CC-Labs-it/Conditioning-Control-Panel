using System;
using System.IO;
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
            bool isLoggedIn = false, isLab = false;
            string? freeKey = null;
            CoreAccount.IsLoggedInProvider = () => isLoggedIn;
            CoreEntitlement.HasLabProvider = () => isLab;
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
                    "SlotDtrh", "SlotArcademy", "SlotRacingThoughts", "SlotWebApp" }, grid.Children.Select(c => c.Name));
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
                // A DtRH drop day (WPF RequiresLab(.., "dtrh") + SetFreeStamp): band off, FREE TODAY on.
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
                Assert.Null(GameWindow.OpenForTests("race"));

                // Signed in with Lab access: the base's DtRH host, then the owned race track.
                isLoggedIn = true;
                C<Button>("BtnPlayDtrh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("/dtrh/index.html", GameWindow.OpenForTests("dtrh")!.PageUrl!.AbsolutePath);
                GameWindow.CloseAllForPanic();
                Dispatcher.UIThread.RunJobs();
                race.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("/dtrh/race.html", GameWindow.OpenForTests("race")!.PageUrl!.AbsolutePath);
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
                text.Text = "detached probe";
                LocalizationManager.Instance.SetLanguage("en");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("detached probe", text.Text);
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
    [InlineData("PlayRaceHeroPlate", "features/race.png")]
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

}
