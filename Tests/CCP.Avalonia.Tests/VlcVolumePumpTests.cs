using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VlcVolumePumpTests
{
    [Fact]
    public async Task UpdatesRunOffCallerAndCollapseToLatest()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var values = new ConcurrentQueue<int>();
        var caller = Environment.CurrentManagedThreadId;
        var worker = caller;
        var pump = new VlcVolumePump(value =>
        {
            worker = Environment.CurrentManagedThreadId;
            values.Enqueue(value);
            if (value == 1) { entered.Set(); release.Wait(); }
            if (value == 3) done.Set();
        });
        try
        {
            pump.Push(1);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            pump.Push(2);
            pump.Push(3);
        }
        finally { release.Set(); }
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        await pump.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(caller, worker);
        Assert.Equal(new[] { 1, 3 }, values.ToArray());
    }

    [Fact]
    public async Task RetirementStartsStopWithoutWaitingForVolumeAndDefersDisposal()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var stopped = new ManualResetEventSlim();
        var calls = 0;
        var disposed = 0;
        var cleaned = 0;
        var pump = new VlcVolumePump(_ => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(); });
        pump.Push(1);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task? retirement = null;
        try
        {
            // Run the entry point on a separate caller so a regression cannot hang the test host.
            var callReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = Task.Run(() =>
            {
                retirement = VlcPlaybackRetirement.Retire(pump, stopped.Set,
                    () => Interlocked.Increment(ref disposed), () => Interlocked.Increment(ref cleaned));
                callReturned.SetResult();
            });
            await callReturned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(stopped.Wait(TimeSpan.FromSeconds(5)));
            pump.Push(2);
            Assert.Equal(0, Volatile.Read(ref disposed));
            Assert.Equal(0, Volatile.Read(ref cleaned));
            Assert.False(retirement!.IsCompleted);
        }
        finally { release.Set(); }
        await retirement!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal(1, Volatile.Read(ref disposed));
        Assert.Equal(1, Volatile.Read(ref cleaned));
    }

    [Fact]
    public async Task RetirementWaitsForStopAndQuarantinesFailure()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var disposed = 0;
        var cleaned = 0;
        var retirement = VlcPlaybackRetirement.Retire(null, () => { entered.Set(); release.Wait(); },
            () => Interlocked.Increment(ref disposed), () => Interlocked.Increment(ref cleaned));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(retirement.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref disposed));
        }
        finally { release.Set(); }
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref disposed));
        Assert.Equal(1, Volatile.Read(ref cleaned));

        var quarantined = 0;
        await VlcPlaybackRetirement.Retire(null, () => throw new InvalidOperationException(),
            () => Interlocked.Increment(ref disposed), () => Interlocked.Increment(ref cleaned),
            _ => Interlocked.Increment(ref quarantined)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref quarantined));
        Assert.Equal(1, Volatile.Read(ref disposed));
        Assert.Equal(1, Volatile.Read(ref cleaned));
    }
}
