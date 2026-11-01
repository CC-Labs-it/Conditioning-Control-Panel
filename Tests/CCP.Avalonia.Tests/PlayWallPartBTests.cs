using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PlayWallPartBTests
{
    // WPF PlayTabView.xaml:335-349, 467-607 and MainWindow.PlayTab.cs:129-132, 179-195.
    [Fact]
    public void GamesWallHasWpfOrderAndPaintsTierAndDailyState() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var oldLab = CoreEntitlement.HasLabProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        bool lab = false, free = false;
        CoreEntitlement.HasLabProvider = () => lab;
        CoreEntitlement.IsFreeTodayProvider = key => free && key == "dtrh";
        var play = new PlayTabView();
        var host = new Window { Content = play };
        try
        {
            host.Show();
            var grid = play.FindControl<UniformGrid>("GamesGrid")!;
            Assert.Equal(3, grid.Columns);
            Assert.Equal(new[] { "SlotBreakoutDemo", "SlotBreakout", "SlotGoon", "SlotPieceByPiece", "SlotBackRoom", "SlotDtrh", "SlotArcademy", "SlotRacingThoughts", "SlotWebApp" }, grid.Children.Select(c => c.Name));
            var scroll = play.FindControl<ScrollViewer>("WallScroll")!;
            Assert.Equal(Loc.Get("pl6_play_subtitle"), Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(scroll.Content).Children[0]).Text);
            foreach (var name in new[] { "BackRoom", "Dtrh", "Arcademy", "RacingThoughts", "WebApp" })
                Assert.NotNull(play.FindControl<Border>("Play" + name + "HeroPlate")!.Background);
            foreach (var name in new[] { "BackRoom", "Arcademy", "RacingThoughts" })
            {
                var button = play.FindControl<Button>("BtnPlay" + name)!;
                Assert.False(button.IsEnabled);
                Assert.Equal(Loc.Get("exclusives_not_on_this_build"), ToolTip.GetTip(button));
            }
            foreach (var name in new[] { "Dtrh", "Arcademy" })
            {
                var card = Assert.IsType<Border>(play.FindControl<Grid>("Slot" + name)!.Children[0]);
                Assert.Equal(2, TierFxBorder.GetTier(card));
                Assert.Equal(3, TierFxBorder.GetRimThickness(card));
            }
            var band = play.FindControl<Border>("PlayLockDtrh")!;
            var badge = play.FindControl<TierBadge>("PlayBadgeDtrh")!;
            Assert.True(band.IsVisible);
            Assert.True(play.FindControl<Border>("PlayLockArcademy")!.IsVisible);
            Assert.True(play.FindControl<Border>("PlayLockRacingThoughts")!.IsVisible);
            Assert.False(badge.FreeToday);
            free = true;
            play.RefreshPlayCards();
            Assert.False(band.IsVisible);
            Assert.True(badge.FreeToday);
            lab = true;
            play.RefreshPlayCards();
            Assert.False(band.IsVisible);
            Assert.False(badge.FreeToday);
            Assert.False(play.FindControl<Border>("PlayLockArcademy")!.IsVisible);
            Assert.False(play.FindControl<Border>("PlayLockRacingThoughts")!.IsVisible);
        }
        finally { host.Close(); CoreEntitlement.HasLabProvider = oldLab; CoreEntitlement.IsFreeTodayProvider = oldFree; }
    });

    // WPF PlayTabView.Cards.cs:59-60, MainWindow.Exclusives.cs:217-222 and LauncherCatalogue.cs:240-249.
    [Fact]
    public void DtrhCardAsksForAccountThenUsesTheTierGateAndExistingHost() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var oldAccount = CoreAccount.IsLoggedInProvider;
        var oldLab = CoreEntitlement.HasLabProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var oldDenied = CoreEntitlement.ShowDeniedHandler;
        var oldServer = GameWindow.Server;
        var oldLaunch = PlayTabView.LaunchGame;
        bool signedIn = false, lab = false;
        int denied = 0;
        CoreAccount.IsLoggedInProvider = () => signedIn;
        CoreEntitlement.HasLabProvider = () => lab;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        CoreEntitlement.ShowDeniedHandler = _ => denied++;
        var root = Directory.CreateTempSubdirectory("play-dtrh-");
        using var server = new WebAssetServer(root.FullName);
        GameWindow.Server = () => server;
        PlayTabView.LaunchGame = id => GameWindow.Launch(id);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("play");
            Dispatcher.UIThread.RunJobs();
            var play = shell.Named<PlayTabView>("PlayTab")!;
            var button = play.FindControl<Button>("BtnPlayDtrh")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(shell.OwnedWindows, w => w is LoginDialog { IsVisible: true });
            Assert.Equal(0, denied);
            Assert.Null(GameWindow.OpenForTests("dtrh"));
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            Dispatcher.UIThread.RunJobs();
            signedIn = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, denied);
            Assert.Null(GameWindow.OpenForTests("dtrh"));
            lab = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var game = Assert.IsType<GameWindow>(GameWindow.OpenForTests("dtrh"));
            Assert.Equal("/dtrh/index.html", game.PageUrl!.AbsolutePath);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(game, GameWindow.OpenForTests("dtrh"));
            lab = false;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, denied); // the door checks access even when the game is already open
            PanicSurfaces.All.Single(s => s.Id == "games").Stop(shell);
            Assert.Null(GameWindow.OpenForTests("dtrh"));
        }
        finally
        {
            GameWindow.CloseAllForPanic(); shell.Close();
            CoreAccount.IsLoggedInProvider = oldAccount; CoreEntitlement.HasLabProvider = oldLab;
            CoreEntitlement.IsFreeTodayProvider = oldFree; CoreEntitlement.ShowDeniedHandler = oldDenied;
            GameWindow.Server = oldServer; PlayTabView.LaunchGame = oldLaunch; root.Delete(true);
        }
    });

    // WPF MainWindow.TabNavigation.cs:523-541 and MainWindow.LabTab.cs:356-375.
    [Fact]
    public void WebcamChipRepaintsOnReturnAndLanguageChange() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var lang = LocalizationManager.Instance.CurrentLanguage;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("play"); Dispatcher.UIThread.RunJobs();
            var play = shell.Named<PlayTabView>("PlayTab")!;
            var label = play.FindControl<TextBlock>("TxtWebcamStatusChipPlay")!;
            shell.ShowTab("settings");
            label.Text = "stale";
            shell.ShowTab("play");
            Assert.Equal(Loc.Get(WebcamStatus.TextKey(WebcamTracker.Instance.State)), label.Text);
            LocalizationManager.Instance.SetLanguage(lang == "it" ? "en" : "it");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get(WebcamStatus.TextKey(WebcamTracker.Instance.State)), label.Text);
        }
        finally { shell.Close(); LocalizationManager.Instance.SetLanguage(lang); Dispatcher.UIThread.RunJobs(); }
    });

    // WPF PlayTabView.Cards.cs:68-69 and MainWindow.PlayTab.cs:77-86.
    [Fact]
    public async System.Threading.Tasks.Task WebAppCardUsesTheShellOpenerAndRetiresItsBanner()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            LabWebcamStatusTests.Setup();
            var shell = new MainShellWindow();
            var seen = CoreSettings.Current.SeenFeatureIntros;
            bool hadKey = seen.Remove(MainShellWindow.WebBannerSeenKey);
            try
            {
                shell.Show(); shell.ShowTab("play"); Dispatcher.UIThread.RunJobs();
                var play = shell.Named<PlayTabView>("PlayTab")!;
                play.FindControl<Button>("BtnPlayWebApp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await System.Threading.Tasks.Task.Yield();
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.IsType<MessageDialog>(shell.OwnedWindows.Single(w => w is MessageDialog));
                Assert.Contains("https://app.cclabs.app", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                dialog.Close();
                await System.Threading.Tasks.Task.Yield();
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(MainShellWindow.WebBannerSeenKey, seen);
            }
            finally
            {
                foreach (var w in shell.OwnedWindows.ToList()) w.Close();
                shell.Close();
                if (!hadKey) seen.Remove(MainShellWindow.WebBannerSeenKey);
            }
        });
    }

}
