using System;
using System.Threading;
using ConditioningControlPanel.Services.Reset;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF baf86f0ac: the factory reset leaves off the UI thread; a wedged exit is still terminated.</summary>
public sealed class UiThreadExitTests
{
    [Fact]
    public void Begin_ReturnsAtOnce_AndExitsOnAnotherThread_EvenWhenTheExitHangs()
    {
        int callerThread = Environment.CurrentManagedThreadId;
        int exitThread = -1;
        using var exitEntered = new ManualResetEventSlim();
        using var neverReleased = new ManualResetEventSlim();
        using var terminated = new ManualResetEventSlim();
        int terminatedWith = -1;

        var started = DateTime.UtcNow;
        UiThreadExit.Begin(1,
            exit: code =>
            {
                exitThread = Environment.CurrentManagedThreadId;
                exitEntered.Set();
                neverReleased.Wait(TimeSpan.FromSeconds(10));   // a ProcessExit handler that wedges
            },
            terminate: code => { terminatedWith = code; terminated.Set(); },
            graceMs: 200);
        var returnedAfter = DateTime.UtcNow - started;

        Assert.True(returnedAfter < TimeSpan.FromSeconds(1), $"Begin blocked the caller for {returnedAfter}");
        Assert.True(exitEntered.Wait(TimeSpan.FromSeconds(5)), "the exit never started");
        Assert.NotEqual(callerThread, exitThread);
        Assert.True(terminated.Wait(TimeSpan.FromSeconds(5)), "the backstop never terminated a wedged exit");
        Assert.Equal(1, terminatedWith);
        neverReleased.Set();
    }

    [Fact]
    public void Backstop_FlushesBeforeItTerminates()
    {
        int order = 0, flushedAt = 0, terminatedAt = 0;
        using var done = new ManualResetEventSlim();
        UiThreadExit.Begin(2,
            beforeTerminate: () => flushedAt = Interlocked.Increment(ref order),
            exit: _ => { },
            terminate: _ => { terminatedAt = Interlocked.Increment(ref order); done.Set(); },
            graceMs: 0);

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, flushedAt);
        Assert.Equal(2, terminatedAt);
    }
}
