using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using ConditioningControlPanel.Avalonia.Views.Lab.GazeMinigame;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF InlineLoopVideo on this head: a real 1 s clip plays headless, loops past its end,
/// pauses when hidden, and detaching frees the player (no leak); the HelpPopover clip poster plays
/// while open and releases on close. Skips where ffmpeg or libvlc is missing.</summary>
public sealed class InlineLoopVideoTests
{
    [Fact]
    public async Task PlaysLoopsPausesWhenHiddenAndFreesOnUnload()
    {
        await WithClip(async clip =>
        {
            var baseline = InlineLoopVideo.LivePlayers;
            var video = new InlineLoopVideo(clip) { Width = 320, Height = 180 };
            var host = new Window { Width = 400, Height = 300, Content = video };
            host.Show();
            Assert.True(await OnFrames(video, () => video.FrameCount > 0, 5000), "no first frame");
            Assert.IsType<global::Avalonia.Media.Imaging.WriteableBitmap>(video.Source);
            Assert.Equal(baseline + 1, InlineLoopVideo.LivePlayers);

            // Loop: the clip is 1 s long; playback time wraps back to the start and frames keep coming.
            long last = 0, peak = 0;
            Assert.True(await OnFrames(video, () =>
            {
                var t = video.Time;
                var wrapped = peak > 500 && t >= 0 && t < last;
                last = t; peak = Math.Max(peak, t);
                return wrapped;
            }, 4000), "stopped at the end instead of looping");
            var f = video.FrameCount;
            Assert.True(await OnFrames(video, () => video.FrameCount > f + 5, 2000), "no frames after the loop");

            // Hidden pauses decoding (time and frames frozen across dispatcher turns); shown again resumes it.
            video.IsVisible = false;
            Assert.True(await OnFrames(video, () => !video.IsPlaying, 2000), "still playing while hidden");
            var frozen = video.Time;
            f = video.FrameCount;
            for (var i = 0; i < 5; i++) { await Task.Yield(); Dispatcher.UIThread.RunJobs(); }
            Assert.False(video.IsPlaying, "still playing while hidden");
            Assert.Equal(frozen, video.Time);
            Assert.Equal(f, video.FrameCount);
            video.IsVisible = true;
            Assert.True(await OnFrames(video, () => video.FrameCount > f + 3, 3000), "did not resume when shown");

            // Unload releases the player, the frame buffer and the bitmap.
            host.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(video.HasPlayer);
            Assert.Null(video.Source);
            Assert.Equal(baseline, InlineLoopVideo.LivePlayers);

            // Reload starts again (the popup re-open path).
            host.Content = video;
            Assert.True(await OnFrames(video, () => video.FrameCount > 0, 5000), "no frame after reload");
            host.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(video.HasPlayer);
            Assert.Equal(baseline, InlineLoopVideo.LivePlayers);
        });
    }

    [Fact]
    public async Task HelpPopoverClipTopicPlaysWhileOpenAndReleasesOnClose()
    {
        await WithClip(async clip =>
        {
            var baseline = InlineLoopVideo.LivePlayers;
            // An absolute ClipFile survives Path.Combine(BaseDirectory, "Resources", ...), so the
            // popover's own path resolution is used unchanged. No drawn loop for this section id.
            var content = new HelpContent { SectionId = "InlineLoopVideoTest", Title = "t", WhatItDoes = "w", ClipFile = clip };
            var button = new Button { Content = "?", Width = 44, Height = 44 };
            var host = new Window { Width = 600, Height = 500, Content = button };
            HelpPopover.Attach(button, content);
            host.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                Click(host, button);
                Assert.True(HelpPopover.IsOpen(button));
                var video = HelpPopover.PopupContent(button)!.GetVisualDescendants().OfType<InlineLoopVideo>().Single();
                Assert.True(await OnFrames(video, () => video.FrameCount > 0, 5000), "poster never drew a frame");

                Click(host, button); // unpin -> close
                Dispatcher.UIThread.RunJobs();
                Assert.False(HelpPopover.IsOpen(button));
                Assert.False(video.HasPlayer);
                Assert.Equal(baseline, InlineLoopVideo.LivePlayers);
            }
            finally
            {
                HelpPopover.Clear(button);
                host.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    /// <summary>The Gaze pane mode (WPF StartVideo): no loop, so the clip ends and holds its last
    /// frame; the start offset skips most of it (a 1 s clip at 25 fps from a random point in the first half shows at most 25 frames).</summary>
    [Fact]
    public async Task PlayOnceFromAnOffsetEndsEarlyAndKeepsTheLastFrame()
    {
        await WithClip(async clip =>
        {
            var video = new InlineLoopVideo(clip, randomStart: true, loop: false) { Width = 320, Height = 180 };
            var host = new Window { Width = 400, Height = 300, Content = video };
            host.Show();
            try
            {
                Assert.True(await OnFrames(video, () => video.FrameCount > 0, 5000), "no first frame");
                var sw = Stopwatch.StartNew();
                while (video.IsPlaying && sw.ElapsedMilliseconds < 5000) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
                Assert.False(video.IsPlaying, "kept playing: looped instead of ending");
                Assert.InRange(video.FrameCount, 1, 25);
                Assert.NotNull(video.Source);
            }
            finally { host.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Fact]
    public async Task GazeRoundStopsOverlapWithoutBlockingDispatcherAndCloseStopsPlayer()
    {
        await WithClip(async clip =>
        {
            var baseline = InlineLoopVideo.LivePlayers;
            var win = new GazeMinigameWindow();
            win.Show();
            var left = win.FindControl<Grid>("LeftPane")!;
            var right = win.FindControl<Grid>("RightPane")!;
            var a = GazeMinigameWindow.CreateRoundVideo(clip);
            var b = GazeMinigameWindow.CreateRoundVideo(clip);
            left.Children.Add(a); right.Children.Add(b);
            Assert.True(await OnFrames(a, () => a.FrameCount > 0, 5000));
            Assert.True(await OnFrames(b, () => b.FrameCount > 0, 5000));
            var sinkA = a.SinkForTest!;
            var sinkB = b.SinkForTest!;
            var oldStop = InlineLoopVideo.StopPlayer;
            using var release = new ManualResetEventSlim();
            using var entered = new CountdownEvent(2);
            try
            {
                InlineLoopVideo.StopPlayer = player => { entered.Signal(); release.Wait(); oldStop(player); };
                win.DisposeCurrentRoundPlayers();
                // A dispatcher turn runs while both native stops are held open.
                var turn = false;
                Dispatcher.UIThread.Post(() => turn = true);
                Dispatcher.UIThread.RunJobs();
                Assert.True(turn);
                Assert.True(await Task.Run(() => entered.Wait(3000)), "stops did not overlap");
                await Task.Delay(550); // Even past WPF's 500 ms limit, a running Stop owns its sink.
                Assert.Equal(baseline + 2, InlineLoopVideo.LivePlayers);
                Assert.False(sinkA.Freed);
                Assert.False(sinkB.Freed);
                release.Set();
                var deadline = Stopwatch.StartNew();
                while ((InlineLoopVideo.LivePlayers != baseline || !sinkA.Freed || !sinkB.Freed) && deadline.ElapsedMilliseconds < 5000)
                { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                Assert.Equal(baseline, InlineLoopVideo.LivePlayers);
                Assert.True(sinkA.Freed && sinkB.Freed);

                InlineLoopVideo.StopPlayer = oldStop;
                var closing = GazeMinigameWindow.CreateRoundVideo(clip);
                left.Children.Add(closing);
                Assert.True(await OnFrames(closing, () => closing.FrameCount > 0, 5000));
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                InlineLoopVideo.StopPlayer = player => { oldStop(player); stopped.TrySetResult(); };
                win.Close();
                await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
                deadline.Restart();
                while (InlineLoopVideo.LivePlayers != baseline && deadline.ElapsedMilliseconds < 5000)
                { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                Assert.Equal(baseline, InlineLoopVideo.LivePlayers);
            }
            finally
            {
                release.Set();
                InlineLoopVideo.StopPlayer = oldStop;
                win.Close();
            }
        });
    }

    [Fact]
    public async Task ShutdownStopsSafetyBeforeBoundedWaitForStuckCleanup()
    {
        var pending = new TaskCompletionSource();
        var safety = false;
        var exited = false;
        var exit = global::ConditioningControlPanel.Avalonia.App.ShutdownAfterGazeCleanupAsync(
            () => safety = true, () => { Assert.True(safety); return pending.Task; },
            () => exited = true, TimeSpan.FromMilliseconds(50));
        await exit.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(exited);
        Assert.False(pending.Task.IsCompleted);
        pending.SetResult();
    }

    [Fact]
    public async Task FinalHapticStopStillRunsAfterOutputDuringGazeWait()
    {
        var settings = new HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        settings.V2.Provider("lovense").Enabled = false;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        using var haptics = new ConditioningControlPanel.Services.HapticService(settings);
        var oldService = ConditioningControlPanel.CoreHaptics.Service;
        var oldToast = ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast;
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
        ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast = messages.Enqueue;
        ConditioningControlPanel.CoreHaptics.Service = haptics;
        try
        {
            Assert.True(await haptics.ConnectAsync());
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var exit = global::ConditioningControlPanel.Avalonia.App.ShutdownAfterGazeCleanupAsync(
                global::ConditioningControlPanel.Avalonia.App.StopHapticsBeforeGazeWait,
                () => pending.Task,
                global::ConditioningControlPanel.Avalonia.App.StopHapticsForExit, TimeSpan.FromSeconds(3));
            await Task.Delay(800); // The early panic's retries have finished.
            messages.Clear();
            var provider = haptics.DeviceManager.Providers.Single(p => p.Key == "mock");
            await provider.SetOutputsAsync("mock-lush", new[] {
                new ConditioningControlPanel.Services.Haptics.Core.ActuatorOutput(
                    ConditioningControlPanel.Services.Haptics.Core.ActuatorType.Vibrate, 0, 1)
            }, CancellationToken.None);
            Assert.Contains(messages, m => m.Contains("100%"));
            pending.SetResult();
            await exit.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Contains(messages, m => m.Contains("ALL STOP"));
        }
        finally
        {
            ConditioningControlPanel.CoreHaptics.Service = oldService;
            ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast = oldToast;
        }
    }

    [Fact]
    public async Task GazeCloseAndShutdownAreBoundedWithPendingRoundCleanup()
    {
        await WithClip(async clip =>
        {
            var baseline = InlineLoopVideo.LivePlayers;
            var win = new GazeMinigameWindow();
            var closed = false;
            win.Closed += (_, _) => closed = true;
            win.Show();
            var left = win.FindControl<Grid>("LeftPane")!;
            var right = win.FindControl<Grid>("RightPane")!;
            var a = GazeMinigameWindow.CreateRoundVideo(clip);
            var b = GazeMinigameWindow.CreateRoundVideo(clip);
            left.Children.Add(a); right.Children.Add(b);
            Assert.True(await OnFrames(a, () => a.FrameCount > 0, 5000));
            Assert.True(await OnFrames(b, () => b.FrameCount > 0, 5000));
            var sinkA = a.SinkForTest!;
            var sinkB = b.SinkForTest!;
            var oldStop = InlineLoopVideo.StopPlayer;
            using var release = new ManualResetEventSlim();
            using var entered = new CountdownEvent(2);
            try
            {
                InlineLoopVideo.StopPlayer = player => { entered.Signal(); release.Wait(); oldStop(player); };
                win.DisposeCurrentRoundPlayers();
                Assert.True(await Task.Run(() => entered.Wait(3000)), "stops did not overlap");
                var elapsed = Stopwatch.StartNew();
                win.Close();
                Assert.True(closed);
                Assert.True(elapsed.ElapsedMilliseconds < 500, "close exceeded the stop limit");
                var turn = false;
                Dispatcher.UIThread.Post(() => turn = true);
                Dispatcher.UIThread.RunJobs();
                Assert.True(turn);
                Assert.False(win.IsVisible);
                Assert.Equal(baseline + 2, InlineLoopVideo.LivePlayers);
                Assert.False(sinkA.Freed);
                Assert.False(sinkB.Freed);
                var shutdown = false;
                var exit = global::ConditioningControlPanel.Avalonia.App.ShutdownAfterGazeCleanupAsync(() => shutdown = true);
                await Task.Delay(550);
                await exit.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(shutdown);
                Assert.False(sinkA.Freed);
                Assert.False(sinkB.Freed);
                release.Set();
                await GazeMinigameWindow.PrepareForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(shutdown);
                Assert.True(closed);
                Assert.Equal(baseline, InlineLoopVideo.LivePlayers);
                Assert.True(sinkA.Freed && sinkB.Freed);
            }
            finally
            {
                release.Set();
                var deadline = Stopwatch.StartNew();
                while (InlineLoopVideo.LivePlayers != baseline && deadline.ElapsedMilliseconds < 5000)
                { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                InlineLoopVideo.StopPlayer = oldStop;
                if (!closed) win.Close();
            }
        });
    }

    [Fact]
    public async Task GazePanicDoesNotDelayLaterSafetySurfacesDuringActiveVideo()
    {
        await WithClip(async clip =>
        {
            var baseline = InlineLoopVideo.LivePlayers;
            var win = new GazeMinigameWindow();
            win.Show();
            var left = win.FindControl<Grid>("LeftPane")!;
            var right = win.FindControl<Grid>("RightPane")!;
            var a = GazeMinigameWindow.CreateRoundVideo(clip);
            var b = GazeMinigameWindow.CreateRoundVideo(clip);
            left.Children.Add(a); right.Children.Add(b);
            Assert.True(await OnFrames(a, () => a.FrameCount > 0, 5000));
            Assert.True(await OnFrames(b, () => b.FrameCount > 0, 5000));
            var oldStop = InlineLoopVideo.StopPlayer;
            var oldSurfaces = PanicSurfaces.All;
            using var release = new ManualResetEventSlim();
            using var entered = new CountdownEvent(2);
            try
            {
                InlineLoopVideo.StopPlayer = player => { entered.Signal(); release.Wait(TimeSpan.FromSeconds(5)); oldStop(player); };
                var haptics = false;
                var camera = false;
                PanicSurfaces.All = new[]
                {
                    oldSurfaces.Single(x => x.Id == "gaze-minigame"),
                    new PanicSurfaces.Surface("haptics", _ => haptics = true),
                    new PanicSurfaces.Surface("camera", _ => camera = true)
                };
                var elapsed = Stopwatch.StartNew();
                PanicSurfaces.StopAll("gaze cleanup test");
                Assert.True(elapsed.ElapsedMilliseconds < 500, "panic waited for native stop");
                Assert.True(haptics && camera);
                Assert.False(win.IsVisible);
                Assert.True(await Task.Run(() => entered.Wait(3000)), "both stops did not start");
                Assert.Equal(baseline + 2, InlineLoopVideo.LivePlayers);
                release.Set();
                await global::ConditioningControlPanel.Avalonia.App.ShutdownAfterGazeCleanupAsync(() => { }).WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(baseline, InlineLoopVideo.LivePlayers);
            }
            finally
            {
                release.Set();
                PanicSurfaces.All = oldSurfaces;
                var deadline = Stopwatch.StartNew();
                while (InlineLoopVideo.LivePlayers != baseline && deadline.ElapsedMilliseconds < 5000)
                { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
                InlineLoopVideo.StopPlayer = oldStop;
                win.Close();
            }
        });
    }

    private static async Task WithClip(Func<string, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-inline-video-").FullName;
        var clip = Path.Combine(dir, "clip.mp4");
        try
        {
            try
            {
                using var ff = Process.Start(new ProcessStartInfo("ffmpeg",
                    $"-v error -f lavfi -i testsrc=d=1:s=320x240:r=25 -pix_fmt yuv420p \"{clip}\"") { UseShellExecute = false })!;
                ff.WaitForExit();
            }
            catch (Exception) { }
            if (!File.Exists(clip)) Assert.Skip("ffmpeg not available to generate a test clip");
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }

            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                await body(clip);
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>Real decoding runs on wall time, so this waits on the control's frames (checked
    /// on each one) with a timeout, never on a fixed sleep. False when the timeout wins.</summary>
    private static async Task<bool> OnFrames(InlineLoopVideo video, Func<bool> ok, int ms)
    {
        if (ok()) return true;
        var done = new TaskCompletionSource();
        void Check() { if (ok()) done.TrySetResult(); }
        video.FrameShown += Check;
        try { await done.Task.WaitAsync(TimeSpan.FromMilliseconds(ms)); }
        catch (TimeoutException) { }
        finally { video.FrameShown -= Check; }
        return done.Task.IsCompletedSuccessfully || ok();
    }

    private static void Click(TopLevel host, Control target)
    {
        var p = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), host)!.Value;
        host.MouseMove(p, RawInputModifiers.None);
        host.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
        host.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }
}
