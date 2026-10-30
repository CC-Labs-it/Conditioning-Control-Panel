using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkingSetArtTests(ITestOutputHelper output)
{
    // WPF MainWindow.xaml.cs playHeroMap; Chess uses the same 512px card budget as Goon.
    private static readonly (string Name, int Width)[] PlayPlates =
    {
        ("PlayGoonHeroPlate", 512), ("PlayChessHeroPlate", 512),
        ("PlayRemoteHeroPlate", 768), ("PlayGazeHeroPlate", 512),
        ("PlayFocusHeroPlate", 512), ("PlayBlinkHeroPlate", 512),
        ("PlayIntakeHeroPlate", 512), ("PlayFypHeroPlate", 512),
        ("PlayLockdownHeroPlate", 1024), ("PlayLoomHeroPlate", 512),
    };

    [Fact]
    public Task WarmedTabsKeepDisplaySizedArtAndReuseItOnReturn() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            VisitTabs(shell);
            var rack = shell.StudioRack!;
            var play = shell.Named<PlayTabView>("PlayTab")!;
            var rackArt = RackArt(rack);
            var playArt = PlayPlates.Select(p => Art(play.FindControl<Border>(p.Name)!)).ToArray();
            Report("warm-up", shell, rackArt, playArt);

            VisitTabs(shell);
            var returnedRackArt = RackArt(rack);
            Assert.Equal(rackArt.Length, returnedRackArt.Length);
            for (var i = 0; i < rackArt.Length; i++) Assert.Same(rackArt[i], returnedRackArt[i]);
            for (var i = 0; i < PlayPlates.Length; i++)
                Assert.Same(playArt[i], Art(play.FindControl<Border>(PlayPlates[i].Name)!));
            Report("return", shell, returnedRackArt, playArt);

            // Each of the 12 rows shares ONE decode between its plate and resting chip.
            Assert.Equal(24, rackArt.Length);
            Assert.Equal(12, rackArt.Distinct().Count());
            Assert.All(rackArt, bitmap => Assert.Equal(256, bitmap.PixelSize.Width));
            for (var i = 0; i < PlayPlates.Length; i++)
                Assert.Equal((PlayPlates[i].Name, PlayPlates[i].Width),
                    (PlayPlates[i].Name, playArt[i].PixelSize.Width));

            // The mod-aware repaint must preserve the same budget, not restore full-size images.
            rack.RepaintModAwareChrome();
            Assert.All(RackArt(rack), bitmap => Assert.Equal(256, bitmap.PixelSize.Width));

            // WPF StudioTabView.ApplyDoorIcon decodes the door medallion at 128px.
            var door = rack.FindControl<Image>("ImgStudioDoorArt")!;
            Assert.Equal(128, Assert.IsType<Bitmap>(door.Source).PixelSize.Width);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    private static void VisitTabs(MainShellWindow shell)
    {
        foreach (var tab in NavCheck.TabPanels)
        {
            Assert.True(PerfBench.Show(shell, tab), tab);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Bitmap Art(Border plate) => Assert.IsType<Bitmap>(Assert.IsType<ImageBrush>(plate.Background).Source);

    private static Bitmap[] RackArt(StudioTabView rack) => rack.FindControl<StackPanel>("RackList")!
        .GetVisualDescendants().OfType<Border>().Select(b => (b.Background as ImageBrush)?.Source)
        .OfType<Bitmap>().ToArray();

    private void Report(string phase, MainShellWindow shell, Bitmap[] rack, Bitmap[] play)
    {
        // Pixel bytes account for native Skia allocations that GC.GetTotalMemory cannot see.
        // Count shared sources once. This inventory excludes GPU surfaces and nonvisual caches.
        var images = shell.GetVisualDescendants().SelectMany(v => v switch
        {
            Image image when image.Source is Bitmap bitmap => new[] { bitmap },
            Border border when border.Background is ImageBrush { Source: Bitmap bitmap } => new[] { bitmap },
            _ => Array.Empty<Bitmap>(),
        });
        for (var i = 0; i < 2; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        output.WriteLine($"{phase}: rack={PixelMiB(rack):F3} MiB; play={PixelMiB(play):F3} MiB; " +
            $"visual bitmaps={PixelMiB(images):F3} MiB; managed={GC.GetTotalMemory(false) / 1048576.0:F3} MiB; " +
            $"headless working set={process.WorkingSet64 / 1048576.0:F3} MiB");
        GC.KeepAlive(shell);
    }

    private static double PixelMiB(IEnumerable<Bitmap> images) => images.Distinct()
        .Sum(b => (long)b.PixelSize.Width * b.PixelSize.Height * 4) / 1048576.0;
}
