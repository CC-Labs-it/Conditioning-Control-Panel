using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PlayArtFramingTests
{
    [Fact]
    public void ModSwitchFramesEachPlaySurfaceAndRestoresDefaultArt()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var dir = Directory.CreateTempSubdirectory("ccp-play-framing-");
            var png = Path.Combine(dir.FullName, "art.png");
            using (var image = new WriteableBitmap(new PixelSize(200, 100), new Vector(96, 96))) image.Save(png);
            var paths = new[] { "features/lab_gaze_hero.png", "features/remote_control.png", "features/loom.png" };
            var surfaces = new[] { "playCard", "playCardTall", "loomStrip" };
            var names = new[] { "PlayGazeHeroPlate", "PlayRemoteHeroPlate", "PlayLoomHeroPlate" };
            var map = new Dictionary<string, Dictionary<string, ModArtFraming>>();
            for (int i = 0; i < paths.Length; i++)
                map[paths[i].Replace('/', '\\').ToUpperInvariant()] = new Dictionary<string, ModArtFraming>
                { [surfaces[i].ToUpperInvariant()] = new ModArtFraming { CenterX = 0.3, CenterY = 0.4, Zoom = 2 } };
            var mod = new ModPackage(new ModManifest { Id = "framed", ArtFraming = map }, null, false);
            var stock = new ModPackage(new ModManifest { Id = "stock" }, null, true);
            var active = mod;
            bool brokenImage = false;
            var previousModProvider = CoreMods.ActiveModPackageProvider;
            var previousArtProvider = CoreModArt.OverridePathProvider;
            void Exercise(bool failBeforeView)
            {
                Window? window = null;
                try
                {
                    CoreMods.ActiveModPackageProvider = () => active;
                    CoreModArt.OverridePathProvider = path => active == mod && Array.IndexOf(paths, path) >= 0 ? (brokenImage ? Path.Combine(dir.FullName, "missing.png") : png) : null;
                    if (failBeforeView) throw new InvalidOperationException("Simulated view construction failure");
                    var play = new PlayTabView();
                    window = new Window { Content = play };
                    window.Show();
                    Dispatcher.UIThread.RunJobs();
                    RelativeRect Crop(int i) => ((ImageBrush)play.FindControl<Border>(names[i])!.Background!).SourceRect;
                    void Check(int i, double x, double y, double width, double height)
                    {
                        var rect = Crop(i);
                        Assert.Equal(RelativeUnit.Relative, rect.Unit);
                        Assert.Equal(x, rect.Rect.X, 5);
                        Assert.Equal(y, rect.Rect.Y, 5);
                        Assert.Equal(width, rect.Rect.Width, 5);
                        Assert.Equal(height, rect.Rect.Height, 5);
                    }
                    // WPF ResolveViewbox for a 2:1 image, each surface and the author's focal point.
                    Check(0, 0.05, 0.2248731, 0.5, 0.3502538);
                    Check(1, 0.05, 0.1868020, 0.5, 0.4263959);
                    Check(2, 0.0711864, 0.15, 0.4576271, 0.5);

                    active = stock;
                    CoreMods.RaiseModChanged(null, stock);
                    Dispatcher.UIThread.RunJobs();
                    for (int i = 0; i < names.Length; i++) Check(i, 0, 0, 1, 1);

                    active = mod;
                    CoreMods.RaiseModChanged(null, mod);
                    Dispatcher.UIThread.RunJobs();
                    Check(0, 0.05, 0.2248731, 0.5, 0.3502538);

                    // An override that cannot be decoded falls back to the shipped image and crop.
                    brokenImage = true;
                    CoreMods.RaiseModChanged(null, mod);
                    Dispatcher.UIThread.RunJobs();
                    for (int i = 0; i < names.Length; i++) Check(i, 0, 0, 1, 1);

                    // A switch while detached is observed when the view returns.
                    window.Content = null;
                    brokenImage = false;
                    CoreMods.RaiseModChanged(null, mod);
                    window.Content = play;
                    Dispatcher.UIThread.RunJobs();
                    Check(1, 0.05, 0.1868020, 0.5, 0.4263959);
                }
                finally
                {
                    CoreMods.ActiveModPackageProvider = previousModProvider;
                    CoreModArt.OverridePathProvider = previousArtProvider;
                    window?.Close();
                }
            }

            try
            {
                Assert.Throws<InvalidOperationException>(() => Exercise(true));
                Assert.Multiple(
                    () => Assert.Same(previousModProvider, CoreMods.ActiveModPackageProvider),
                    () => Assert.Same(previousArtProvider, CoreModArt.OverridePathProvider));
                Exercise(false);
                Assert.Same(previousModProvider, CoreMods.ActiveModPackageProvider);
                Assert.Same(previousArtProvider, CoreModArt.OverridePathProvider);
            }
            finally { dir.Delete(true); }
        });
    }
}
