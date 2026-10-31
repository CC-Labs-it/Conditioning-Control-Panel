using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.Diagnostics;
using Xunit;

namespace ConditioningControlPanel.Tests;

// Native Thread.Start latency is measured here. Other suites create many STA/render threads;
// run this safety timing contract without that unrelated parallel load, at the same 1 s limit.
[CollectionDefinition(nameof(UiThreadExitTests), DisableParallelization = true)]
public sealed class UiThreadExitCollection { }

/// <summary>
/// ccp-bugs #1458 / #1468 / #1469 / #1471 / #1472 (one 7.1.9 hang report): the render-failure exit
/// ran Environment.Exit on the UI thread, ProcessExit's SystemEvents.Shutdown waited on a Send to
/// that same thread, and the app froze instead of leaving.
/// </summary>
[Collection(nameof(UiThreadExitTests))]
public sealed class UiThreadExitTests
{
    [Fact]
    public void RenderThreadFailureHResult_IsARenderFailure()
    {
        var ex = new COMException("UCEERR", unchecked((int)0x88980406));
        Assert.True(UiThreadExit.IsRenderFailure(ex));
    }

    [Fact]
    public void RenderMessageAndOom_AreRenderFailures()
    {
        Assert.True(UiThreadExit.IsRenderFailure(new InvalidOperationException("UCEERR_RENDERTHREADFAILURE")));
        Assert.True(UiThreadExit.IsRenderFailure(new InvalidOperationException("failed with 0x88980406")));
        Assert.True(UiThreadExit.IsRenderFailure(new OutOfMemoryException()));
    }

    [Fact]
    public void OrdinaryExceptions_AreNotRenderFailures()
    {
        Assert.False(UiThreadExit.IsRenderFailure(null));
        Assert.False(UiThreadExit.IsRenderFailure(new InvalidOperationException("Collection was modified")));
        Assert.False(UiThreadExit.IsRenderFailure(new IndexOutOfRangeException()));
    }

    [Fact]
    public void Begin_ReturnsAtOnce_AndExitsOnAnotherThread_EvenWhenTheExitHangs()
    {
        int callerThread = Environment.CurrentManagedThreadId;
        int exitThread = -1;
        using var exitEntered = new ManualResetEventSlim();
        using var neverReleased = new ManualResetEventSlim();
        using var terminated = new ManualResetEventSlim();
        int terminatedWith = -1;

        var started = Stopwatch.StartNew();
        UiThreadExit.Begin(1,
            exit: code =>
            {
                exitThread = Environment.CurrentManagedThreadId;
                exitEntered.Set();
                neverReleased.Wait(TimeSpan.FromSeconds(10));   // a ProcessExit handler that wedges
            },
            terminate: code => { terminatedWith = code; terminated.Set(); },
            graceMs: 200);
        var returnedAfter = started.Elapsed;

        try
        {
            Assert.True(returnedAfter < TimeSpan.FromSeconds(1), $"Begin blocked the caller for {returnedAfter}");
            Assert.True(exitEntered.Wait(TimeSpan.FromSeconds(5)), "the exit never started");
            Assert.NotEqual(callerThread, exitThread);
            Assert.True(terminated.Wait(TimeSpan.FromSeconds(5)), "the backstop never terminated a wedged exit");
            Assert.Equal(1, terminatedWith);
        }
        finally { neverReleased.Set(); } // also release the worker when an assertion fails
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

    [Fact]
    public void DispatcherHandler_NeverCallsEnvironmentExitOnTheUiThread()
    {
        var app = File.ReadAllText(Path.Combine(SourceRoot(), "App.xaml.cs"));
        int start = app.IndexOf("DispatcherUnhandledException += (s, args) =>", StringComparison.Ordinal);
        Assert.True(start >= 0, "dispatcher handler not found");
        int end = app.IndexOf("AppDomain.CurrentDomain.UnhandledException +=", start, StringComparison.Ordinal);
        Assert.True(end > start, "dispatcher handler end not found");
        var handler = app.Substring(start, end - start);

        Assert.DoesNotContain("Environment.Exit(", handler);
        Assert.Contains("UiThreadExit.IsRenderFailure(", handler);
        Assert.Contains("UiThreadExit.Begin(", handler);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
