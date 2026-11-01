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

                // Goon has no host on this head; the other cards route to their game host.
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
                    foreach (var n in new[] { "BtnPlayBreakoutDemo", "BtnPlayBreakout", "BtnPlayChess" })
                    {
                        var b = C<Button>(n);
                        Assert.True(b.IsEnabled, n);
                        Assert.Null(ToolTip.GetTip(b));
                        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                }
                finally { PlayTabView.LaunchGame = previousLaunch; }
                Assert.Equal(new[] { "breakoutdemo", "breakout", "piecebypiece" }, launched);
                // The free ids are launcher destinations; all three have a GameWindow host.
                Assert.All(launched, id => Assert.True(GameWindow.Games.ContainsKey(id), id));
                Assert.True(LauncherWindow.CanStartGame("breakoutdemo"));
                Assert.True(LauncherWindow.CanStartGame("piecebypiece"));
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

    // WPF PlayTabView.Cards.cs:45-46, BreakoutHostService.cs:14-16,
    // BreakoutAccess.cs:9-16 and BackRoomHostService.cs:589-599, 737-763.
    [Fact]
    public void FullBreakout_RequiresTierTwo_AndTracksAccessDuringTheGame()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var oldSettings = CoreSettings.ServiceProvider;
            var oldLab = CoreEntitlement.HasLabProvider;
            var oldDenied = CoreEntitlement.ShowDeniedHandler;
            var oldServer = GameWindow.Server;
            var oldLaunch = PlayTabView.LaunchGame;
            var root = Directory.CreateTempSubdirectory("play-full-breakout-");
            using var server = new WebAssetServer(root.FullName);
            bool allowed = false;
            int denied = 0;
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasLabProvider = () => allowed;
            CoreEntitlement.ShowDeniedHandler = _ => denied++;
            GameWindow.Server = () => server;
            PlayTabView.LaunchGame = id => GameWindow.Launch(id);
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var play = shell.Named<PlayTabView>("PlayTab")!;
                var button = play.FindControl<Button>("BtnPlayBreakout")!;
                Assert.True(button.IsEnabled);
                Assert.True(play.FindControl<Border>("PlayLockBreakout")!.IsVisible);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, denied);
                Assert.Null(GameWindow.OpenForTests("breakout"));

                allowed = true;
                play.RefreshPlayCards();
                Assert.False(play.FindControl<Border>("PlayLockBreakout")!.IsVisible);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var game = Assert.IsType<GameWindow>(GameWindow.OpenForTests("breakout"));
                var watch = Assert.IsType<DispatcherTimer>(typeof(GameWindow)
                    .GetField("_breakoutAccessWatch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(game));
                Assert.Equal(TimeSpan.FromSeconds(2), watch.Interval);
                Assert.True(watch.IsEnabled);
                Assert.Equal("/backroom/stations/breakout/play.html", game.PageUrl!.AbsolutePath);
                Assert.True(game.PageUrl.Query.Contains("desktop=1", StringComparison.Ordinal));
                Assert.False(game.PageUrl.Query.Contains("demo=1", StringComparison.Ordinal));
                var frames = new System.Collections.Generic.List<Newtonsoft.Json.Linq.JObject>();
                game.Posted += json => frames.Add(Newtonsoft.Json.Linq.JObject.Parse(json));
                game.Web.OnNavigationCompleted(game.PageUrl);
                game.Web.OnWebMessage("{\"type\":\"ready\"}");
                Assert.Equal(8, (int)frames.Single()["breakout"]!["storyLimit"]!);
                Assert.True((bool)frames[0]["breakout"]!["endless"]!);
                Assert.False((bool)frames[0]["breakout"]!["demo"]!);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(game, Assert.Single(GameWindow.OpenWindowsForTests()));

                // Losing entitlement pushes the demo projection on the two-second watch.
                allowed = false;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(2, denied);
                game.CheckBreakoutAccess(); // the two-second timer's tick
                Assert.Equal(2, frames.Count);
                Assert.Equal("settings", (string?)frames[1]["type"]);
                Assert.Equal(3, (int)frames[1]["breakout"]!["storyLimit"]!);
                Assert.False((bool)frames[1]["breakout"]!["endless"]!);
                Assert.True((bool)frames[1]["breakout"]!["demo"]!);
                allowed = true;
                game.CheckBreakoutAccess();
                Assert.Equal(8, (int)frames[2]["breakout"]!["storyLimit"]!);
                var demo = Newtonsoft.Json.Linq.JObject.FromObject(GameWindow.BreakoutEntitlementFor("breakoutdemo")!);
                Assert.Equal(3, (int)demo["storyLimit"]!);
                Assert.False((bool)demo["endless"]!);
                PanicSurfaces.All.Single(s => s.Id == "games").Stop(shell);
                Assert.Empty(GameWindow.OpenWindowsForTests());
                Assert.False(watch.IsEnabled);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                shell.Close();
                PlayTabView.LaunchGame = oldLaunch;
                GameWindow.Server = oldServer;
                CoreEntitlement.HasLabProvider = oldLab;
                CoreEntitlement.ShowDeniedHandler = oldDenied;
                service.SealForReset();
                CoreSettings.ServiceProvider = oldSettings;
                root.Delete(true);
            }
        });
    }

    [Theory]
    [InlineData("breakoutdemo", "breakout")]
    [InlineData("breakout", "breakoutdemo")]
    public async System.Threading.Tasks.Task BreakoutModes_AskBeforeReplacingTheOpenGame(string from, string to)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var oldServer = GameWindow.Server;
            var oldLab = CoreEntitlement.HasLabProvider;
            var oldConfirm = GameWindow.ConfirmBreakoutSwitch;
            var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
            var root = Directory.CreateTempSubdirectory("breakout-switch-");
            using var server = new WebAssetServer(root.FullName);
            GameWindow.Server = () => server;
            bool allowed = true;
            CoreEntitlement.HasLabProvider = () => allowed;
            try
            {
                LocalizationManager.Instance.SetLanguage("it");
                var first = GameWindow.Launch(from)!;
                Assert.Same(first, GameWindow.Launch(from));
                if (to == "breakout")
                {
                    allowed = false;
                    Assert.Null(GameWindow.Launch(to));
                    Assert.Empty(first.OwnedWindows);
                    allowed = true;
                }
                var switching = GameWindow.LaunchAsync(to);
                var dialog = Assert.Single(first.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>());
                Assert.Equal(Loc.Get("launcher_switch_title"), dialog.Title);
                Assert.Equal(Loc.Get("launcher_switch_body"), dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                var no = dialog.FindControl<Button>("BtnCancel")!;
                var yes = dialog.FindControl<Button>("BtnOk")!;
                Assert.True(no.IsDefault);
                Assert.Equal(Loc.Get("btn_no"), ((TextBlock)no.Content!).Text);
                Assert.Equal(Loc.Get("btn_yes"), ((TextBlock)yes.Content!).Text);
                Assert.Equal("Sì", ((TextBlock)yes.Content!).Text);
                Assert.False(switching.IsCompleted);
                Assert.Null(GameWindow.Launch(to)); // a second click cannot open another prompt
                Assert.Single(first.OwnedWindows);
                Assert.Same(first, Assert.Single(GameWindow.OpenWindowsForTests()));
                no.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(await switching);
                Assert.True(first.IsVisible);
                Assert.Same(first, Assert.Single(GameWindow.OpenWindowsForTests()));

                switching = GameWindow.LaunchAsync(to);
                dialog = Assert.Single(first.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>());
                if (to == "breakout") allowed = false; // WPF already checked the tier before asking
                dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var second = Assert.IsType<GameWindow>(await switching);
                Assert.Equal(to, second.Spec.Id);
                if (to == "breakout")
                {
                    var init = Newtonsoft.Json.Linq.JObject.FromObject(second.InitMessage());
                    Assert.Equal(3, (int)init["breakout"]!["storyLimit"]!);
                    allowed = true;
                }
                Assert.False(first.IsVisible);
                Assert.Same(second, Assert.Single(GameWindow.OpenWindowsForTests()));

                // A panic can close the old host while the confirmation is open.
                var answer = new System.Threading.Tasks.TaskCompletionSource<bool>();
                GameWindow.ConfirmBreakoutSwitch = owner => { Assert.Same(second, owner); return answer.Task; };
                switching = GameWindow.LaunchAsync(from);
                GameWindow.CloseAllForPanic();
                answer.SetResult(true);
                Assert.Equal(from, (await switching)!.Spec.Id);
                Assert.Single(GameWindow.OpenWindowsForTests());
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                GameWindow.ConfirmBreakoutSwitch = oldConfirm;
                LocalizationManager.Instance.SetLanguage(oldLanguage);
                GameWindow.Server = oldServer;
                CoreEntitlement.HasLabProvider = oldLab;
                root.Delete(true);
            }
        });
    }

    [Fact]
    public void FullBreakout_ProviderEventsRefreshImmediatelyAndUnsubscribeOnClose()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var oldServer = GameWindow.Server;
            var oldLab = CoreEntitlement.HasLabProvider;
            var oldSources = GameWindow.BreakoutTierSources;
            var root = Directory.CreateTempSubdirectory("breakout-tiers-");
            using var server = new WebAssetServer(root.FullName);
            using var patreon = new ProviderSubscription("patreon", () => new ConditioningControlPanel.Models.AppSettings());
            using var substar = new ProviderSubscription("substar", () => new ConditioningControlPanel.Models.AppSettings());
            GameWindow.Server = () => server;
            GameWindow.BreakoutTierSources = () => (patreon, substar);
            bool allowed = true;
            CoreEntitlement.HasLabProvider = () => allowed;
            int Subscribers(ProviderSubscription p) => ((Delegate?)typeof(ProviderSubscription)
                .GetField("TierChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(p))?.GetInvocationList().Length ?? 0;
            try
            {
                var game = GameWindow.Launch("breakout")!;
                Assert.Equal(1, Subscribers(patreon));
                Assert.Equal(1, Subscribers(substar));
                var frames = new System.Collections.Generic.List<Newtonsoft.Json.Linq.JObject>();
                game.Posted += json => frames.Add(Newtonsoft.Json.Linq.JObject.Parse(json));
                game.Web.OnNavigationCompleted(game.PageUrl!);
                game.Web.OnWebMessage("{\"type\":\"ready\"}");
                allowed = false;
                patreon.NotifyEntitlementRaised(ConditioningControlPanel.Models.PatreonTier.None);
                Assert.Equal(3, (int)frames[1]["breakout"]!["storyLimit"]!);
                allowed = true;
                System.Threading.Tasks.Task.Run(() => substar.NotifyEntitlementRaised(ConditioningControlPanel.Models.PatreonTier.Level2)).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(8, (int)frames[2]["breakout"]!["storyLimit"]!);
                allowed = false;
                System.Threading.Tasks.Task.Run(() => patreon.NotifyEntitlementRaised(ConditioningControlPanel.Models.PatreonTier.None)).GetAwaiter().GetResult();
                GameWindow.BreakoutTierSources = () => (null, null); // the providers may have been replaced
                game.Close(); // a queued event must not post after close
                Assert.Equal(0, Subscribers(patreon));
                Assert.Equal(0, Subscribers(substar));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(3, frames.Count);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                GameWindow.BreakoutTierSources = oldSources;
                GameWindow.Server = oldServer;
                CoreEntitlement.HasLabProvider = oldLab;
                root.Delete(true);
            }
        });
    }

}
