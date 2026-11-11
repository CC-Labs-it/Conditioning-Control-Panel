using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
using ConditioningControlPanel.Models;
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
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        bool lab = false, free = false;
        CoreEntitlement.HasLabProvider = () => lab;
        CoreEntitlement.HasPremiumProvider = () => true; // Tier 1 alone must not unlock the base's Tier 2 door.
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
            foreach (var name in new[] { "BackRoom", "Arcademy" })
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
        finally { host.Close(); CoreEntitlement.HasLabProvider = oldLab; CoreEntitlement.HasPremiumProvider = oldPremium; CoreEntitlement.IsFreeTodayProvider = oldFree; }
    });

    // WPF PlayTabView.xaml:478,502,533,564,595 use packaged resources rather than mod overrides.
    [Fact]
    public void PackBoundGameArtIgnoresModOverridesOnInitialPaintAndModChange() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var directory = Directory.CreateTempSubdirectory("play-fixed-art-");
        var overridePath = Path.Combine(directory.FullName, "override.png");
        using (var image = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96))) image.Save(overridePath);
        var previous = CoreModArt.OverridePathProvider;
        var requests = new System.Collections.Generic.List<string>();
        CoreModArt.OverridePathProvider = path => { requests.Add(path); return overridePath; };
        Window? host = null;
        try
        {
            var play = new PlayTabView();
            host = new Window { Content = play };
            host.Show();
            void Check()
            {
                foreach (var name in new[] { "BackRoom", "Dtrh", "Arcademy", "RacingThoughts", "WebApp" })
                    Assert.IsType<Bitmap>(Assert.IsType<ImageBrush>(play.FindControl<Border>("Play" + name + "HeroPlate")!.Background).Source);
                foreach (var path in new[] { "features/backroom.png", "features/dtrh.png", "features/arcademy.png", "features/race.png", "billboard/webapp.png" })
                    Assert.DoesNotContain(path, requests);
                Assert.Contains("features/goon_game_tile.png", requests); // existing mod-aware plate still resolves overrides
                var goon = Assert.IsType<Bitmap>(Assert.IsType<ImageBrush>(play.FindControl<Border>("PlayGoonHeroPlate")!.Background).Source);
                Assert.Equal(256, goon.PixelSize.Height);
            }
            Check();
            var before = requests.Count;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.True(requests.Count > before);
            Check();
        }
        finally
        {
            host?.Close();
            CoreModArt.OverridePathProvider = previous;
            directory.Delete(true);
        }
    });

    // WPF PlayTabView.Cards.cs:65, MainWindow.Exclusives.cs:217-222 and GlassroadHostService.cs:57-82.
    [Fact]
    public void RacingThoughtsAsksForAccountThenChecksTierAndOpensGlassroad() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var oldAccount = CoreAccount.IsLoggedInProvider;
        var oldLab = CoreEntitlement.HasLabProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var oldDenied = CoreEntitlement.ShowDeniedHandler;
        var oldServer = GameWindow.Server;
        var oldDoor = GameWindow.GlassroadDoorEnv;
        GameWindow.GlassroadDoorEnv = () => "0";
        var oldLaunch = PlayTabView.LaunchGame;
        bool signedIn = false, lab = false;
        int denied = 0;
        CoreAccount.IsLoggedInProvider = () => signedIn;
        CoreEntitlement.HasLabProvider = () => lab;
        CoreEntitlement.HasPremiumProvider = () => true; // Tier 1 alone must not unlock the base's Tier 2 door.
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        CoreEntitlement.ShowDeniedHandler = _ => denied++;
        var root = Directory.CreateTempSubdirectory("play-glassroad-");
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
            var button = play.FindControl<Button>("BtnPlayRacingThoughts")!;
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(shell.OwnedWindows, w => w is LoginDialog { IsVisible: true });
            Assert.Equal(0, denied);
            Assert.Null(GameWindow.OpenForTests(GameWindow.GlassroadId));
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            Dispatcher.UIThread.RunJobs();
            signedIn = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, denied);
            Assert.Null(GameWindow.OpenForTests(GameWindow.GlassroadId));
            lab = true;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var game = Assert.IsType<GameWindow>(GameWindow.OpenForTests(GameWindow.GlassroadId));
            Assert.Equal("/glassroad/index.html", game.PageUrl!.AbsolutePath);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(game, GameWindow.OpenForTests(GameWindow.GlassroadId));
            lab = false;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, denied); // the door checks access even when the game is already open
            Assert.Null(GameWindow.Launch("play-race"));
            Assert.Equal(3, denied);
            Assert.Null(GameWindow.Launch("PLAY-RACE"));
            Assert.Equal(4, denied);
            PanicSurfaces.All.Single(s => s.Id == "games").Stop(shell);
            Assert.Null(GameWindow.OpenForTests(GameWindow.GlassroadId));
        }
        finally
        {
            GameWindow.CloseAllForPanic(); shell.Close();
            CoreAccount.IsLoggedInProvider = oldAccount; CoreEntitlement.HasLabProvider = oldLab; CoreEntitlement.HasPremiumProvider = oldPremium;
            CoreEntitlement.IsFreeTodayProvider = oldFree; CoreEntitlement.ShowDeniedHandler = oldDenied;
            GameWindow.Server = oldServer; GameWindow.GlassroadDoorEnv = oldDoor; PlayTabView.LaunchGame = oldLaunch; root.Delete(true);
        }
    });

    // WPF PlayTabView.Cards.cs:59-60, MainWindow.Exclusives.cs:217-222 and LauncherCatalogue.cs:240-249.
    [Fact]
    public void DtrhCardAsksForAccountThenUsesTheTierGateAndExistingHost() => AvaloniaTestDispatcher.Run(() =>
    {
        LabWebcamStatusTests.Setup();
        var oldAccount = CoreAccount.IsLoggedInProvider;
        var oldLab = CoreEntitlement.HasLabProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldFree = CoreEntitlement.IsFreeTodayProvider;
        var oldDenied = CoreEntitlement.ShowDeniedHandler;
        var oldServer = GameWindow.Server;
        var oldLaunch = PlayTabView.LaunchGame;
        bool signedIn = false, lab = false;
        int denied = 0;
        CoreAccount.IsLoggedInProvider = () => signedIn;
        CoreEntitlement.HasLabProvider = () => lab;
        CoreEntitlement.HasPremiumProvider = () => true; // Tier 1 alone must not unlock the base's Tier 2 door.
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
            CoreAccount.IsLoggedInProvider = oldAccount; CoreEntitlement.HasLabProvider = oldLab; CoreEntitlement.HasPremiumProvider = oldPremium;
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


    [Fact]
    public void PlayLedgerHasOneStubRowWithRemainingWork()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "docs", "avalonia-parity.html"))) root = root.Parent;
        Assert.NotNull(root);
        var row = Assert.Single(File.ReadLines(Path.Combine(root!.FullName, "docs", "avalonia-parity.html"))
            .Where(line => line.StartsWith("<tr><td>views-tab-play</td>", StringComparison.Ordinal)));
        Assert.Contains("<td>stub</td>", row);
        Assert.Contains("Remaining work is Goon", row);
        Assert.Contains("outgoing fade", row);
    }

}
