using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FavoritesRailFinishTests
{
    private static Task InShell(Action<MainShellWindow, SettingsService> run) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.RailFavorites.Clear();
        s.RailRecent.Clear();
        s.MotionLevel = MotionLevel.Off;
        s.DashboardInvertClicks = s.FavoritesDrawerOpen = false;
        s.DashboardToggleHintUses = 0;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            run(shell, settings);
        }
        finally
        {
            shell?.Close();
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LogoHintRetiresAfterThreeRealToggleGesturesAndStaysRetired() => InShell((shell, settings) =>
    {
        var dash = shell.SettingsPage!;
        var hint = dash.FindControl<TextBlock>("DashToggleHint");
        Assert.NotNull(hint);
        Assert.Equal(Loc.Get("dash_toggle_hint"), hint.Text);
        Assert.False(hint.IsHitTestVisible);
        Assert.True(hint.IsVisible);
        var running = CoreSession.IsSessionRunningProvider;
        try
        {
            CoreSession.IsSessionRunningProvider = () => true;
            bool before = settings.Current.FlashEnabled;
            shell.ToggleWallFeature("flash");
            Assert.Equal(before, settings.Current.FlashEnabled);
            Assert.Equal(0, settings.Current.DashboardToggleHintUses);
        }
        finally { CoreSession.IsSessionRunningProvider = running; }
        var card = dash.FindControl<FeatureCard>("CardFlash")!;
        void Click(MouseButton button)
        {
            var at = card.TranslatePoint(new Point(30, 30), shell)!.Value;
            shell.MouseDown(at, button);
            shell.MouseUp(at, button);
            Dispatcher.UIThread.RunJobs();
        }
        settings.Current.DashboardInvertClicks = false;
        Click(MouseButton.Right);
        Assert.Equal(1, settings.Current.DashboardToggleHintUses);
        Assert.True(hint.IsVisible);
        // Count the learned toggle verb even after the persisted click preference swaps it.
        settings.Current.DashboardInvertClicks = true;
        Click(MouseButton.Left);
        Assert.Equal(2, settings.Current.DashboardToggleHintUses);
        Assert.Equal(Loc.Get("dash_click_swapped"), hint.Text);
        Assert.True(hint.IsVisible);
        Click(MouseButton.Left);
        Assert.Equal(3, settings.Current.DashboardToggleHintUses);
        Assert.False(hint.IsVisible);
        Click(MouseButton.Left);
        Assert.Equal(3, settings.Current.DashboardToggleHintUses);
        shell.ShowTab("studio");
        shell.ShowTab("settings");
        Assert.False(hint.IsVisible);
    });

    [Fact]
    public Task ModFramingUsesNormalizedKeysAndRepaintsOnModChange() => InShell((shell, settings) =>
    {
        var previousArt = CoreModArt.OverridePathProvider;
        var previousMod = CoreMods.ActiveModPackageProvider;
        var path = Path.Combine(Path.GetTempPath(), "ccp-rail-art-" + Guid.NewGuid().ToString("N") + ".png");
        using (var input = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/vibe.png")))
        using (var output = File.Create(path)) input.CopyTo(output);
        var framing = new ModArtFraming { CenterX = 0.25, CenterY = 0.4, Zoom = 2 };
        var manifest = new ModManifest { ArtFraming = new Dictionary<string, Dictionary<string, ModArtFraming>>
        {
            ["FEATURES\\VIBE.PNG"] = new() { ["RAILCHIP"] = framing },
        } };
        var package = new ModPackage(manifest, null, isBuiltIn: false);
        try
        {
            CoreModArt.OverridePathProvider = key => key == "features/vibe.png" ? path : null;
            CoreMods.ActiveModPackageProvider = () => package;
            settings.Current.RailFavorites.Add("tab.haptics");
            shell.RefreshFavoritesRail();
            Button Chip() => shell.SettingsPage!.FindControl<StackPanel>("FavoritesList")!.Children.OfType<Button>().Single();
            var brush = Assert.IsType<ImageBrush>(Chip().Background);
            Assert.IsAssignableFrom<global::Avalonia.Media.Imaging.Bitmap>(brush.Source);
            // Decode dimensions round at the probe width; the sharpened bitmap can round differently.
            using var input = File.OpenRead(path);
            using var probe = global::Avalonia.Media.Imaging.Bitmap.DecodeToWidth(input, FavoritesRailArt.BaseDecodeWidth);
            double aspect = probe.Size.Width / probe.Size.Height;
            var expected = ModArtFramingRegistry.ResolveViewbox("features/vibe.png", ModArtFramingRegistry.SurfaceRailChip,
                true, aspect, framing);
            Assert.Equal(new RelativeRect(expected.X, expected.Y, expected.Width, expected.Height, RelativeUnit.Relative), brush.SourceRect);
            Assert.True(brush.SourceRect.Rect.Width < 1);
            manifest.ArtFraming.Clear(); // no author framing => centre crop, never our shipped crop
            CoreMods.RaiseModChanged(null, package);
            Dispatcher.UIThread.RunJobs();
            var centered = ModArtFramingRegistry.ResolveViewbox("features/vibe.png", ModArtFramingRegistry.SurfaceRailChip,
                true, aspect, null);
            Assert.Equal(new RelativeRect(centered.X, centered.Y, centered.Width, centered.Height, RelativeUnit.Relative),
                Assert.IsType<ImageBrush>(Chip().Background).SourceRect);
        }
        finally
        {
            CoreModArt.OverridePathProvider = previousArt;
            CoreMods.ActiveModPackageProvider = previousMod;
            File.Delete(path);
        }
    });

    [Fact]
    public Task StudioPrizePillTracksGrantsWithoutMarkingUnrelatedChips() => InShell((shell, settings) =>
    {
        var owned = PrizeOwnership.IsGranted;
        var premium = CoreEntitlement.HasPremiumProvider;
        var free = CoreEntitlement.IsFreeTodayProvider;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            PrizeOwnership.IsGranted = _ => false;
            settings.Current.RailFavorites.AddRange(new[] { "door.studio", "tab.haptics" });
            shell.RefreshFavoritesRail();
            Button Chip(string id) => shell.SettingsPage!.FindControl<StackPanel>("FavoritesList")!.Children.OfType<Button>().Single(b => Equals(b.Tag, id));
            Border? Pill(Button chip) => ((Grid)chip.Content!).Children.OfType<Border>().SingleOrDefault(b => b.Child is TextBlock t && t.Text == Loc.Get("badge_v2"));
            Assert.Null(Pill(Chip("door.studio")));
            PrizeOwnership.IsGranted = id => id == PrizeGrants.BubbleRain;
            PrizeGrants.RaiseGrantsChanged();
            Dispatcher.UIThread.RunJobs();
            var pill = Pill(Chip("door.studio"));
            Assert.NotNull(pill);
            Assert.Equal(HorizontalAlignment.Right, pill.HorizontalAlignment);
            Assert.False(pill.IsHitTestVisible);
            Assert.Null(Pill(Chip("tab.haptics")));
            PrizeOwnership.IsGranted = _ => false;
            PrizeGrants.RaiseGrantsChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(Pill(Chip("door.studio")));
        }
        finally
        {
            PrizeOwnership.IsGranted = owned;
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.IsFreeTodayProvider = free;
        }
    });

    [Fact]
    public Task PageDrawerPushesContentPinsAndClosesOnNavigationAndPanic() => InShell((shell, settings) =>
    {
        var drawer = shell.FindControl<Grid>("PageDrawer");
        Assert.NotNull(drawer);
        Assert.False(drawer.IsVisible);
        shell.ShowTab("studio");
        Dispatcher.UIThread.RunJobs();
        Assert.True(drawer.IsVisible);
        var handle = drawer.FindControl<Button>("PageFavoritesHandle")!;
        var body = drawer.FindControl<Grid>("PageFavoritesBody")!;
        Assert.Equal(0, body.Width);
        var studio = shell.FindControl<Control>("StudioTab")!;
        double before = studio.Bounds.Width;
        handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(92, body.Width);
        Assert.Equal(before - 92, studio.Bounds.Width, 1);
        Assert.False(settings.Current.FavoritesDrawerOpen); // page drawer never persists Home's choice
        shell.ShowTab("haptics");
        Assert.Equal(0, body.Width);
        shell.TogglePinned("tab.haptics");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(92, body.Width);
        var chip = drawer.FindControl<StackPanel>("PageFavoritesList")!.Children.OfType<Button>().Single();
        Assert.Equal("tab.haptics", chip.Tag);
        Assert.NotNull(chip.ContextMenu);
        PanicSurfaces.All.Single(s => s.Id == "favorites-drawers").Stop(shell);
        Assert.Equal(0, body.Width);
        shell.ShowTab("settings");
        Assert.False(drawer.IsVisible);
    });
}
