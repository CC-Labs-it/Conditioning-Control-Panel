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
                Say(shell, "shake the screen");
                shell.OnEngineStopped();
                Assert.False(ScreenShake.IsRunning);
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

    [Fact]
    public Task VideoCommandsPauseWithoutHealingResumeAndRespectStrictAndGraceHolds() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            _ = new LibVlcAudio("--aout=dummy");
            var o = MandatoryVideoOverlay.Instance;
            var oldScheduler = o.Scheduler;
            var oldVideo = CoreEngine.Video;
            var s = CoreSettings.Current;
            var saved = (s.AttentionChecksEnabled, s.VideoBlurredBackgroundEnabled, s.VideoMaxDurationSeconds);
            var shell = new MainShellWindow();
            shell.Show();
            var clip = Path.Combine(AppContext.BaseDirectory, "Assets", "voice-video-30s.mp4");
            // The pre-roll fires when this test fires it, here on the dispatcher thread, never from a pool
            // timer: on a busy CI runner that pool callback (Show -> UIThread.Invoke, then VideoStarted and
            // the rest of the pre-roll body off-thread) raced the test body or arrived after the 10 s bound.
            var preroll = new PrerollClock();
            var scheduler = new MandatoryVideoScheduler(o, preroll, library: () => new[] { clip });
            try
            {
                (s.AttentionChecksEnabled, s.VideoBlurredBackgroundEnabled, s.VideoMaxDurationSeconds) = (false, false, 0);
                o.Scheduler = CoreEngine.Video = scheduler;
                async Task Open(bool strict)
                {
                    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Action onStarted = () =>
                    {
                        // A busy dispatcher can outlast the old 2 s fixture before it presents two frames.
                        // Exercise that delay once while the real player continues decoding.
                        if (!strict) System.Threading.Thread.Sleep(2500);
                        started.TrySetResult();
                    };
                    scheduler.VideoStarted += onStarted;
                    try
                    {
                        Assert.True(scheduler.Trigger(strict));
                        // The pre-roll (and the freeze lead before it when a Freeze surface is seeded).
                        while (!started.Task.IsCompleted && preroll.FireArmed()) { }
                        // When Prepare() is still running at the end of the pre-roll, the show arrives through a
                        // posted continuation (MandatoryVideoScheduler.ShowPrepared): pump the dispatcher for it
                        // instead of asserting on the first pass (failed twice on a loaded Windows shard).
                        var deadline = Stopwatch.GetTimestamp() + 10 * Stopwatch.Frequency;
                        while (!started.Task.IsCompleted && Stopwatch.GetTimestamp() < deadline)
                        {
                            Dispatcher.UIThread.RunJobs();
                            if (!preroll.FireArmed()) System.Threading.Thread.Sleep(10);
                        }
                        Assert.True(started.Task.IsCompletedSuccessfully, "the pre-roll showed the clip"); // Sink exists
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

    /// <summary>The wall clock, except that every timer armed through it fires only from <see cref="FireArmed"/>.</summary>
    private sealed class PrerollClock : TimeProvider
    {
        private readonly global::System.Collections.Generic.List<ManualTimer> _timers = new();

        public override global::System.Threading.ITimer CreateTimer(global::System.Threading.TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(() => callback(state));
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        /// <summary>Fires each armed timer once on the calling thread; false when none was armed.</summary>
        public bool FireArmed()
        {
            var armed = _timers.Where(t => t.Armed).ToArray();
            foreach (var t in armed) t.Fire();
            return armed.Length > 0;
        }

        private sealed class ManualTimer(Action fire) : global::System.Threading.ITimer
        {
            internal bool Armed { get; private set; }
            internal void Fire() { Armed = false; fire(); }   // a callback may re-arm it (freeze lead -> pre-roll)
            public bool Change(TimeSpan dueTime, TimeSpan period) { Armed = dueTime != global::System.Threading.Timeout.InfiniteTimeSpan; return true; }
            public void Dispose() => Armed = false;
            public ValueTask DisposeAsync() { Armed = false; return default; }
        }
    }
}
