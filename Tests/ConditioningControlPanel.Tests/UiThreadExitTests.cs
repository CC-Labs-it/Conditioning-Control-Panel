using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.Diagnostics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1458 / #1468 / #1469 / #1471 / #1472 (one 7.1.9 hang report): the render-failure exit
/// ran Environment.Exit on the UI thread, ProcessExit's SystemEvents.Shutdown waited on a Send to
/// that same thread, and the app froze instead of leaving.
/// </summary>
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
        int callerThread = -1, exitThread = -1, terminatedWith = -1;
        using var exitEntered = new ManualResetEventSlim();
        using var releaseExit = new ManualResetEventSlim();
        using var exitFinished = new ManualResetEventSlim();
        using var beginReturned = new ManualResetEventSlim();
        using var terminated = new ManualResetEventSlim();
        Exception? beginError = null;

        // Test the dependency, not a wall-clock benchmark of Thread.Start/JIT/GC/scheduling.
        // The exit cannot finish until AFTER we prove Begin returned and the backstop ran.
        // Unlike a timed fake exit, this cannot pass merely because the fake stopped hanging.
        var caller = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            try
            {
                UiThreadExit.Begin(1,
                    exit: code =>
                    {
                        exitThread = Environment.CurrentManagedThreadId;
                        exitEntered.Set();
                        releaseExit.Wait();
                        exitFinished.Set();
                    },
                    terminate: code => { terminatedWith = code; terminated.Set(); },
                    graceMs: 200);
            }
            catch (Exception ex) { beginError = ex; }
            finally { beginReturned.Set(); }
        }) { IsBackground = true };
        caller.Start();
        try
        {
            Assert.True(exitEntered.Wait(TimeSpan.FromSeconds(5)), "the exit never started");
            Assert.True(beginReturned.Wait(TimeSpan.FromSeconds(5)), "Begin waited for the wedged exit");
            Assert.Null(beginError);
            Assert.NotEqual(callerThread, exitThread);
            Assert.False(exitFinished.IsSet, "the fake exit must still be blocked when Begin returns");
            Assert.True(terminated.Wait(TimeSpan.FromSeconds(5)), "the backstop never terminated a wedged exit");
            Assert.Equal(1, terminatedWith);
            Assert.False(exitFinished.IsSet, "the backstop must run independently of the wedged exit");
        }
        finally
        {
            // Even a failing assertion must release and drain the workers before disposing signals.
            releaseExit.Set();
            Assert.True(caller.Join(TimeSpan.FromSeconds(5)), "the caller did not finish after cleanup released the exit");
            Assert.True(exitFinished.Wait(TimeSpan.FromSeconds(5)), "the exit did not finish after cleanup");
            Assert.True(terminated.Wait(TimeSpan.FromSeconds(5)), "the backstop did not finish before cleanup");
        }
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
