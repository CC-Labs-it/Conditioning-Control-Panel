using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class UiUpdatesTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                        .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions
                { UseHeadlessDrawing = false }).SetupWithoutStarting();
        global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private sealed class VideoHost : ConditioningControlPanel.Services.IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(ConditioningControlPanel.Services.AttentionVerdict verdict, int ms, Action then) { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningShellHonorsForceVideoOnLaunch(bool force) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (oldForce, oldPath) = (s.ForceVideoOnLaunch, s.StartupVideoPath);
        var oldVideo = CoreEngine.Video;
        var oldDelay = MainShellWindow.StartupVideoDelay;
        var scheduler = new ConditioningControlPanel.Services.MandatoryVideoScheduler(new VideoHost(), library: () => new[] { "test-video.mp4" });
        CoreEngine.Video = scheduler;
        s.ForceVideoOnLaunch = force; s.StartupVideoPath = null;
        MainShellWindow.StartupVideoDelay = TimeSpan.Zero;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Assert.True(shell.StartupVideo.IsCompletedSuccessfully);
            Assert.Equal(force, scheduler.IsPlaying);
            // A tray restore (Tray.cs Hide, then Show) re-fires Opened; WPF plays it once, from Loaded.
            scheduler.ForceCleanup();
            shell.Hide();
            shell.Show();
            Assert.False(scheduler.IsPlaying);
        }
        finally
        {
            scheduler.ForceCleanup(); shell.Close(); CoreEngine.Video = oldVideo;
            MainShellWindow.StartupVideoDelay = oldDelay;
            (s.ForceVideoOnLaunch, s.StartupVideoPath) = (oldForce, oldPath);
        }
    });

    [Fact]
    public void SignedOutAccountChipOpensLogin() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        CoreAccount.IsLoggedInProvider = () => false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Named<Button>("BtnAccountChip")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(shell.OwnedWindows.OfType<global::ConditioningControlPanel.Avalonia.Views.Dialogs.LoginDialog>());
        }
        finally
        {
            foreach (var dialog in shell.OwnedWindows.ToList()) dialog.Close();
            shell.Close();
        }
    });

    [Fact]
    public void AccountRefreshPaintsLoginOverlayNameAndCanonicalTier() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldName = CoreAccount.DisplayNameProvider;
        var oldPremium = CoreAccount.HasPremiumAccessProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        bool signedIn = false, premium = false, lab = false;
        CoreAccount.IsLoggedInProvider = () => signedIn;
        CoreAccount.DisplayNameProvider = () => "Test account";
        CoreAccount.HasPremiumAccessProvider = () => premium;
        CoreAccount.HasLabAccessProvider = () => lab;
        var shell = new MainShellWindow();
        try
        {
            shell.UpdateQuickLoginUI();
            Assert.True(shell.Named<Border>("XPBarLoginOverlay")!.IsVisible);
            Assert.Equal(0.3, shell.Named<Grid>("XPBarContent")!.Opacity);
            Assert.Equal(Loc.Get("account_chip_sign_in"), shell.Named<TextBlock>("TxtAccountChipName")!.Text);
            signedIn = true;
            shell.UpdateQuickLoginUI();
            Assert.False(shell.Named<Border>("XPBarLoginOverlay")!.IsVisible);
            Assert.Equal(1, shell.Named<Grid>("XPBarContent")!.Opacity);
            Assert.Equal("Test account", shell.Named<TextBlock>("TxtAccountChipName")!.Text);
            foreach (var tier in new[] { 1, 2, 0 })
            {
                premium = tier > 0; lab = tier == 2;
                shell.RefreshProfileBubble(); // the existing entitlement-change choke point
                Assert.Equal(tier > 0, shell.Named<Control>("TxtAccountChipBadge")!.IsVisible);
                if (tier > 0)
                    Assert.Equal(global::Avalonia.Media.Color.Parse(tier == 2 ? "#FFB47BFF" : "#FFF0C24B"),
                        Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(shell.Named<global::ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtAccountChipBadge")!.Foreground).Color);
                Assert.Equal(global::Avalonia.Media.Color.Parse(tier == 2 ? "#B47BFF" : tier == 1 ? "#FFD700" : "#3D3D60"),
                    Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(shell.Named<Button>("BtnAccountChip")!.BorderBrush).Color);
            }
        }
        finally
        {
            shell.Close();
            CoreAccount.IsLoggedInProvider = oldLogin; CoreAccount.DisplayNameProvider = oldName;
            CoreAccount.HasPremiumAccessProvider = oldPremium; CoreAccount.HasLabAccessProvider = oldLab;
        }
    });

    [Fact]
    public void StatRefreshRepairsMicPillAndRebuildsXpBonusChips() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var s = CoreSettings.Current;
        var skills = s.UnlockedSkills.ToList();
        var (consent, wake) = (s.MicConsentGiven, s.SpeechWakeWordEnabled);
        var shell = new MainShellWindow();
        try
        {
            s.UnlockedSkills.Clear();
            s.UnlockedSkills.Add("sparkle_boost_1");
            s.MicConsentGiven = s.SpeechWakeWordEnabled = true;
            shell.Named<Border>("MicActivePill")!.IsVisible = false;
            shell.UpdateStatPills();
            Assert.True(shell.Named<Border>("MicActivePill")!.IsVisible);
            var chip = Assert.Single(shell.Named<StackPanel>("XPBarBonusList")!.Children.OfType<Border>());
            Assert.Equal("+10% Sparkle Boost", Assert.IsType<TextBlock>(chip.Child).Text);
            Assert.Equal(new Thickness(6, 3, 6, 3), chip.Padding);
            Assert.Equal("Skill tree bonus: +10% XP from Sparkle Boost", ToolTip.GetTip(chip));
            s.MicConsentGiven = false;
            s.UnlockedSkills.Clear();
            shell.UpdateStatPills();
            Assert.False(shell.Named<Border>("MicActivePill")!.IsVisible);
            Assert.Empty(shell.Named<StackPanel>("XPBarBonusList")!.Children);
        }
        finally
        {
            shell.Close();
            System.Globalization.CultureInfo.CurrentCulture = previousCulture;
            (s.MicConsentGiven, s.SpeechWakeWordEnabled) = (consent, wake);
            s.UnlockedSkills.Clear(); s.UnlockedSkills.AddRange(skills);
        }
    });

    [Fact]
    public void StatTipsAndBonusChipsFollowModAndLanguage() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        if (AvApp.Mods is null) AvApp.StartMods();
        var mods = AvApp.Mods!;
        var oldMod = mods.ActiveModId;
        var loc = LocalizationManager.Instance;
        var oldLanguage = loc.CurrentLanguage;
        var oldProvider = CoreMods.MakeModAwareProvider;
        var skills = CoreSettings.Current.UnlockedSkills.ToList();
        mods.ActivateMod(BuiltInMods.CCPDefaultId);
        CoreSettings.Current.UnlockedSkills.Clear();
        CoreSettings.Current.UnlockedSkills.Add("sparkle_boost_1");
        var shell = new MainShellWindow();
        var tips = new[] { ("PillConditioningTime", "pink_hours", "tooltip_total_conditioning_time_pink_hours_skill"),
            ("PillOnlineUsers", "hive_mind", "tooltip_bimbos_online_now_hive_mind_skill"),
            ("PillRankPercentile", "popular_girl", "tooltip_your_rank_percentile_popular_girl_skill") };
        void Drain() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            shell.Show(); Drain();
            foreach (var (name, _, key) in tips)
                Assert.Equal(Loc.Get(key), ToolTip.GetTip(shell.Named<Border>(name)!));
            mods.ActivateMod(BuiltInMods.DronificationId);
            CoreMods.MakeModAwareProvider = text => "Drone " + text;
            CoreMods.RaiseModChanged(null, mods.ActiveMod); Drain();
            foreach (var (name, skill, _) in tips)
                Assert.Equal(mods.ActiveMod.Manifest.EnhancementOverrides!.StatPillTooltips![skill], ToolTip.GetTip(shell.Named<Border>(name)!));
            var chip = Assert.Single(shell.Named<StackPanel>("XPBarBonusList")!.Children.OfType<Border>());
            var label = Assert.IsType<TextBlock>(chip.Child);
            Assert.Contains("Drone ", label.Text);
            Assert.Equal(global::Avalonia.Media.Color.Parse(mods.GetAccentLightColorHex()),
                Assert.IsAssignableFrom<global::Avalonia.Media.ISolidColorBrush>(label.Foreground).Color);
            loc.SetLanguage("de"); Drain();
            foreach (var (name, skill, _) in tips)
                Assert.Equal(mods.ActiveMod.Manifest.EnhancementOverrides!.StatPillTooltips![skill], ToolTip.GetTip(shell.Named<Border>(name)!));
            mods.ActivateMod(BuiltInMods.CCPDefaultId); Drain();
            foreach (var (name, _, key) in tips)
                Assert.Equal(Loc.Get(key), ToolTip.GetTip(shell.Named<Border>(name)!));
            loc.SetLanguage("en"); Drain();
            foreach (var (name, _, key) in tips)
                Assert.Equal(Loc.Get(key), ToolTip.GetTip(shell.Named<Border>(name)!));
        }
        finally
        {
            shell.Close(); CoreMods.MakeModAwareProvider = oldProvider;
            mods.ActivateMod(oldMod); loc.SetLanguage(oldLanguage);
            CoreSettings.Current.UnlockedSkills.Clear(); CoreSettings.Current.UnlockedSkills.AddRange(skills);
            Drain();
        }
    });

    [Fact]
    public void ModChangeHidesJournalAndReturnsAnOpenJournalToDaily() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var old = CoreMods.ActiveModIdProvider;
        string id = BuiltInMods.CCPDefaultId;
        CoreMods.ActiveModIdProvider = () => id;
        var view = new QuestsTabView();
        var host = new Window { Content = view };
        try
        {
            host.Show();
            var button = view.FindControl<Button>("BtnQuestSubRoadmap")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(view.FindControl<Control>("RoadmapPanel")!.IsVisible);
            id = "community-mod";
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest { Id = id }, null, false));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(button.IsVisible);
            Assert.False(view.FindControl<Control>("RoadmapPanel")!.IsVisible);
            Assert.True(view.FindControl<Control>("DailyWeeklyPanel")!.IsVisible);
            id = BuiltInMods.BambiSleepId;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest { Id = id }, null, false));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsVisible);
        }
        finally { host.Close(); CoreMods.ActiveModIdProvider = old; }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task LeaderboardThemeFollowsModWithoutChangingMode(bool allTime)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var oldAccent = CoreMods.AccentColorHexProvider;
            var oldOffline = CoreSettings.Current.OfflineMode;
            string accent = "#123456";
            CoreMods.AccentColorHexProvider = () => accent;
            CoreSettings.Current.OfflineMode = true;
            var view = new LeaderboardTabView();
            var host = new Window { Content = view };
            try
            {
                host.Show();
                await view.SetLeaderboardMode(allTime);
                accent = "#ABCDEF";
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(allTime, view.IsAllTimeMode);
                var monthly = view.FindControl<Button>("BtnLeaderboardMonthly")!;
                var all = view.FindControl<Button>("BtnLeaderboardAllTime")!;
                var expected = global::Avalonia.Media.Color.Parse(accent);
                Assert.Equal(expected, Assert.IsType<global::Avalonia.Media.SolidColorBrush>(allTime ? monthly.Foreground : monthly.Background).Color);
                if (!allTime) Assert.Equal(expected, Assert.IsType<global::Avalonia.Media.SolidColorBrush>(all.Foreground).Color);
                Assert.True(view.TryFindResource("LeaderboardAccentBrush", out var rowBrush));
                Assert.Equal(allTime ? global::Avalonia.Media.Color.Parse("#FFD700") : expected,
                    Assert.IsType<global::Avalonia.Media.SolidColorBrush>(rowBrush).Color);
                var tint = Assert.IsType<global::Avalonia.Media.SolidColorBrush>(view.FindControl<Border>("YouBar")!.Background).Color;
                Assert.Equal(allTime ? global::Avalonia.Media.Color.Parse("#20FFD700") : global::Avalonia.Media.Color.Parse("#20ABCDEF"), tint);
            }
            finally { host.Close(); CoreMods.AccentColorHexProvider = oldAccent; CoreSettings.Current.OfflineMode = oldOffline; }
        });
    }
}
