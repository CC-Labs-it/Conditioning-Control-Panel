using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Declutter wave 1 (2026-11-15, twin of WPF HudDepthTests / HeaderBannerFxTests): START is the
/// one filled button on the bottom bar, Save and Exit are outline planks sized to their label, and the
/// header banner shows on Home only.</summary>
public sealed class DeclutterBarTests
{
    private static void Run(Action<MainShellWindow> check) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.OfflineMode = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();
            check(shell);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private static bool Filled(Button b) => b.Background switch
    {
        null => false,
        ISolidColorBrush solid => solid.Color.A != 0 && solid.Opacity > 0,
        _ => true,
    };

    [Fact]
    public void StartIsTheOnlyFilledButtonAndSaveAndExitAreOutlinePlanks() => Run(shell =>
    {
        var start = shell.Named<Button>("BtnStart")!;
        var save = shell.Named<Button>("BtnSaveAll")!;
        var exit = shell.Named<Button>("BtnExit")!;
        Assert.True(Filled(start));
        Assert.False(Filled(save));
        Assert.False(Filled(exit));

        // START flexes alone; Save and Exit size to their labels.
        var grid = Assert.IsType<Grid>(start.Parent);
        Assert.Single(grid.ColumnDefinitions, c => c.Width.IsStar);
        Assert.True(grid.ColumnDefinitions[Grid.GetColumn(start)].Width.IsStar);
        Assert.True(grid.ColumnDefinitions[Grid.GetColumn(save)].Width.IsAuto);
        Assert.True(grid.ColumnDefinitions[Grid.GetColumn(exit)].Width.IsAuto);
        Assert.True(start.Bounds.Width > save.Bounds.Width + exit.Bounds.Width);

        foreach (var b in new[] { save, exit })
        {
            Assert.Equal(84, b.MinWidth);
            Assert.Equal(14, b.FontSize);
            Assert.Equal(50, b.Height);
            Assert.True(b.Bounds.Width >= 84, b.Name);
            // the outline: the plate's border is the button's own BorderBrush, the face is clear at rest
            var plate = b.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "b");
            Assert.Same(b.BorderBrush, plate.BorderBrush);
            Assert.Equal(new Thickness(1), plate.BorderThickness);
            Assert.Equal(Colors.Transparent, ((ISolidColorBrush)plate.Background!).Color);
            // the parts HudPlank moves
            Assert.Contains(b.GetVisualDescendants().OfType<Border>(), x => x.Name == "DepthDrop");
            Assert.Contains(b.GetVisualDescendants().OfType<Grid>(), x => x.Name == "DepthFace");
        }

        // Save is neutral; Exit carries the danger colour on text and border only.
        var danger = Application.Current!.FindResource("DangerBrush");
        Assert.Same(shell.FindResource("TextSecondaryBrush"), save.Foreground);
        Assert.Same(shell.FindResource("TextMutedBrush"), save.BorderBrush);
        Assert.Same(danger, exit.Foreground);
        Assert.Same(danger, exit.BorderBrush);
    });

    [Fact]
    public void TheHeaderBannerShowsOnHomeOnly() => Run(shell =>
    {
        var host = shell.Named<Border>("HeaderBannerHost")!;
        var glow = shell.Named<Border>("HeaderBannerGlowHost")!;
        Assert.True(host.IsVisible);   // the shell lands on the dashboard
        Assert.True(glow.IsVisible);

        // Each key names the tab it lands on: "Settings" is the door's case fold, "progression" is the
        // dashboard's permanent alias (a real key on this head, the rule shows the banner there).
        // Every entry is checked; none is skipped.
        foreach (var (tab, lands, shows) in new[]
        {
            ("studio", "studio", false), ("appsettings", "appsettings", false), ("settings", "settings", true),
            ("companion", "companion", false), ("Settings", "settings", true), ("studio", "studio", false),
            ("progression", "progression", true),
        })
        {
            shell.ShowTab(tab);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(lands, shell.CurrentTab);
            Assert.True(shows == host.IsVisible, tab);
            Assert.True(shows == glow.IsVisible, tab);   // the halo never outlives the pill
            if (!shows) Assert.False(shell.BannerBreathing, tab);   // a collapsed banner runs no ambient loop
        }

        // An unknown key keeps the current tab and the banner as it was.
        shell.ShowTab("studio");
        shell.ShowTab("no-such-tab");
        Assert.Equal("studio", shell.CurrentTab);
        Assert.False(host.IsVisible);
    });

    // Owner call 2026-11-15: welcome lines and tips stay Home only, a live announcement shows everywhere.
    [Fact]
    public void ALiveAnnouncementShowsTheBannerOnEveryPageUntilItEnds() => Run(shell =>
    {
        var host = shell.Named<Border>("HeaderBannerHost")!;
        var glow = shell.Named<Border>("HeaderBannerGlowHost")!;

        // It arrives while another page is on screen: the banner appears there.
        shell.ShowTab("studio");
        Assert.False(host.IsVisible);
        shell.SetBannerAnnouncement("Servers restart at nine");
        Assert.True(shell.BannerAnnouncementLive);
        Assert.True(host.IsVisible);
        Assert.True(glow.IsVisible);

        // It stays through every tab change while it is live.
        foreach (var tab in new[] { "appsettings", "companion", "settings", "studio" })
        {
            shell.ShowTab(tab);
            Dispatcher.UIThread.RunJobs();
            Assert.True(host.IsVisible, tab);
            Assert.True(glow.IsVisible, tab);
        }

        // It ends off Home: the welcome line is back and the banner collapses again.
        shell.ClearBannerAnnouncement();
        Assert.False(shell.BannerAnnouncementLive);
        Assert.NotEqual("Servers restart at nine", shell.Named<TextBlock>("TxtBannerSecondary")!.Text);
        Assert.False(host.IsVisible);
        Assert.False(glow.IsVisible);
        Assert.False(shell.BannerBreathing);

        // It ends on Home: the banner stays, as Home's own. An account refresh ends it the same way.
        shell.ShowTab("settings");
        shell.SetBannerAnnouncement("Another one");
        shell.ClearBannerAnnouncement();
        Assert.True(host.IsVisible);
        shell.SetBannerAnnouncement("A third");
        shell.ShowTab("studio");
        Assert.True(host.IsVisible);
        shell.UpdateQuickLoginUI();
        Assert.False(shell.BannerAnnouncementLive);
        Assert.False(host.IsVisible);
    });

    // The header banner is Home only, so the restart notice (raised on Settings) is an announcement;
    // a language change re-localises the slot without ending a live one (WPF has no such hook).
    [Fact]
    public void TheRestartNoticeShowsOffHomeAndALanguageChangeKeepsALiveAnnouncement() => Run(shell =>
    {
        var host = shell.Named<Border>("HeaderBannerHost")!;
        var line = shell.Named<TextBlock>("TxtBannerSecondary")!;
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            shell.ShowTab("appsettings");
            Assert.False(host.IsVisible);
            var target = oldLanguage == "fr" ? "de" : "fr";
            shell.ApplyLanguageSelection(target);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(target, LocalizationManager.Instance.CurrentLanguage);
            Assert.True(shell.BannerAnnouncementLive);
            Assert.True(host.IsVisible);
            Assert.Equal(Loc.Get("msg_restart_to_apply"), line.Text);

            // A server announcement survives a later language change, text and all.
            shell.SetBannerAnnouncement("Servers restart at nine");
            LocalizationManager.Instance.SetLanguage(oldLanguage);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.BannerAnnouncementLive);
            Assert.True(host.IsVisible);
            Assert.Equal("Servers restart at nine", line.Text);

            // With no announcement live, the language hook re-localises the welcome line as before.
            shell.ClearBannerAnnouncement();
            var welcome = line.Text;
            shell.RelocaliseBannerSlot();
            Assert.False(shell.BannerAnnouncementLive);
            Assert.Equal(welcome, line.Text);
        }
        finally
        {
            LocalizationManager.Instance.SetLanguage(oldLanguage);
        }
    });

    // Save lost its focus ring with the slab; the outline plank carries its own :focus-visible, and the
    // Exit variant's hover (danger fill, white text) wins over the base theme's hover.
    [Fact]
    public void TheOutlinePlanksShowKeyboardFocusAndExitHoversDanger() => Run(shell =>
    {
        var save = shell.Named<Button>("BtnSaveAll")!;
        var exit = shell.Named<Button>("BtnExit")!;
        var light = shell.FindResource("TextLightBrush");
        var danger = shell.FindResource("DangerBrush");
        foreach (var b in new[] { save, exit })
        {
            var plate = b.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "b");
            ((IPseudoClasses)b.Classes).Set(":focus-visible", true);
            Assert.Same(light, plate.BorderBrush);
            Assert.Equal(new Thickness(2), plate.BorderThickness);
            ((IPseudoClasses)b.Classes).Set(":focus-visible", false);
            Assert.Same(b.BorderBrush, plate.BorderBrush);
            Assert.Equal(new Thickness(1), plate.BorderThickness);
        }

        ((IPseudoClasses)exit.Classes).Set(":pointerover", true);
        var exitPlate = exit.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "b");
        Assert.Same(danger, exitPlate.Background);
        Assert.Equal(Colors.White, ((ISolidColorBrush)exit.Foreground!).Color);
        ((IPseudoClasses)exit.Classes).Set(":pointerover", false);
        Assert.Same(danger, exit.Foreground);
    });
}
