using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubbleCountWedgeWatchdogTests
{
    [Fact]
    public void EscapeReleasesEachLiveWindowOnceAndStopsAfterDisarm()
    {
        long clock = 0;
        var released = new List<IntPtr>();
        var watchdog = new BubbleCountWedgeWatchdog(() => clock, ids => released.AddRange(ids), () => { });
        watchdog.Arm(false);
        watchdog.Register((IntPtr)1);
        watchdog.Register((IntPtr)2);
        watchdog.Register((IntPtr)2);
        watchdog.Register((IntPtr)3);
        watchdog.Unregister((IntPtr)3);
        clock = VideoWedgeLadder.StallMs + 1;
        watchdog.Tick();
        Assert.Empty(released); // escape is rung 3, not rung 1
        watchdog.Beat();
        clock += VideoWedgeLadder.Rung3Ms - 1;
        watchdog.Tick();
        Assert.Empty(released);
        clock++;
        watchdog.Tick();
        watchdog.Tick();
        Assert.Equal(new[] { 1, 2 }, released.Select(x => x.ToInt32()).OrderBy(x => x));
        watchdog.Disarm();
        clock += VideoWedgeLadder.Rung3Ms;
        watchdog.Tick();
        Assert.Equal(2, released.Count);
        watchdog.Arm(false);
        watchdog.Register((IntPtr)4);
        watchdog.Disarm();
        clock += VideoWedgeLadder.Rung3Ms;
        watchdog.Tick();
        Assert.Equal(2, released.Count);
    }

    [Fact]
    public void WindowsHandlesAreCachedAndReleased()
    {
        long clock = 0;
        var released = new List<IntPtr>();
        int x11Calls = 0;
        var watchdog = new BubbleCountWedgeWatchdog(() => clock,
            ids => BubbleCountWedgeWatchdog.Release(ids, true,
                windows => BubbleCountWedgeWatchdog.ReleaseEach(windows, id =>
                {
                    released.Add(id);
                    if (id == (IntPtr)1) throw new InvalidOperationException("one window fails");
                }, work => { work(); return Task.CompletedTask; }), _ => x11Calls++), () => { });
        watchdog.Arm(false);
        Assert.True(watchdog.RegisterHandle(new PlatformHandle((IntPtr)1, "HWND")));
        Assert.True(watchdog.RegisterHandle(new PlatformHandle((IntPtr)2, "HWND")));
        Assert.False(watchdog.RegisterHandle(new PlatformHandle(IntPtr.Zero, "HWND")));
        Assert.False(watchdog.RegisterHandle(new PlatformHandle((IntPtr)3, "headless")));
        clock = VideoWedgeLadder.StallMs;
        watchdog.Tick();
        clock = VideoWedgeLadder.Rung3Ms;
        watchdog.Tick();
        watchdog.Tick();
        Assert.Equal(0, x11Calls);
        // A failing release does not stop the next window's.
        Assert.Equal(new[] { 1, 2 }, released.Select(x => x.ToInt32()).OrderBy(x => x));
        watchdog.Disarm();
    }

    [Fact]
    public void WedgeStopRunsOnceAndCannotBlockEscapeOrDuplicateTeardownStop()
    {
        long clock = 0;
        int stops = 0, escapes = 0;
        using var entered = new ManualResetEventSlim();
        using var unblock = new ManualResetEventSlim();
        var owner = new object();
        Task? pending = null;
        void Stop() { Interlocked.Increment(ref stops); entered.Set(); unblock.Wait(); }
        var watchdog = new BubbleCountWedgeWatchdog(() => clock, _ => escapes++,
            () => pending = BubbleCountWindow.QueueNativeStop(owner, Stop));
        try
        {
            watchdog.Arm(false);
            clock = VideoWedgeLadder.StallMs - 1;
            watchdog.Tick();
            Assert.Null(pending);
            clock++;
            watchdog.Tick();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotNull(pending);
            Assert.Same(pending, BubbleCountWindow.QueueNativeStop(owner, Stop));
            watchdog.Tick();
            Assert.Equal(1, stops);
            clock = VideoWedgeLadder.Rung3Ms;
            watchdog.Tick();
            Assert.Equal(1, escapes);
            Assert.False(pending!.IsCompleted);
        }
        finally
        {
            unblock.Set();
            pending?.GetAwaiter().GetResult();
            watchdog.Disarm();
        }
    }

    [Fact]
    public void CleanupWaitsForTheWedgeStopBeforeDisposal()
    {
        using var entered = new ManualResetEventSlim();
        using var unblock = new ManualResetEventSlim();
        using var settled = new ManualResetEventSlim();
        int stopped = 0, disposed = 0;
        var owner = new object();
        var wedgeStop = BubbleCountWindow.QueueNativeStop(owner, () =>
        {
            Interlocked.Increment(ref stopped);
            entered.Set();
            unblock.Wait();
        });
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var teardownStop = BubbleCountWindow.QueueNativeStop(owner, () => Interlocked.Increment(ref stopped));
            Assert.False(BubbleCountWindow.StopNative(() => teardownStop.GetAwaiter().GetResult(),
                () => Interlocked.Increment(ref disposed), () => { }, new object(), 20,
                post: action => action(), settled: () => settled.Set()));
            Assert.Equal(0, disposed);
            Assert.Equal(1, stopped);
        }
        finally { unblock.Set(); }
        Assert.True(settled.Wait(TimeSpan.FromSeconds(5)));
        wedgeStop.GetAwaiter().GetResult();
        Assert.Equal(1, disposed);
        Assert.Equal(1, stopped);
    }

    [Fact]
    public void ACompletedWedgeStopDoesNotReplaceTheLaterCleanupStop()
    {
        int stops = 0;
        var owner = new object();
        var first = BubbleCountWindow.QueueNativeStop(owner, () => Interlocked.Increment(ref stops));
        first.GetAwaiter().GetResult();
        var second = BubbleCountWindow.QueueNativeStop(owner, () => Interlocked.Increment(ref stops));
        second.GetAwaiter().GetResult();
        Assert.NotSame(first, second);
        Assert.Equal(2, stops);
    }

    [Fact]
    public Task CompleteSkipAndCloseAllDisarm() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var gate = BrowserVideoGate.EngineAccepts;
        var blocked = BubbleCountWindow.NativeUnavailable;
        var path = Path.GetTempFileName();
        try
        {
            BrowserVideoGate.EngineAccepts = (_, _) => false;
            int complete = 0, skip = 0;
            BubbleCountWindow.ShowOnAllMonitors(path + ".missing", BubbleCountScheduler.Difficulty.Easy, false,
                _ => complete++);
            Assert.Equal(1, complete);
            Assert.False(BubbleCountWedgeWatchdog.Game.IsArmed);
            BubbleCountWindow.NativeUnavailable = true;
            BubbleCountWindow.ShowOnAllMonitors(path, BubbleCountScheduler.Difficulty.Easy, false,
                _ => complete++, () => skip++);
            Assert.Equal(1, skip);
            Assert.False(BubbleCountWedgeWatchdog.Game.IsArmed);
            BubbleCountWedgeWatchdog.Game.Arm();
            BubbleCountWindow.ForceCloseAll();
            Assert.False(BubbleCountWedgeWatchdog.Game.IsArmed);
        }
        finally
        {
            BubbleCountWindow.ForceCloseAll();
            BubbleCountWindow.NativeUnavailable = blocked;
            BrowserVideoGate.EngineAccepts = gate;
            File.Delete(path);
        }
        return Task.CompletedTask;
    });
}
