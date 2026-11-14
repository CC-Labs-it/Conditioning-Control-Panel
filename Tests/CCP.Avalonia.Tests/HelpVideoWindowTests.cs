using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.HelpLoops;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>HelpVideoWindow hosts the topic's drawn help loop like WPF TryShowLoop (help-loops-d):
/// the Session Editor's ? opens it with the SessionEditor loop playing; a topic without a loop
/// keeps WPF's caption fallback; a topic with a clip plays it muted and
/// looping through the head's LibVLC frame sink.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class HelpVideoWindowTests
{
    [Fact]
    public Task SessionEditorHelpOpensTheWindowWithTheLoopPlaying() => Run(() =>
    {
        var editor = new SessionEditorWindow();
        editor.Show();
        try
        {
            editor.FindControl<Button>("BtnHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var win = Current();
            Assert.NotNull(win);
            Assert.False(editor.FindControl<Control>("TutorialOverlay")!.IsVisible);

            var view = win!.GetVisualDescendants().OfType<HelpLoopView>().Single();
            Assert.Equal("SessionEditor", view.Scene.Id);
            Assert.True(win.FindControl<Border>("VideoContainer")!.IsVisible);
            Assert.False(win.FindControl<TextBlock>("TxtCaption")!.IsVisible);
            Assert.Single(win.GetVisualDescendants().OfType<HelpLoopSteps>());
            Assert.True(view.IsRunning);
            win.CaptureRenderedFrame();
            Assert.False(view.Failed);
            win.Close();
            Assert.Null(Current());
            Assert.False(view.IsRunning);
        }
        finally { Current()?.Close(); editor.Close(); }
    });

    [Fact]
    public Task SessionEditorClipOnlyTopicOpensHelpInsteadOfCoachMarks() => Run(() =>
    {
        // WPF SessionEditorWindow.xaml.cs:123-129 accepts a clip even without a drawn loop.
        var content = HelpContentService.GetContent("SessionEditor");
        var oldSection = content.SectionId;
        var oldClip = content.ClipFile;
        var editor = new SessionEditorWindow();
        try
        {
            content.SectionId = "SessionEditorClipOnlyTest";
            content.ClipFile = "missing-help-test.mp4";
            Assert.False(HelpLoopRegistry.Has(content.SectionId));
            editor.Show();
            editor.FindControl<Button>("BtnHelp")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var win = Current();
            Assert.NotNull(win);
            Assert.Same(editor, win!.Owner);
            Assert.False(editor.FindControl<Control>("TutorialOverlay")!.IsVisible);
            // A missing clip still opens the help window with WPF's caption fallback.
            Assert.False(win.FindControl<Border>("VideoContainer")!.IsVisible);
            Assert.True(win.FindControl<TextBlock>("TxtCaption")!.IsVisible);
        }
        finally
        {
            Current()?.Close();
            editor.Close();
            content.SectionId = oldSection;
            content.ClipFile = oldClip;
        }
    });

    [Fact]
    public Task WebcamCalibrationTopicHostsItsSliceCLoop() => Run(() =>
    {
        // What WebcamCalibrationWindow's ? passes (WebcamCalibrationWindow.axaml.cs:239).
        HelpVideoWindow.Show(HelpContentService.GetContent("WebcamCalibration"), null, topmost: true);
        var win = Current()!;
        try
        {
            var view = win.GetVisualDescendants().OfType<HelpLoopView>().Single();
            Assert.Equal("WebcamCalibration", view.Scene.Id);
            Assert.False(win.FindControl<TextBlock>("TxtCaption")!.IsVisible);
            win.CaptureRenderedFrame();
            Assert.False(view.Failed);
        }
        finally { win.Close(); }
    });

    [Fact]
    public Task TopicWithoutLoopKeepsTheCaptionFallback() => Run(() =>
    {
        var content = HelpContentService.GetContent("Modding");
        Assert.False(HelpLoopRegistry.Has(content.SectionId));
        HelpVideoWindow.Show(content, null);
        var win = Current()!;
        try
        {
            Assert.False(win.FindControl<Border>("VideoContainer")!.IsVisible);
            Assert.Empty(win.GetVisualDescendants().OfType<HelpLoopView>());
            var caption = win.FindControl<TextBlock>("TxtCaption")!;
            Assert.True(caption.IsVisible);
            Assert.Equal(Loc.Get(content.CaptionKey!), caption.Text);
        }
        finally { win.Close(); }
    });

    [Fact]
    public async Task ClipTopicPlaysTheClipMutedAndLoopingAndFreesItOnClose()
    {
        // No shipped topic sets ClipFile any more (release/6.11.3), so the test lays one down where
        // WPF looks: <base>/Resources/tutorial_videos/<ClipFile>.
        var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "tutorial_videos");
        Directory.CreateDirectory(dir);
        var clip = Path.Combine(dir, "help-test-clip.mp4");
        // bubblecount-2s.mp4 plus a silent AAC track, so the mute assert has an audio track to drop.
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "help-clip-audio-2s.mp4"), clip, overwrite: true);
        try
        {
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e)
            {
                // core-linux installs libvlc (build.yml), so a missing one there is a broken job, not a skip.
                if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CI") == "true") throw;
                Assert.Skip("libvlc not installed (apt install libvlc-dev vlc-plugin-base): " + e.Message);
            }
            await RunAsync(async () =>
            {
                var content = new HelpContent { SectionId = "HelpClipTest", Title = "Clip", ClipFile = "help-test-clip.mp4", WhatItDoes = "blurb" };
                Assert.False(HelpLoopRegistry.Has(content.SectionId));
                // WPF starts the clip on Loaded: a window that is never shown never starts a player.
                var unshown = new HelpVideoWindow(content);
                Assert.Null(unshown.Player);
                unshown.Close();
                HelpVideoWindow.Show(content, null);
                var win = Current()!;
                try
                {
                    var container = win.FindControl<Border>("VideoContainer")!;
                    Assert.True(container.IsVisible);
                    Assert.False(win.FindControl<TextBlock>("TxtCaption")!.IsVisible);   // a playing clip needs no blurb
                    Assert.False(win.Player!.EnableHardwareDecoding); // the RV32 callback sink needs CPU frames
                    var sink = win.Sink!;
                    // The sink can drop frames when the UI is busy. Check both the original
                    // 70 displayed frames and a frame after the third completed pass restarts.
                    var looped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    int endings = 0, restarted = 0;
                    win.Player!.EndReached += (_, _) => Interlocked.Increment(ref endings);
                    win.Player.Playing += (_, _) => { if (Volatile.Read(ref endings) >= 3) Volatile.Write(ref restarted, 1); };
                    win.FrameShown += () => { if (Volatile.Read(ref restarted) != 0 && sink.FrameCount >= 70) looped.TrySetResult(); };
                    await looped.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    Assert.True(Volatile.Read(ref endings) >= 3);
                    Assert.True(sink.FrameCount >= 70, $"looped past the 2 s end ({sink.FrameCount} frames)");
                    Assert.Same(sink.Bitmap, Assert.IsType<Image>(container.Child).Source);
                    Assert.Equal(-1, win.Player!.AudioTrack);   // muted tutorial loop: the clip's audio track is never selected
                    win.Close();
                    Assert.Null(Current());
                    Assert.Null(win.Player);
                    Assert.True(sink.Freed);
                }
                finally { Current()?.Close(); }
            });
        }
        finally { File.Delete(clip); }
    }

    [Fact]
    public Task PanicClosesCurrentHelpWindowAndIsSafeWhenIdle() => Run(() =>
    {
        HelpVideoWindow.Show(new HelpContent { SectionId = "PanicHelpTest", Title = "Help", WhatItDoes = "Help" }, null);
        var win = Current()!;
        try
        {
            var stop = PanicSurfaces.All.Single(x => x.Id == "help-video");
            stop.Stop(null);
            Assert.False(win.IsVisible);
            Assert.Null(Current());
            stop.Stop(null);
        }
        finally { win.Close(); }
    });

    [Fact]
    public Task StalledHelpDecoderDoesNotBlockLaterPanicSteps() => RunAsync(async () =>
    {
        HelpVideoWindow.Show(new HelpContent { SectionId = "StalledHelpTest", Title = "Help" }, null);
        var win = Current()!;
        using var release = new System.Threading.ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        win.BeforeNativeStop = () =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(5)); // bounded even with broken synchronous cleanup
            exited.TrySetResult();
        };
        var all = PanicSurfaces.All;
        var later = false;
        PanicSurfaces.All = new[]
        {
            all.Single(x => x.Id == "help-video"),
            new PanicSurfaces.Surface("later-engine-haptics", _ => later = true)
        };
        try
        {
            PanicSurfaces.StopAll("stalled help decoder test");
            Assert.True(later);
            Assert.False(win.IsVisible);
            Assert.Null(Current());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(exited.Task.IsCompleted); // the later step ran while cleanup was stalled
            Assert.NotNull(win.NativeTeardown);
            Assert.False(win.NativeTeardown!.IsCompleted);
        }
        finally
        {
            release.Set();
            PanicSurfaces.All = all;
            if (win.NativeTeardown != null) await win.NativeTeardown.WaitAsync(TimeSpan.FromSeconds(5));
            win.BeforeNativeStop = null;
            win.Close();
        }
    });

    [Fact]
    public async Task PanicKeepsDecoderBufferUntilNativeTeardownCompletes()
    {
        try { _ = new LibVlcAudio("--aout=dummy"); }
        catch (Exception e) { Assert.Skip("libvlc not installed: " + e.Message); }
        await RunAsync(async () =>
        {
            var clip = Path.Combine(AppContext.BaseDirectory, "Assets", "help-clip-audio-2s.mp4");
            HelpVideoWindow.Show(new HelpContent { SectionId = "RetainedHelpTest", Title = "Help", ClipFile = clip }, null);
            var win = Current()!;
            var sink = win.Sink!;
            using var release = new System.Threading.ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            win.FrameShown += () => frame.TrySetResult();
            try
            {
                await frame.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(sink.Freed);
                win.BeforeNativeStop = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5)); };
                PanicSurfaces.All.Single(x => x.Id == "help-video").Stop(null);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(win.IsVisible);
                Assert.Null(win.Player);
                Assert.False(sink.Freed); // callback buffer belongs to the stalled decoder
                Assert.False(win.NativeTeardown!.IsCompleted);
            }
            finally
            {
                release.Set();
                if (win.NativeTeardown != null) await win.NativeTeardown.WaitAsync(TimeSpan.FromSeconds(5));
                win.BeforeNativeStop = null;
                win.Close();
            }
            Assert.True(sink.Freed);
        });
    }

    private static HelpVideoWindow? Current() =>
        (HelpVideoWindow?)typeof(HelpVideoWindow).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

    private static Task Run(Action body) => RunAsync(() => { body(); return Task.CompletedTask; });

    private static Task RunAsync(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        HelpLoopView.RequestAnimationFrame = (_, _) => { };   // no real frame clock in tests
        try { await body(); }
        finally
        {
            HelpLoopView.RequestAnimationFrame = (top, cb) => top.RequestAnimationFrame(cb);
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
