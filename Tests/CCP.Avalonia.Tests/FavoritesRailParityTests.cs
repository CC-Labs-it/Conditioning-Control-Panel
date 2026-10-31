using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FavoritesRailParityTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.RailFavorites.Clear();
        s.RailRecent.Clear();
    }

    // WPF PageFavoritesDrawer.cs:185-214; MainWindow.xaml:2065;
    // MainWindow.FavoritesRail.cs:151-184,474: real off-Home pin, unpin and navigation.
    [Fact]
    public Task EveryPageDrawerSharesChipsAndClosesOnNavigation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        bool savedOpen = s.FavoritesDrawerOpen;
        var motionBefore = s.MotionLevel;
        s.MotionLevel = MotionLevel.Off; // Assert settled widths; the next test checks the Full slide.
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var drawer = shell.PageFavorites!;
            Assert.False(drawer.IsVisible);
            shell.ShowTab("haptics");
            Dispatcher.UIThread.RunJobs();
            Assert.True(drawer.IsVisible);
            Assert.False(drawer.IsOpen);
            Assert.Equal(0, drawer.Body.Width);
            Assert.True(drawer.FavoritesEmpty.IsVisible);
            Assert.Contains(drawer.RecentList.Children.OfType<Button>(), b => Equals(b.Tag, "tab.haptics"));
            Assert.Equal(1, Grid.GetColumn(drawer));
            Assert.Equal("Auto", ((Grid)drawer.Parent!).ColumnDefinitions[1].Width.ToString());
            drawer.Handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(drawer.IsOpen);
            Assert.Equal(92, drawer.Body.Width);
            Assert.Equal(savedOpen, s.FavoritesDrawerOpen);
            drawer.Handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(drawer.IsOpen);
            // Pin through the actual page pill menu.
            var row = shell.PageStrip!.PillFor("haptics")!;
            row.RaiseEvent(new global::Avalonia.Input.ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
            ((MenuItem)Assert.Single(row.ContextMenu!.Items)!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            row.ContextMenu.Close();
            Assert.True(drawer.IsOpen);
            var chip = Assert.IsType<Button>(Assert.Single(drawer.FavoritesList.Children));
            Assert.Equal("tab.haptics", chip.Tag);
            Assert.DoesNotContain(drawer.RecentList.Children.OfType<Button>(), b => Equals(b.Tag, "tab.haptics"));
            Assert.NotSame(chip, Assert.Single(shell.SettingsPage!.FindControl<StackPanel>("FavoritesList")!.Children));
            Assert.IsType<ImageBrush>(chip.Background);
            shell.ShowTab("play");
            Assert.False(drawer.IsOpen);
            Assert.Equal(0, drawer.Body.Width);
            Assert.Null(drawer.Body.Transitions);
            drawer.Handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("haptics", shell.CurrentTab);
            Assert.False(drawer.IsOpen);
            chip = (Button)Assert.Single(drawer.FavoritesList.Children);
            chip.RaiseEvent(new global::Avalonia.Input.ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
            var unpin = (MenuItem)Assert.Single(chip.ContextMenu!.Items)!;
            Assert.Equal(global::ConditioningControlPanel.Localization.Loc.Get("rail_unpin"), unpin.Header);
            unpin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            chip.ContextMenu.Close();
            Assert.Empty(drawer.FavoritesList.Children);
            Assert.True(drawer.FavoritesEmpty.IsVisible);
            shell.ShowTab("settings");
            Assert.False(drawer.IsVisible);
            Assert.False(drawer.IsOpen);
        }
        finally { shell.Close(); s.MotionLevel = motionBefore; }
        return Task.CompletedTask;
    });

    // WPF PageFavoritesDrawer.cs:159-214: paint and motion gate independent of saved Home state.
    [Fact]
    public Task PageDrawerPaintAndMotionFollowWpf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var before = s.MotionLevel;
        try
        {
            var drawer = new global::ConditioningControlPanel.Avalonia.Views.Controls.PageFavoritesDrawer();
            drawer.OnNavigated("play");
            s.MotionLevel = MotionLevel.Full;
            drawer.SetOpen(true, true);
            var slide = Assert.IsType<global::Avalonia.Animation.DoubleTransition>(Assert.Single(drawer.Body.Transitions!));
            Assert.Equal(System.TimeSpan.FromMilliseconds(200), slide.Duration);
            Assert.IsType<global::Avalonia.Animation.Easings.CubicEaseOut>(slide.Easing);
            s.MotionLevel = MotionLevel.Off;
            drawer.SetOpen(false, true);
            Assert.Null(drawer.Body.Transitions);
            Assert.Equal(0, drawer.Body.Width);
            var hue = Color.FromRgb(10, 20, 30);
            drawer.Paint(hue);
            Assert.Equal(Color.FromArgb(0x80, 10, 20, 30), Assert.IsType<SolidColorBrush>(drawer.Rail.BorderBrush).Color);
            drawer.OnNavigated(null);
            Assert.False(drawer.IsVisible);
        }
        finally { s.MotionLevel = before; }
        return Task.CompletedTask;
    });

    // WPF MainWindow.FavoritesRail.cs:92-93,256-265: a live grant repaints the Studio chip.
    [Fact]
    public Task OwnedPillRepaintsAndChipStillNavigates() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        s.OfflineMode = false;
        s.UnifiedId = "rail-test";
        CoreAccount.UnifiedUserId = "rail-test";
        s.AuthToken = "test-token";
        PrizeOwnership.IsGranted = PrizeOwnership.Held;
        s.RailFavorites.AddRange(new[] { "door.studio", "door.home" });
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Button Chip(string id) => shell.SettingsPage!.FindControl<StackPanel>("FavoritesList")!.Children
                .OfType<Button>().Single(c => Equals(c.Tag, id));
            Border? Pill(string id) => ((Grid)Chip(id).Content!).Children.OfType<Border>()
                .SingleOrDefault(b => b.Child is TextBlock t && t.Text == global::ConditioningControlPanel.Localization.Loc.Get("badge_v2"));
            Assert.Null(Pill("door.studio"));
            PrizeOwnership.ApplySnapshot("rail-test", 1, new[] { PrizeGrants.BubbleRain });
            Dispatcher.UIThread.RunJobs();
            var pill = Assert.IsType<Border>(Pill("door.studio"));
            Assert.Equal(HorizontalAlignment.Right, pill.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, pill.VerticalAlignment);
            Assert.Equal(new Thickness(0, 2, 3, 0), pill.Margin);
            Assert.False(pill.IsHitTestVisible);
            Assert.Null(Pill("door.home"));
            Chip("door.studio").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("studio", shell.CurrentTab);
            PrizeOwnership.ApplySnapshot("rail-test", 2, System.Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.Null(Pill("door.studio"));
            PrizeOwnership.ApplySnapshot("rail-test", 3, new[] { PrizeGrants.JackpotRemix });
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(Pill("door.studio"));
            s.UnifiedId = "other-account";
            CoreAccount.UnifiedUserId = "other-account";
            Dispatcher.UIThread.RunJobs();
            Assert.Null(Pill("door.studio"));
        }
        finally { shell.Close(); PrizeOwnership.ResetHeld(); }
        return Task.CompletedTask;
    });

    // WPF MainWindow.FavoritesRail.cs:325-334 and MainWindow.xaml.cs:3064-3082.
    [Fact]
    public Task ModCoverUsesAuthoredRailCropAndFallbacks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var path = Path.Combine(TestUserDataProfile.Root, "rail-crop.png");
        using (var bitmap = ModArt.TryLoadBuiltIn("features/vibe.png")) bitmap!.Save(path);
        CoreModArt.OverridePathProvider = p => p == "features/vibe.png" ? path : null;
        var framing = new ModArtFraming { CenterX = .2, CenterY = .7, Zoom = 3 };
        var manifest = new ModManifest { ArtFraming = new Dictionary<string, Dictionary<string, ModArtFraming>>
        {
            ["FEATURES\\VIBE.PNG"] = new() { ["RAILCHIP"] = framing }
        } };
        CoreMods.ActiveModPackageProvider = () => new ModPackage(manifest, null, false);
        CoreSettings.Current.RailFavorites.Add("tab.haptics");
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            ImageBrush Face() => Assert.IsType<ImageBrush>(shell.SettingsPage!.FindControl<StackPanel>("FavoritesList")!
                .Children.OfType<Button>().Single().Background);
            var face = Face();
            using var bitmap = ModArt.TryLoad("features/vibe.png", FavoritesRailArt.BaseDecodeWidth)!;
            var expected = ModArtFramingRegistry.ToViewbox(framing, bitmap.Size.Width / bitmap.Size.Height,
                ModArtFramingRegistry.FindSurface(ModArtFramingRegistry.SurfaceRailChip)!.AspectRatio);
            Assert.Equal(new RelativeRect(expected.X, expected.Y, expected.Width, expected.Height, RelativeUnit.Relative), face.SourceRect);
            manifest.ArtFraming = null;
            shell.RefreshFavoritesRail();
            Assert.NotEqual(face.SourceRect, Face().SourceRect);
            CoreModArt.OverridePathProvider = _ => null;
            shell.RefreshFavoritesRail();
            var shipped = ModArtFramingRegistry.ShippedViewbox("features/vibe.png", ModArtFramingRegistry.SurfaceRailChip)!.Value;
            Assert.Equal(new RelativeRect(shipped.X, shipped.Y, shipped.Width, shipped.Height, RelativeUnit.Relative), Face().SourceRect);
        }
        finally { shell.Close(); File.Delete(path); }
        return Task.CompletedTask;
    });
}
