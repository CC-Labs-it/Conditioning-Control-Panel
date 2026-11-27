using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4.2 (ccp-bugs #1718): the camera picker's COM enumeration ran on the UI thread from ShowTab
/// and one hang report's stack sat in it. <see cref="WebcamDeviceScan"/> runs it on its own STA
/// thread, one scan at a time.
/// </summary>
public class WebcamDeviceScanTests
{
    private static IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice> Two() => new[]
    {
        new WebcamDeviceEnumerator.WebcamDevice(0, "Desk cam"),
        new WebcamDeviceEnumerator.WebcamDevice(1, "Virtual cam"),
    };

    [Fact]
    public async Task The_scan_runs_on_its_own_sta_thread_and_the_caller_does_not_wait()
    {
        var release = new ManualResetEventSlim();
        int callerThread = Environment.CurrentManagedThreadId;
        int scanThread = -1;
        ApartmentState apartment = ApartmentState.Unknown;
        var scan = new WebcamDeviceScan();

        var task = scan.Run(() =>
        {
            scanThread = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
            release.Wait(TimeSpan.FromSeconds(10));
            return Two();
        }, out bool started);

        // Run returned while the enumeration is still blocked: a wedged driver no longer holds the caller.
        Assert.True(started);
        Assert.False(task.IsCompleted);
        Assert.True(scan.Busy);
        release.Set();
        var devices = await task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, devices.Count);
        Assert.NotEqual(callerThread, scanThread);
        Assert.Equal(ApartmentState.STA, apartment);
        Assert.False(scan.Busy);
    }

    [Fact]
    public async Task A_second_ask_while_one_runs_joins_it_instead_of_scanning_again()
    {
        var release = new ManualResetEventSlim();
        int calls = 0;
        var scan = new WebcamDeviceScan();
        IReadOnlyList<WebcamDeviceEnumerator.WebcamDevice> Slow()
        {
            Interlocked.Increment(ref calls);
            release.Wait(TimeSpan.FromSeconds(10));
            return Two();
        }

        var first = scan.Run(Slow, out bool startedFirst);
        var second = scan.Run(Slow, out bool startedSecond);
        Assert.True(startedFirst);
        Assert.False(startedSecond);
        Assert.Same(first, second);
        release.Set();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, calls);

        // Once it has answered, the next ask scans afresh (a camera plugged in since).
        var third = scan.Run(Slow, out bool startedThird);
        Assert.True(startedThird);
        await third.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_scan_that_throws_answers_an_empty_list()
    {
        var scan = new WebcamDeviceScan();
        var devices = await scan.Run(() => throw new InvalidOperationException("wedged driver"), out _)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(devices);
    }

    [Fact]
    public void The_picker_never_enumerates_on_the_ui_thread()
    {
        // The two LabTab callers that used to call EnumerateDevices() inline now go through the scan.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "MainWindow")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "MainWindow", "MainWindow.LabTab.cs"));
        Assert.DoesNotContain("EnumerateDevices()", src);
        Assert.Contains("_webcamDeviceScan.Run(", src);
    }
}
