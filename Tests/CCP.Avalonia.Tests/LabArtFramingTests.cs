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
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LabArtFramingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void HeadersUseModFramingAndRestoreStockOnSwitch(int index)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-lab-framing-");
            var png = Path.Combine(dir.FullName, "art.png");
            using (var image = new WriteableBitmap(new PixelSize(200, 100), new Vector(96, 96))) image.Save(png);
            var paths = new[] { "features/lab_quiz_hero.png", "features/lab_aimemory_hero.png" };
            var surfaces = new[] { "intakeStrip", "pageHeader" };
            var map = new Dictionary<string, Dictionary<string, ModArtFraming>>();
            for (int i = 0; i < paths.Length; i++)
                map[paths[i].Replace('/', '\\').ToUpperInvariant()] = new Dictionary<string, ModArtFraming>
                { [surfaces[i].ToUpperInvariant()] = new ModArtFraming { CenterX = 0.3, CenterY = 0.4, Zoom = 2 } };
            var mod = new ModPackage(new ModManifest { Id = "framed", ArtFraming = map }, null, false);
            var stock = new ModPackage(new ModManifest { Id = "stock" }, null, true);
            var active = mod;
            var previousMod = CoreMods.ActiveModPackageProvider;
            var previousArt = CoreModArt.OverridePathProvider;
            Window? window = null;
            try
            {
                CoreMods.ActiveModPackageProvider = () => active;
                CoreModArt.OverridePathProvider = path => active == mod && Array.IndexOf(paths, path) >= 0 ? png : null;
                var intake = new GradedIntakeTabView();
                var permissions = new AiPermissionsGrid();
                window = new Window { Content = new StackPanel { Children = { intake, permissions } } };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var plates = new[] { intake.FindControl<Border>("GradedIntakeHeroArtHost")!, permissions.FindControl<Border>("LabAiMemoryHeroArt")! };
                for (int i = index; i <= index; i++)
                {
                    var expected = ModArtFramingRegistry.ResolveViewbox(paths[i], surfaces[i], true, 2, map[paths[i].Replace('/', '\\').ToUpperInvariant()][surfaces[i].ToUpperInvariant()]);
                    var actual = ((ImageBrush)plates[i].Background!).SourceRect;
                    Assert.Equal(RelativeUnit.Relative, actual.Unit);
                    Assert.Equal(expected.X, actual.Rect.X, 5);
                    Assert.Equal(expected.Y, actual.Rect.Y, 5);
                    Assert.Equal(expected.Width, actual.Rect.Width, 5);
                    Assert.Equal(expected.Height, actual.Rect.Height, 5);
                }
                active = stock;
                CoreMods.RaiseModChanged(null, stock);
                Dispatcher.UIThread.RunJobs();
                foreach (var plate in plates)
                    Assert.Equal(new RelativeRect(0, 0, 1, 1, RelativeUnit.Relative), ((ImageBrush)plate.Background!).SourceRect);
            }
            finally
            {
                window?.Close();
                CoreMods.ActiveModPackageProvider = previousMod;
                CoreModArt.OverridePathProvider = previousArt;
                dir.Delete(true);
            }
        });
    }
}
