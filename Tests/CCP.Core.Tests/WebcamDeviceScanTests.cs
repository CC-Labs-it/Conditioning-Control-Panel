using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class WebcamDeviceScanTests
{
    [Fact]
    public async Task ScanRunsOffThreadAndJoinsWhileBusyThenRunsAgain()
    {
        var scan = new WebcamScanCoordinator<int>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int caller = Environment.CurrentManagedThreadId, worker = caller, calls = 0;
        IReadOnlyList<int> Enumerate()
        {
            Interlocked.Increment(ref calls);
            worker = Environment.CurrentManagedThreadId;
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(5));
            return new[] { 7 };
        }
        var first = scan.Run(Enumerate, out var started);
        try
        {
            Assert.True(started);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var joined = scan.Run(Enumerate, out var secondStarted);
            Assert.False(secondStarted);
            Assert.Same(first, joined);
            Assert.NotEqual(caller, worker);
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        Assert.Equal(new[] { 7 }, await first);
        var next = scan.Run(Enumerate, out var nextStarted);
        Assert.True(nextStarted);
        await next;
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ScanFailureReturnsEmptyAndAllowsRetry()
    {
        var scan = new WebcamScanCoordinator<int>();
        Assert.Empty(await scan.Run(() => throw new InvalidOperationException(), out _));
        Assert.Equal(new[] { 3 }, await scan.Run(() => new[] { 3 }, out var started));
        Assert.True(started);
    }
}
