using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NavRailModTwinTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static byte[] Digest(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return SHA256.HashData(stream.ToArray());
    }

    private static byte[] Shipped(string path, int width = 128)
    {
        using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + path));
        using var bitmap = Bitmap.DecodeToWidth(stream, width);
        return Digest(bitmap);
    }

    [Fact]
    public void ResolverUsesPackThenSafeExistingTwinThenDefault() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-nav-twin-");
        var overridePath = Path.Combine(dir.FullName, "override.png");
        using (var bitmap = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96))) bitmap.Save(overridePath);
        var oldId = CoreMods.ActiveModIdProvider;
        var oldOverride = CoreModArt.OverridePathProvider;
        try
        {
            string id = "drone-mode";
            CoreMods.ActiveModIdProvider = () => id;
            CoreModArt.OverridePathProvider = _ => null;
            foreach (var mod in new[] { "drone-mode", "builtin-locked", "infection-control" })
            {
                id = mod;
                using var actual = ModArt.TryLoad("nav/door_social.png", 128)!;
                Assert.Equal(Shipped("nav/mods/" + mod + "/door_social.png"), Digest(actual));
            }
            id = "drone-mode";
            CoreModArt.OverridePathProvider = _ => overridePath;
            using (var actual = ModArt.TryLoad("nav/door_social.png", 128)!)
                Assert.Equal(new PixelSize(128, 64), actual.PixelSize);
            CoreModArt.OverridePathProvider = _ => null;
            foreach (var mod in new[] { "no-twin", "../drone-mode", "../mods/drone-mode", "drone-mode/child", "drone-mode\\child" })
            {
                id = mod;
                using var actual = ModArt.TryLoad("nav/door_social.png", 128)!;
                Assert.Equal(Shipped("nav/door_social.png"), Digest(actual));
            }
            id = "drone-mode";
            Assert.Null(ModArt.TryLoad("nav/../nav/door_social.png", 128));
        }
        finally
        {
            CoreMods.ActiveModIdProvider = oldId;
            CoreModArt.OverridePathProvider = oldOverride;
            dir.Delete(true);
        }
    });

    [Fact]
    public void FavoriteSocialChipFollowsModChangesAtBothDecodeWidths() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var favoritesBefore = settings.RailFavorites.ToArray();
        var recentBefore = settings.RailRecent.ToArray();
        var oldId = CoreMods.ActiveModIdProvider;
        var oldOverride = CoreModArt.OverridePathProvider;
        var oldMotion = settings.MotionLevel;
        MainShellWindow? window = null;
        try
        {
            string id = "drone-mode";
            CoreMods.ActiveModIdProvider = () => id;
            CoreModArt.OverridePathProvider = _ => null;
            settings.MotionLevel = MotionLevel.Off;
            settings.RailFavorites.Clear();
            settings.RailRecent.Clear();
            window = new MainShellWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Pin the real Social rail row, as in WPF MainWindow.FavoritesRail.cs:484.
            var door = window.Named<Button>("DoorSocial")!;
            var menu = door.ContextMenu!;
            door.RaiseEvent(new ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<MenuItem>(Assert.Single(menu.Items)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.Close();
            Dispatcher.UIThread.RunJobs();
            var favorites = window.SettingsPage!.FindControl<StackPanel>("FavoritesList")!;

            void AssertChip(string path)
            {
                var chip = favorites.Children.OfType<Button>().Single(b => Equals(b.Tag, "door.social"));
                var plate = ((Grid)chip.Content!).Children.OfType<Border>()
                    .Single(b => b.Background is ImageBrush);
                var icon = (Bitmap)Assert.IsType<ImageBrush>(plate.Background).Source!;
                var backdrop = (Bitmap)Assert.IsType<ImageBrush>(chip.Background).Source!;
                Assert.Equal(Shipped(path, 128), Digest(icon));
                Assert.Equal(Shipped(path, 12), Digest(backdrop));
            }

            AssertChip("nav/mods/drone-mode/door_social.png");
            foreach (var (mod, path) in new[]
            {
                ("infection-control", "nav/mods/infection-control/door_social.png"),
                ("no-twin", "nav/door_social.png"),
                ("drone-mode", "nav/mods/drone-mode/door_social.png"),
            })
            {
                id = mod;
                CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
                Dispatcher.UIThread.RunJobs();
                window.ShowTab("studio");
                window.ShowTab("settings"); // WPF MainWindow.TabNavigation.cs:389 rebuilds the dashboard rail.
                Dispatcher.UIThread.RunJobs();
                AssertChip(path);
            }
        }
        finally
        {
            window?.Close();
            CoreMods.ActiveModIdProvider = oldId;
            CoreModArt.OverridePathProvider = oldOverride;
            settings.MotionLevel = oldMotion;
            settings.RailFavorites.Clear(); settings.RailFavorites.AddRange(favoritesBefore);
            settings.RailRecent.Clear(); settings.RailRecent.AddRange(recentBefore);
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void RailRepaintsSocialMedallionWhenModChanges() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldId = CoreMods.ActiveModIdProvider;
        var oldOverride = CoreModArt.OverridePathProvider;
        var oldMotion = CoreSettings.Current.MotionLevel;
        MainShellWindow? window = null;
        try
        {
            string id = "drone-mode";
            CoreMods.ActiveModIdProvider = () => id;
            CoreModArt.OverridePathProvider = _ => null;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            window = new MainShellWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var image = window.Named<Image>("ImgDoorSocial")!;
            Assert.Equal(Shipped("nav/mods/drone-mode/door_social.png"), Digest((Bitmap)image.Source!));
            Assert.False(window.Named<Border>("ShadeDoorSocial")!.IsVisible);
            id = "infection-control";
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Shipped("nav/mods/infection-control/door_social.png"), Digest((Bitmap)image.Source!));
            id = "no-twin";
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Shipped("nav/door_social.png"), Digest((Bitmap)image.Source!));
        }
        finally
        {
            window?.Close();
            CoreMods.ActiveModIdProvider = oldId;
            CoreModArt.OverridePathProvider = oldOverride;
            CoreSettings.Current.MotionLevel = oldMotion;
            Dispatcher.UIThread.RunJobs();
        }
    });
}
