using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
                    var sink = win.Sink!;
                    // The 2 s, 24-frame clip runs past its end: frames keep coming (WPF EndReached -> Play loop;
                    // without it one pass stops below 50 displays). Event-driven wait, no sleep (P08).
                    var looped = new TaskCompletionSource();
                    win.FrameShown += () => { if (sink.FrameCount >= 70) looped.TrySetResult(); };
                    try { await looped.Task.WaitAsync(TimeSpan.FromSeconds(10)); } catch (TimeoutException) { }
                    Assert.True(sink.FrameCount >= 70, $"looped past the 2 s end ({sink.FrameCount} frames)");
                    Assert.Same(sink.Bitmap, Assert.IsType<Image>(container.Child).Source);
                    Assert.Equal(-1, win.Player!.AudioTrack);   // muted tutorial loop: the clip's audio track is never selected
                    win.Close();
                    Assert.Null(Current());
                    Assert.Null(win.Player);
                    Assert.True(sink.Freed);

                    HelpVideoWindow.Show(content, null);
                    var replaced = Current()!;
                    var replacedSink = replaced.Sink!;
                    HelpVideoWindow.Show(content, null);
                    Assert.False(replaced.IsVisible);
                    Assert.Null(replaced.Player);
                    Assert.True(replacedSink.Freed);
                    var panicWindow = Current()!;
                    var panicSink = panicWindow.Sink!;
                    PanicSurfaces.All.Single(x => x.Id == "help-video").Stop(null);
                    Assert.False(panicWindow.IsVisible);
                    Assert.Null(panicWindow.Player);
                    Assert.True(panicSink.Freed);
                    Assert.Null(Current());
                }
                finally { Current()?.Close(); }
            });
        }
        finally { File.Delete(clip); }
    }

    [Fact]
    public Task PanicClosesCurrentHelpAndReplacementIsSafe() => Run(() =>
    {
        var content = new HelpContent { SectionId = "NoClip", Title = "Help", WhatItDoes = "Fallback" };
        HelpVideoWindow.Show(content, null);
        var first = Current()!;
        try
        {
            HelpVideoWindow.Show(content, null);
            Assert.False(first.IsVisible);
            var replacement = Current()!;
            Assert.NotSame(first, replacement);
            var stop = PanicSurfaces.All.Single(x => x.Id == "help-video");
            stop.Stop(null);
            stop.Stop(null);
            Assert.False(replacement.IsVisible);
            Assert.Null(Current());
        }
        finally { Current()?.Close(); }
    });

    private static HelpVideoWindow? Current() =>
        (HelpVideoWindow?)typeof(HelpVideoWindow).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

    private static Task Run(Action body) => RunAsync(() => { body(); return Task.CompletedTask; });

    private static Task RunAsync(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
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
