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
    public void UpdatesRunOffCallerAndCollapseToLatest()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var values = new ConcurrentQueue<int>();
        var caller = Environment.CurrentManagedThreadId;
        var worker = caller;
        using var pump = new VlcVolumePump(value =>
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
        Assert.NotEqual(caller, worker);
        Assert.Equal(new[] { 1, 3 }, values.ToArray());
    }

    [Fact]
    public void DisposeWaitsForNativeCallAndRejectsLaterUpdates()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var count = 0;
        var pump = new VlcVolumePump(_ => { Interlocked.Increment(ref count); entered.Set(); release.Wait(); });
        pump.Push(1);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var dispose = Task.Run(pump.Dispose);
        try { Assert.False(dispose.Wait(100)); }
        finally { release.Set(); }
        Assert.True(dispose.Wait(TimeSpan.FromSeconds(5)));
        pump.Push(2);
        Assert.Equal(1, Volatile.Read(ref count));
    }
}
