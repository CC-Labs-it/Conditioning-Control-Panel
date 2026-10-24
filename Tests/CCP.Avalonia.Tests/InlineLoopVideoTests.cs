using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
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

    [Fact]
    public async Task HelpWindowClipLoopsAndReplacementAndPanicReleaseItsPlayer()
    {
        await WithClip(async clip =>
        {
            var baseline = InlineLoopVideo.LivePlayers;
            HelpVideoWindow? Current() => (HelpVideoWindow?)typeof(HelpVideoWindow)
                .GetField("_current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null);
            var content = new HelpContent { SectionId = "VideoTest", Title = "Tutorial", WhatItDoes = "Fallback", ClipFile = clip };
            try
            {
                HelpVideoWindow.Show(content, null);
                var first = Current()!;
                var video = first.GetVisualDescendants().OfType<InlineLoopVideo>().Single();
                Assert.True(first.FindControl<Border>("VideoContainer")!.IsVisible);
                Assert.Equal(global::Avalonia.Media.Stretch.Uniform, video.Stretch);
                Assert.True(await OnFrames(video, () => video.FrameCount > 30, 5000), "clip did not keep looping");
                Assert.Equal(baseline + 1, InlineLoopVideo.LivePlayers);
                HelpVideoWindow.Show(content, null);
                var second = Current()!;
                Assert.NotSame(first, second);
                Assert.False(video.HasPlayer);
                var replacement = second.GetVisualDescendants().OfType<InlineLoopVideo>().Single();
                Assert.True(await OnFrames(replacement, () => replacement.FrameCount > 0, 5000));
                PanicSurfaces.All.Single(x => x.Id == "help-video").Stop(null);
                Assert.Null(Current());
                Assert.False(replacement.HasPlayer);
                Assert.Equal(baseline, InlineLoopVideo.LivePlayers);
            }
            finally { Current()?.Close(); }
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
