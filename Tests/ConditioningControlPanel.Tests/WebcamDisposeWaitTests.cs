using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Closing the app never waits out a camera start (hang report 2026-11-16: the close sat in
/// WebcamTrackingService.Dispose behind a start stuck in the camera open, and the app had to be ended).</summary>
public class WebcamDisposeWaitTests
{
    [Fact]
    public void Dispose_gives_up_on_a_start_that_holds_the_state_lock()
    {
        var service = new WebcamTrackingService();
        var gate = typeof(WebcamTrackingService)
            .GetField("_stateLock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(service)!;

        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (gate)
            {
                held.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            }
        }) { IsBackground = true };
        holder.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(5)));

        var clock = Stopwatch.StartNew();
        service.Dispose();
        clock.Stop();
        release.Set();
        holder.Join(TimeSpan.FromSeconds(5));

        Assert.True(clock.Elapsed < WebcamTrackingService.DisposeLockWait + TimeSpan.FromSeconds(2),
            $"Dispose waited {clock.Elapsed.TotalSeconds:0.0}s behind the start");
    }

    [Fact]
    public void Dispose_of_an_idle_service_returns_at_once()
    {
        var service = new WebcamTrackingService();
        var clock = Stopwatch.StartNew();
        service.Dispose();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1));
    }
}
