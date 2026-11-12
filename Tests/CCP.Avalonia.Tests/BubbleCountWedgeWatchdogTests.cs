using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
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
        var watchdog = new BubbleCountWedgeWatchdog(() => clock, ids => released.AddRange(ids));
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
