using System;
using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using ConditioningControlPanel.Avalonia.Platform;

namespace CCP.Avalonia.Tests;

/// <summary>WPF App.xaml.cs:1949-3867 + Windows/SplashScreen.xaml.cs: the splash comes up first, each
/// startup step paints its status before the next runs, then the shell shows and the splash fades.</summary>
// Shared dispatcher/settings snapshots must remain exclusive across async lifecycle turns.
[Collection(RunsAloneCollection.Name)]
public sealed class SplashStartupTests
{
    private static void Setup() => LifecycleTestPump.Setup();

    private static double Bar(SplashScreen s) =>
        ((ScaleTransform)s.FindControl<Border>("ProgressFill")!.RenderTransform!).ScaleX;

    private static string Status(SplashScreen s) => s.FindControl<TextBlock>("TxtStatus")!.Text!;

    [Fact]
    public Task EachStepPaintsBeforeTheNextThenTheShellTakesOver() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var splash = SplashScreen.ShowOnOwnThread()!;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        splash.Closed += (_, _) => closed.TrySetResult();
        var shell = new Window();
        var ran = new List<string>();
        try
        {
            var start = App.StartBehindSplash(splash, async step =>
            {
                ran.Add("settings");
                await step(0.2, "Loading settings...");
                Assert.Equal("Loading settings...", Status(splash));
                Assert.False(shell.IsVisible);
                ran.Add("audio");
                var audioFrame = step(0.6, "Initializing audio...");
                Assert.Equal(0.6, Bar(splash), 3); // before the live creep timer can advance it
                await audioFrame;
                Assert.Equal("Initializing audio...", Status(splash));
                Assert.False(shell.IsVisible);
                ran.Add("shell built");
            }, () => shell);

            // The first step waits for a frame: nothing after it has run, its status is up.
            Assert.Equal(new[] { "settings" }, ran);
            Assert.Equal("Loading settings...", Status(splash));
            Assert.Equal(0.2, Bar(splash), 3);   // jumped to WPF's value, not left creeping
            Assert.False(shell.IsVisible);

            await LifecycleTestPump.UntilAsync(() => start.IsCompleted, "Startup did not complete.");
            await start;
            Assert.Equal(new[] { "settings", "audio", "shell built" }, ran);
            Assert.True(shell.IsVisible);
            Assert.Equal("Ready!", Status(splash));
            Assert.False(splash.Topmost);   // dropped first so the shell is not hidden under the fade

            await LifecycleTestPump.UntilAsync(() => !splash.IsVisible, "The splash fade did not close its window.");
            Assert.False(splash.IsVisible);
        }
        finally { splash.CloseImmediate(); shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    // WPF App.xaml.cs:2182,4069: intermediate dispatcher pumps do not end startup protection.
    [Fact]
    public Task StartupThatPumpsThenBlocksStillAcknowledgesRelaunches() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var savedAck = SingleInstance.ShowAckTimeoutMs;
        var savedWait = SingleInstance.StaleTakeoverWait;
        var savedKill = SingleInstance.KillStale;
        using var blocked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var suffix = "_test_" + Guid.NewGuid().ToString("N");
        using var primary = SingleInstance.Claim(suffix, _ => Dispatcher.UIThread.InvokeAsync(() => { }).GetTask());
        Task<bool>? relaunch = null;
        try
        {
            int kills = 0;
            SingleInstance.ShowAckTimeoutMs = 100;
            SingleInstance.StaleTakeoverWait = TimeSpan.FromMilliseconds(10);
            SingleInstance.KillStale = () => { Interlocked.Increment(ref kills); return 0; };
            bool pumped = false, completed = false;
            Dispatcher.UIThread.Post(() => pumped = true);
            var launchResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            relaunch = launchResult.Task;
            // Claim blocks while the pipe listener uses the thread pool. A separate thread models
            // the second process without starving that listener on a loaded test runner.
            var launch = new Thread(() =>
            {
                bool exited = false;
                Exception? failure = null;
                try
                {
                    Assert.True(blocked.Wait(TimeSpan.FromSeconds(10)));
                    using var second = SingleInstance.Claim(suffix, _ => Task.CompletedTask);
                    Thread.Sleep(300); // Keep the UI blocked beyond the 100 ms acknowledgement timeout.
                    exited = second is null;
                }
                catch (Exception ex) { failure = ex; }
                finally { release.Set(); }
                if (failure is null) launchResult.TrySetResult(exited);
                else launchResult.TrySetException(failure);
            }) { IsBackground = true };
            launch.Start();
            var start = App.StartBehindSplash(null, async step =>
            {
                await step(0.2, "Loading settings...");
                await Dispatcher.Yield(DispatcherPriority.Background); // Model a splash frame without creating fonts.
                Assert.True(pumped);
                blocked.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            }, () => null, () => { completed = true; primary!.EndStartupPhase(); });
            Dispatcher.UIThread.RunJobs();
            await start;
            Assert.True(await relaunch, $"Second launch did not exit; stale takeover attempted {Volatile.Read(ref kills)} times.");
            Assert.True(completed);
            Assert.Equal(0, Volatile.Read(ref kills));
        }
        finally
        {
            release.Set();
            try { if (relaunch != null) await relaunch; }
            finally
            {
                SingleInstance.ShowAckTimeoutMs = savedAck;
                SingleInstance.StaleTakeoverWait = savedWait;
                SingleInstance.KillStale = savedKill;
                Dispatcher.UIThread.RunJobs();
            }
        }
    });

    [Fact]
    public Task WithoutASplashStartupRunsInline() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        bool done = false, completed = false;
        var start = App.StartBehindSplash(null, async step => { await step(0.2, "x"); done = true; }, () => null, () => completed = true);
        Assert.True(start.IsCompletedSuccessfully);   // tests and checks still get a synchronous start
        Assert.True(done);
        Assert.True(completed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedStartupNeverSignalsCompletion() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        bool completed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => App.StartBehindSplash(null,
            _ => Task.FromException(new InvalidOperationException("boom")), () => null, () => completed = true));
        Assert.False(completed);
    });

    [Fact]
    public Task AFailedStartupClosesTheSplash() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var splash = SplashScreen.ShowOnOwnThread()!;
        bool completed = false;
        var start = App.StartBehindSplash(splash, async step =>
        {
            await step(0.2, "Loading settings...");
            throw new InvalidOperationException("boom");
        }, () => null, () => completed = true);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => start);
            Assert.False(splash.IsVisible);
            Assert.False(completed);
            // Recorded for Program.Main to rethrow once the loop ends (closing the splash ends it with 0).
            Assert.Equal("boom", Assert.Throws<InvalidOperationException>(() => App.StartupFailure!.Throw()).Message);
        }
        finally { App.StartupFailure = null; }
    });

    /// <summary>Owner WSLg run: "the splash screen doesn't move". The sweep is a render-thread composition
    /// animation, so a startup step holding the UI thread cannot freeze it.</summary>
    [Fact]
    public Task TheShimmerSweepsOnTheRenderThread() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var splash = SplashScreen.ShowOnOwnThread()!;
        try
        {
            Assert.True(splash.ShimmerStarted);
            // No UI-thread transform is left driving it (the old Style.Animations sweep froze with startup).
            Assert.Null(splash.FindControl<Border>("Shimmer")!.RenderTransform);
        }
        finally { splash.CloseImmediate(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task AStalledStepCreepsAndTheHintReassures() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var splash = SplashScreen.ShowOnOwnThread()!;
        try
        {
            splash.SetProgress(0.4, "Initializing flash service...");
            for (int i = 0; i < 400; i++) splash.CreepTick();   // 120 ms ticks, stepped
            Assert.Equal(0.45, splash.DisplayedProgress, 3);   // caught up, then 5% past the stalled target
            Assert.Equal(0.45, ((ScaleTransform)splash.FindControl<Border>("ProgressFill")!.RenderTransform!).ScaleX, 3);

            var hint = splash.FindControl<TextBlock>("TxtHint")!;
            splash.ReassureTick();   // 8 s
            Assert.Equal("Still loading... the app is fine and will open on its own.", hint.Text);
            splash.ReassureTick();   // 16 s
            Assert.Equal("Almost there. Large libraries can take a minute to warm up.", hint.Text);
        }
        finally { splash.CloseImmediate(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });
}
