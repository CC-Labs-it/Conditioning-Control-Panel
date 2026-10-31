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

    // WPF MainWindow.FavoritesRail.cs:92-93,256-265: a live grant repaints the Studio chip.
    [Fact]
    public Task OwnedPillRepaintsAndChipStillNavigates() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (get, put, granted) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted);
        var (offline, id, token, user) = (s.OfflineMode, s.UnifiedId, s.AuthToken, CoreAccount.UnifiedUserId);
        var secrets = new Dictionary<string, string?>();
        MainShellWindow? shell = null;
        try
        {
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            s.OfflineMode = false;
            s.UnifiedId = "rail-test";
            CoreAccount.UnifiedUserId = "rail-test";
            s.AuthToken = "test-token";
            PrizeOwnership.IsGranted = PrizeOwnership.Held;
            s.RailFavorites.AddRange(new[] { "door.studio", "door.home" });
            shell = new MainShellWindow();
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
        finally
        {
            try { shell?.Close(); }
            finally
            {
                PrizeOwnership.ResetHeld();
                PrizeOwnership.IsGranted = granted;
                // Restore the token through the temporary store before returning the real providers.
                (s.OfflineMode, s.UnifiedId, s.AuthToken) = (offline, id, token);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
                CoreAccount.UnifiedUserId = user;
            }
        }
        return Task.CompletedTask;
    });

    // WPF MainWindow.FavoritesRail.cs:325-334 and MainWindow.xaml.cs:3064-3082.
    [Fact]
    public Task ModCoverUsesAuthoredRailCropAndFallbacks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var (packageProvider, artProvider) = (CoreMods.ActiveModPackageProvider, CoreModArt.OverridePathProvider);
        var path = Path.Combine(TestUserDataProfile.Root, "rail-crop.png");
        MainShellWindow? shell = null;
        try
        {
            using (var fixture = ModArt.TryLoadBuiltIn("features/vibe.png")) fixture!.Save(path);
            CoreModArt.OverridePathProvider = p => p == "features/vibe.png" ? path : null;
            var framing = new ModArtFraming { CenterX = .2, CenterY = .7, Zoom = 3 };
            var manifest = new ModManifest { ArtFraming = new Dictionary<string, Dictionary<string, ModArtFraming>>
            {
                ["FEATURES\\VIBE.PNG"] = new() { ["RAILCHIP"] = framing }
            } };
            CoreMods.ActiveModPackageProvider = () => new ModPackage(manifest, null, false);
            CoreSettings.Current.RailFavorites.Add("tab.haptics");
            shell = new MainShellWindow();
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
        finally
        {
            try { shell?.Close(); }
            finally
            {
                (CoreMods.ActiveModPackageProvider, CoreModArt.OverridePathProvider) = (packageProvider, artProvider);
                File.Delete(path);
            }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TestsRestoreTheirSeamsBeforeTheIsolationHookRuns() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var snapshot = ProcessStateSnapshot.Take();
        try
        {
            var secrets = new Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            var s = CoreSettings.Current;
            (s.OfflineMode, s.UnifiedId, s.AuthToken) = (true, "prior-account", "prior-test-token");
            CoreAccount.UnifiedUserId = "prior-account";
            var (get, put, granted) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted);
            var (packageProvider, artProvider) = (CoreMods.ActiveModPackageProvider, CoreModArt.OverridePathProvider);

            // Direct calls finish before xUnit's After hook can hide a missing finally restoration.
            await OwnedPillRepaintsAndChipStillNavigates();
            Assert.Same(get, CoreSecrets.RetrieveProvider);
            Assert.Same(put, CoreSecrets.StoreProvider);
            Assert.Same(granted, PrizeOwnership.IsGranted);
            Assert.True(s.OfflineMode);
            Assert.Equal("prior-account", s.UnifiedId);
            Assert.Equal("prior-test-token", s.AuthToken);
            Assert.Equal("prior-account", CoreAccount.UnifiedUserId);

            await ModCoverUsesAuthoredRailCropAndFallbacks();
            Assert.Same(packageProvider, CoreMods.ActiveModPackageProvider);
            Assert.Same(artProvider, CoreModArt.OverridePathProvider);
        }
        finally { snapshot.Restore(); }
    });
}
