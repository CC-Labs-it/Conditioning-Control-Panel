using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ScreenShake = ConditioningControlPanel.Avalonia.Views.Deeper.ScreenShake;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class VoiceRemainingCommandsTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Say(MainShellWindow shell, string command)
    {
        Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi " + command, new[] { "hey bambi" }));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task OverlayCommandsHoldWithoutChangingSettingsReleaseTheirOwnerAndPanicClearsThem() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var saved = (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.PinkFilterEnabled, s.PinkFilterOpacity);
            var running = CoreSession.IsEngineRunningProvider;
            var gif = Path.GetTempFileName();
            File.WriteAllBytes(gif, Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                if (SpiralOverlay.Decoding is { } pending) await pending;
                SpiralOverlay.CloseAll();
                PinkFilterOverlay.CloseAll();
                SpiralOverlay.SkipPlatformChecksForTests = PinkFilterOverlay.SkipPlatformChecksForTests = true;
                CoreSession.IsEngineRunningProvider = () => false;
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.PinkFilterEnabled, s.PinkFilterOpacity) = (false, gif, 70, false, 10);
                Say(shell, "spiral on");
                PanicSurfaces.All.Single(x => x.Id == "voice-overlays").Stop(shell);
                if (SpiralOverlay.Decoding is { } decode) await decode;
                Assert.False(SpiralOverlay.IsShowing); // decode completion cannot resurrect a cancelled hold
                Say(shell, "spiral on");
                Assert.True(SpiralOverlay.IsShowing);
                Assert.Equal(SpiralFrames.Paint(0.7), SpiralOverlay.Shown[0].Spiral.Opacity, 8);
                SpiralOverlay.Refresh(shell); // the next settings reconciliation must not drop the hold
                Assert.True(SpiralOverlay.IsShowing);
                Assert.False(s.SpiralEnabled);
                Say(shell, "spiral off");
                Assert.False(SpiralOverlay.IsShowing);
                CoreSession.IsEngineRunningProvider = () => true;
                s.SpiralEnabled = true;
                Say(shell, "spiral on");
                Say(shell, "spiral off");
                Assert.True(SpiralOverlay.IsShowing); // persistent owner keeps the same saved opacity
                Assert.Null(SpiralOverlay.VoiceOpacity);
                s.SpiralEnabled = false;
                CoreSession.IsEngineRunningProvider = () => false;
                SpiralOverlay.Refresh(shell);
                Assert.False(SpiralOverlay.IsShowing);

                Say(shell, "go pink");
                Assert.True(PinkFilterOverlay.IsShowing);
                Assert.Equal(102, TintAlpha()); // voice's 40% floor
                Assert.False(s.PinkFilterEnabled);
                s.PinkFilterOpacity = 45; // the setting clamps to 50
                Say(shell, "go pink");
                Assert.Equal(114, TintAlpha()); // never dim the user's stronger configured tint
                Assert.Single(PinkFilterOverlay.Shown); // repeated on is idempotent
                PinkFilterOverlay.Refresh(shell);
                Assert.Equal(114, TintAlpha());

                // Release leaves a persistent feature alone only while its engine is running.
                CoreSession.IsEngineRunningProvider = () => true;
                s.PinkFilterEnabled = true;
                s.PinkFilterOpacity = 20;
                Say(shell, "pink filter off");
                Assert.True(PinkFilterOverlay.IsShowing);
                Assert.Null(PinkFilterOverlay.VoiceOpacity);
                Assert.Equal(51, TintAlpha());
                CoreSession.IsEngineRunningProvider = () => false;
                PinkFilterOverlay.Refresh(shell);
                Assert.False(PinkFilterOverlay.IsShowing);

                // A merely ticked checkbox with the engine off cannot strand a voice overlay.
                Say(shell, "go pink");
                Say(shell, "pink filter off");
                Assert.False(PinkFilterOverlay.IsShowing);
                Say(shell, "go pink");
                Say(shell, "spiral on");
                PanicSurfaces.All.Single(x => x.Id == "voice-overlays").Stop(shell);
                Assert.False(PinkFilterOverlay.IsShowing || SpiralOverlay.IsShowing);
                Assert.Null(PinkFilterOverlay.VoiceOpacity);
                Assert.Null(SpiralOverlay.VoiceOpacity);
                PinkFilterOverlay.Refresh(shell);
                SpiralOverlay.Refresh(shell);
                Assert.False(PinkFilterOverlay.IsShowing || SpiralOverlay.IsShowing);
                Assert.Contains("the spiral", shell.VoiceCmds.HelpLine());

                // WPF Stop -> StopPinkFilter/Overlay.Stop: stopping the engine drops voice holds too.
                CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                shell.StartEngine();
                Say(shell, "go pink");
                Assert.True(PinkFilterOverlay.IsShowing);
                MainShellWindow.StopEngine();
                Assert.False(PinkFilterOverlay.IsShowing);
                Assert.Null(PinkFilterOverlay.VoiceOpacity);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                SpiralOverlay.CloseAll();
                PinkFilterOverlay.CloseAll();
                SpiralOverlay.SkipPlatformChecksForTests = PinkFilterOverlay.SkipPlatformChecksForTests = false;
                CoreSession.IsEngineRunningProvider = running;
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.PinkFilterEnabled, s.PinkFilterOpacity) = saved;
                shell.Close();
                File.Delete(gif);
            }
        });

    [Fact]
    public Task ShakeCommandSynchronizesWindowsReplacesPriorRunAndPanicRestoresTransforms() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var shell = new MainShellWindow();
            var other = new Window { Content = new Border() };
            shell.Show();
            other.Show();
            var a = (Control)shell.Content!;
            var b = (Control)other.Content;
            var priorA = a.RenderTransform;
            var priorB = b.RenderTransform = new ScaleTransform(0.8, 0.8);
            var windows = ScreenShake.WindowsProvider;
            var now = ScreenShake.Now;
            try
            {
                long clock = 1000;
                ScreenShake.Now = () => clock;
                ScreenShake.WindowsProvider = () => new[] { shell, other };
                Say(shell, "shake the screen");
                ScreenShake.Tick();
                var first = Assert.IsType<TranslateTransform>(a.RenderTransform);
                var second = Assert.IsType<TranslateTransform>(b.RenderTransform);
                Assert.Equal((first.X, first.Y), (second.X, second.Y));
                Assert.InRange(first.X, -28, 28);
                clock += 1199;
                ScreenShake.Tick();
                Assert.True(ScreenShake.IsRunning);
                clock++;
                ScreenShake.Tick();
                Assert.False(ScreenShake.IsRunning);
                Assert.Same(priorA, a.RenderTransform);
                Assert.Same(priorB, b.RenderTransform);
                Say(shell, "shake the screen");
                Say(shell, "shake the screen"); // replace, never nest the jitter transform
                PanicSurfaces.All.Single(x => x.Id == "screen-shake").Stop(shell);
                Assert.Same(priorA, a.RenderTransform);
                Assert.Same(priorB, b.RenderTransform);
                var script = ScreenShake.WebScript(28, 1200);
                Assert.Contains("document.querySelector('video')", script);
                Assert.Contains("clearInterval(window._ccpShakeTimer)", script);
                Assert.Contains("removeProperty('transform')", script);
                Assert.EndsWith("})(28.0,1200);", script);
            }
            finally
            {
                ScreenShake.Stop();
                ScreenShake.WindowsProvider = windows;
                ScreenShake.Now = now;
                other.Close();
                shell.Close();
            }
            return Task.CompletedTask;
        });

    private static byte TintAlpha() => ((SolidColorBrush)((Border)PinkFilterOverlay.Shown[0].Content!).Background!).Color.A;

    [Fact]
    public Task VideoCommandsPauseWithoutHealingResumeAndRespectStrictAndGraceHolds() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            try { _ = new LibVlcAudio("--aout=dummy"); }
            catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }
            var o = MandatoryVideoOverlay.Instance;
            var oldScheduler = o.Scheduler;
            var oldVideo = CoreEngine.Video;
            var s = CoreSettings.Current;
            var saved = (s.AttentionChecksEnabled, s.VideoBlurredBackgroundEnabled, s.VideoMaxDurationSeconds);
            var shell = new MainShellWindow();
            shell.Show();
            var clip = Path.Combine(AppContext.BaseDirectory, "Assets", "bubblecount-2s.mp4");
            var scheduler = new MandatoryVideoScheduler(o, library: () => new[] { clip });
            try
            {
                (s.AttentionChecksEnabled, s.VideoBlurredBackgroundEnabled, s.VideoMaxDurationSeconds) = (false, false, 0);
                o.Scheduler = CoreEngine.Video = scheduler;
                async Task Open(bool strict)
                {
                    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Action onStarted = () => started.TrySetResult();
                    scheduler.VideoStarted += onStarted;
                    try
                    {
                        Assert.True(scheduler.Trigger(strict));
                        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); // after the pre-roll, Sink exists
                    }
                    finally { scheduler.VideoStarted -= onStarted; }
                    await o.Sink!.SecondFrame.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.True(o.Sink?.FrameCount >= 2);
                }
                await Open(false);
                Say(shell, "pause the video");
                Assert.True(o.PrimaryPaused);
                var sink = o.Sink;
                o.FrameTs = Stopwatch.GetTimestamp() - 20 * Stopwatch.Frequency;
                o.GuardTick(); // stale output while deliberately paused must neither replay nor end
                Assert.True(scheduler.IsPlaying);
                Assert.Same(sink, o.Sink);
                Say(shell, "resume the video");
                Assert.False(o.PrimaryPaused);
                Assert.True(Stopwatch.GetElapsedTime(o.FrameTs) < TimeSpan.FromSeconds(1));
                Say(shell, "pause the video");
                Assert.True(o.TryGracePause(false));
                Say(shell, "resume the video");
                Assert.True(o.GracePaused && o.PrimaryPaused); // voice cannot play underneath the grace card
                o.ResumeFromGrace("test card resume");
                Assert.False(o.GracePaused || o.PrimaryPaused); // the card's own Resume releases the player
                scheduler.Stop();
                Assert.False(o.PrimaryPaused || o.GracePaused);
                await Open(true);
                Say(shell, "pause the video");
                Assert.False(o.PrimaryPaused); // shared StopLocked guard refuses strict-video escape
                Assert.True(scheduler.IsPlaying);
            }
            finally
            {
                scheduler.Stop();
                o.Scheduler = oldScheduler;
                CoreEngine.Video = oldVideo;
                (s.AttentionChecksEnabled, s.VideoBlurredBackgroundEnabled, s.VideoMaxDurationSeconds) = saved;
                shell.Close();
            }
        });
}
