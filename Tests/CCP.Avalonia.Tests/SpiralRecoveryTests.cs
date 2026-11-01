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

    // WPF OverlayService.cs:416,759-765,797-802 reads the current policy on each restart.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task Restart_reads_changed_randomize_policy(bool initiallyRandom, bool retry)
        => WithOverlay(async (host, first, second) =>
        {
            // BMP is valid configured media but outside SpiralPool's random image extensions.
            // One eligible GIF makes both policy transitions deterministic, with no random seam.
            File.Delete(second);
            var configured = Path.Combine(Path.GetDirectoryName(first)!, "fixed.bmp");
            var bmp = Convert.FromHexString("424D3A0000000000000036000000280000000100000001000000010018000000000004000000000000000000000000000000000000000000FF00");
            File.WriteAllBytes(configured, bmp);
            var s = CoreSettings.Current;
            s.SpiralPath = configured;
            s.SpiralRandomize = initiallyRandom;
            if (retry)
            {
                File.WriteAllText(configured, "broken");
                File.WriteAllText(first, "broken");
            }
            SpiralOverlay.Refresh(host);
            await Settle();
            Assert.Equal(!retry, SpiralOverlay.IsShowing);
            Assert.Equal(initiallyRandom ? first : configured, SpiralOverlay.SourcePath());

            s.SpiralRandomize = !initiallyRandom;
            if (retry)
            {
                File.WriteAllBytes(configured, bmp);
                File.WriteAllBytes(first, Gif);
            }
            else
            {
                // A policy edit alone still waits for restart, as the feature card promises.
                var window = SpiralOverlay.Shown[0];
                SpiralOverlay.Refresh(host);
                Assert.Same(window, SpiralOverlay.Shown[0]);
                Assert.Equal(initiallyRandom ? first : configured, SpiralOverlay.SourcePath());
                s.DualMonitorEnabled = !s.DualMonitorEnabled;
            }
            SpiralOverlay.Reconcile(host);
            await Settle();
            Assert.True(SpiralOverlay.IsShowing);
            Assert.Equal(initiallyRandom ? configured : first, SpiralOverlay.SourcePath());
        });

    [Fact]
    public Task Recorded_platform_refusal_stops_reconciliation_before_decode()
        => WithOverlay(async (host, first, second) =>
        {
            // Model an earlier native click-through refusal, with platform checks left on.
            var refused = typeof(SpiralOverlay).GetField("_refused",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var saved = refused.GetValue(null);
            try
            {
                SpiralOverlay.Refresh(host);
                await Settle();
                Assert.True(SpiralOverlay.IsReconciling);
                refused.SetValue(null, true);
                SpiralOverlay.SkipPlatformChecksForTests = false;
                for (var i = 0; i < 3; i++)
                {
                    SpiralOverlay.Reconcile(host);
                    Assert.False(SpiralOverlay.IsReconciling || SpiralOverlay.IsShowing);
                    Assert.Null(SpiralOverlay.Decoding);
                }
            }
            finally { refused.SetValue(null, saved); }
        });

    // WPF OverlayService.cs:1562,1590-1594: an unchanged failed path is not decoded again.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Unchanged_failed_images_are_skipped_and_edited_files_recover(bool randomize)
        => WithOverlay(async (host, first, second) =>
        {
            CoreSettings.Current.SpiralRandomize = randomize;
            File.WriteAllText(first, "broken");
            File.WriteAllText(second, "broken");
            SpiralOverlay.Refresh(host);
            await Settle();
            for (var i = 0; i < 4; i++)
            {
                SpiralOverlay.Reconcile(host);
                Assert.Null(SpiralOverlay.Decoding);
                Assert.False(SpiralOverlay.IsShowing);
            }
            File.WriteAllBytes(first, Gif);
            File.WriteAllBytes(second, Gif);
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddSeconds(5));
            File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddSeconds(5));
            SpiralOverlay.Reconcile(host);
            await Settle();
            Assert.True(SpiralOverlay.IsShowing);
        });

    [Fact]
    public Task Failed_video_start_is_skipped_until_its_file_changes()
        => WithOverlay((host, first, second) =>
        {
            var start = SpiralOverlay.VideoStart;
            var calls = 0;
            var video = Path.ChangeExtension(first, ".mp4");
            File.WriteAllText(video, "video");
            try
            {
                CoreSettings.Current.SpiralRandomize = false;
                CoreSettings.Current.SpiralPath = video;
                SpiralOverlay.VideoStart = (_, _) => { calls++; return null; };
                SpiralOverlay.Refresh(host);
                Assert.Equal(1, calls);
                for (var i = 0; i < 4; i++) SpiralOverlay.Reconcile(host);
                Assert.Equal(1, calls);
                Assert.False(SpiralOverlay.IsShowing);
                File.SetLastWriteTimeUtc(video, DateTime.UtcNow.AddSeconds(5));
                SpiralOverlay.Reconcile(host);
                Assert.Equal(2, calls);
                return Task.CompletedTask;
            }
            finally { SpiralOverlay.VideoStart = start; }
        });

    // WPF OverlayService.cs:1562,1633,1731: later starts can retry the same unchanged source.
    [Fact]
    public Task Stop_start_retries_an_unchanged_failed_image()
        => WithOverlay(async (host, first, second) =>
        {
            CoreSettings.Current.SpiralRandomize = false;
            File.WriteAllText(first, "broken");
            var stamp = File.GetLastWriteTimeUtc(first);
            SpiralOverlay.Refresh(host);
            await Settle();
            SpiralOverlay.Reconcile(host);
            Assert.Null(SpiralOverlay.Decoding);
            SpiralOverlay.CloseAll();
            SpiralOverlay.Refresh(host);
            Assert.NotNull(SpiralOverlay.Decoding);
            await Settle();
            Assert.False(SpiralOverlay.IsShowing);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(first));
        });

    [Fact]
    public Task Stop_start_recovers_from_transient_video_failure_without_editing_the_file()
        => WithOverlay((host, first, second) =>
        {
            var start = SpiralOverlay.VideoStart;
            var calls = 0;
            var video = Path.ChangeExtension(first, ".mp4");
            File.WriteAllText(video, "video");
            var stamp = File.GetLastWriteTimeUtc(video);
            try
            {
                CoreSettings.Current.SpiralRandomize = false;
                CoreSettings.Current.SpiralPath = video;
                SpiralOverlay.VideoStart = (_, _) => ++calls == 1 ? null : new MemoryStream();
                SpiralOverlay.Refresh(host);
                SpiralOverlay.Reconcile(host);
                Assert.Equal(1, calls);
                Assert.False(SpiralOverlay.IsShowing);
                SpiralOverlay.CloseAll();
                SpiralOverlay.Refresh(host);
                Assert.Equal(2, calls);
                Assert.True(SpiralOverlay.IsShowing && SpiralOverlay.IsVideoPlaying);
                Assert.Equal(stamp, File.GetLastWriteTimeUtc(video));
                return Task.CompletedTask;
            }
            finally { SpiralOverlay.VideoStart = start; }
        });

    [Fact]
    public Task A_decode_finishing_after_stop_cannot_restart_or_poison_the_next_run()
        => WithOverlay(async (host, first, second) =>
        {
            CoreSettings.Current.SpiralRandomize = false;
            File.WriteAllText(first, "broken");
            SpiralOverlay.Refresh(host);
            var pending = SpiralOverlay.Decoding;
            Assert.NotNull(pending);
            SpiralOverlay.CloseAll();
            await pending;
            Assert.False(SpiralOverlay.IsReconciling || SpiralOverlay.IsShowing);
            SpiralOverlay.Refresh(host);
            Assert.NotNull(SpiralOverlay.Decoding);
            await Settle();
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
