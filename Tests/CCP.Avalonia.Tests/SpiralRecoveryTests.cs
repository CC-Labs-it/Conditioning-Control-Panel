using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralRecoveryTests
{
    private static readonly byte[] Gif = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    // WPF OverlayService.cs:463,797-802 retries while enabled and absent.
    [Fact]
    public Task Failed_random_pool_recovers_on_a_later_tick_and_disable_stops_recovery()
        => WithOverlay(async (host, first, second) =>
        {
            File.WriteAllText(first, "broken");
            File.WriteAllText(second, "broken");
            SpiralOverlay.Refresh(host);
            await Settle();
            Assert.False(SpiralOverlay.IsShowing);
            Assert.True(SpiralOverlay.IsReconciling);
            SpiralOverlay.Reconcile(host);
            await Settle();
            Assert.False(SpiralOverlay.IsShowing);
            // Both original attempts failed. Repairing the pool must recover without a settings edit.
            File.WriteAllBytes(first, Gif);
            File.WriteAllBytes(second, Gif);
            SpiralOverlay.Reconcile(host);
            await Settle();
            Assert.True(SpiralOverlay.IsShowing);
            var window = SpiralOverlay.Shown[0];
            SpiralOverlay.Reconcile(host);
            Assert.Same(window, SpiralOverlay.Shown[0]);
            CoreSettings.Current.SpiralEnabled = false;
            SpiralOverlay.Reconcile(host);
            Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsReconciling);
        });

    // WPF OverlayService.cs:759-765 picks afresh when rebuilding for monitor changes.
    [Fact]
    public Task Monitor_rebuild_repicks_and_geometry_changes_replace_the_window()
        => WithOverlay(async (host, first, second) =>
        {
            SpiralOverlay.Refresh(host);
            await Settle();
            var picked = SpiralOverlay.SourcePath();
            var original = SpiralOverlay.Shown[0];
            CoreSettings.Current.DualMonitorEnabled = true;
            SpiralOverlay.Reconcile(host);
            await Settle();
            Assert.NotEqual(picked, SpiralOverlay.SourcePath());
            Assert.NotSame(original, SpiralOverlay.Shown[0]);
            var rebuilt = SpiralOverlay.Shown[0];
            var screen = MakeScreen(1024);
            ScreenList.QueryOverride = _ => new[] { screen };
            SpiralOverlay.Reconcile(host);
            await Settle();
            Assert.NotSame(rebuilt, SpiralOverlay.Shown[0]);
            var stable = SpiralOverlay.Shown[0];
            SpiralOverlay.Reconcile(host);
            Assert.Same(stable, SpiralOverlay.Shown[0]);
            SpiralOverlay.CloseAll();
            Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsReconciling);
        });

    private static Screen MakeScreen(int width)
    {
        // Same synthetic PlatformScreen seam as MultiMonitorResultTests; no native handles used.
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(42), "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, new PixelRect(0, 0, width, 600));
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, true);
        return screen;
    }

    private static async Task Settle()
    {
        // A broken pool is allowed at most two decodes per refresh, never recursive retry forever.
        for (var i = 0; i < 8 && SpiralOverlay.Decoding is { } pending; i++) await pending;
        Assert.Null(SpiralOverlay.Decoding);
    }

    private static async Task WithOverlay(Func<Window, string, string, Task> test)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-recovery-");
        try
        {
            var first = Path.Combine(dir.FullName, "first.gif");
            var second = Path.Combine(dir.FullName, "second.gif");
            File.WriteAllBytes(first, Gif);
            File.WriteAllBytes(second, Gif);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current == null)
                    ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                await Settle();
                SpiralOverlay.CloseAll();
                var s = CoreSettings.Current;
                var saved = (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize,
                    s.SpiralTargetMonitor, s.GlobalTargetMonitor, s.DualMonitorEnabled);
                var query = ScreenList.QueryOverride;
                var provider = CoreSession.IsEngineRunningProvider;
                var checks = SpiralOverlay.SkipPlatformChecksForTests;
                var host = new Window();
                host.Show();
                try
                {
                    ScreenList.QueryOverride = _ => new[] { MakeScreen(800) };
                    CoreSession.IsEngineRunningProvider = () => true;
                    SpiralOverlay.SkipPlatformChecksForTests = true;
                    (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize,
                        s.SpiralTargetMonitor, s.GlobalTargetMonitor, s.DualMonitorEnabled)
                        = (true, first, true, -1, -1, false);
                    await test(host, first, second);
                }
                finally
                {
                    await Settle();
                    SpiralOverlay.CloseAll();
                    ScreenList.QueryOverride = query;
                    CoreSession.IsEngineRunningProvider = provider;
                    SpiralOverlay.SkipPlatformChecksForTests = checks;
                    (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize,
                        s.SpiralTargetMonitor, s.GlobalTargetMonitor, s.DualMonitorEnabled) = saved;
                    host.Close();
                }
            });
        }
        finally { dir.Delete(true); }
    }
}
