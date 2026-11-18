using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class CrashLogTests
{
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "ccp-crash-" + Guid.NewGuid());

    [Fact]
    public async Task DispatcherFaultIsHandledRedactedAndShowsOnlyOneDialog()
    {
        var dir = NewDir();
        var dialogs = 0;
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                var log = new CrashLog(dir, "1", _ => dialogs++);
                Dispatcher.UIThread.UnhandledException += log.DispatcherFault;
                try
                {
                    for (var i = 0; i < 2; i++)
                    {
                        var done = new TaskCompletionSource<bool>();
                        void Check(object? _, DispatcherUnhandledExceptionEventArgs e) => done.TrySetResult(e.Handled);
                        Dispatcher.UIThread.UnhandledException += Check;
                        try
                        {
                            Dispatcher.UIThread.Post(() => throw new InvalidOperationException("/home/alice/private"));
                            Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                        }
                        finally { Dispatcher.UIThread.UnhandledException -= Check; }
                    }
                    Assert.Equal(1, dialogs);
                    var text = File.ReadAllText(Path.Combine(dir, "crash.log"));
                    Assert.DoesNotContain("/home/alice", text);
                    Assert.Equal(2, text.Split("CRASH REPORT").Length - 1);
                }
                finally { Dispatcher.UIThread.UnhandledException -= log.DispatcherFault; }
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task PreviouslyHandledFaultIsLeftAlone()
    {
        var dir = NewDir();
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                var dialogs = 0;
                var log = new CrashLog(dir, "1", _ => dialogs++);
                void Filter(object? _, DispatcherUnhandledExceptionEventArgs e) => e.Handled = true;
                Dispatcher.UIThread.UnhandledException += Filter;
                Dispatcher.UIThread.UnhandledException += log.DispatcherFault;
                var done = new TaskCompletionSource<bool>();
                void Check(object? _, DispatcherUnhandledExceptionEventArgs e) => done.TrySetResult(e.Handled);
                Dispatcher.UIThread.UnhandledException += Check;
                try
                {
                    Dispatcher.UIThread.Post(() => throw new InvalidOperationException("filtered"));
                    Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                    Assert.Equal(0, dialogs);
                    Assert.False(File.Exists(Path.Combine(dir, "crash.log")));
                }
                finally
                {
                    Dispatcher.UIThread.UnhandledException -= Check;
                    Dispatcher.UIThread.UnhandledException -= Filter;
                    Dispatcher.UIThread.UnhandledException -= log.DispatcherFault;
                }
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DispatcherTestsRestoreExistingCrashState()
    {
        var dir = NewDir();
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                var previous = CrashLog.Current;
                var dialogs = 0;
                var sentinel = new CrashLog(dir, "1", _ => dialogs++);
                CrashLog.Current = sentinel;
                try
                {
                    await DispatcherFaultIsHandledRedactedAndShowsOnlyOneDialog();
                    await PreviouslyHandledFaultIsLeftAlone();
                    Assert.Same(sentinel, CrashLog.Current);
                    var done = new TaskCompletionSource<bool>();
                    void Guard(object? _, DispatcherUnhandledExceptionEventArgs e)
                    { var wasHandled = e.Handled; e.Handled = true; done.TrySetResult(wasHandled); }
                    Dispatcher.UIThread.UnhandledException += Guard;
                    try
                    {
                        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("sentinel"));
                        Assert.False(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                        Assert.Equal(0, dialogs);
                        Assert.False(File.Exists(Path.Combine(dir, "crash.log")));
                    }
                    finally { Dispatcher.UIThread.UnhandledException -= Guard; }
                }
                finally { CrashLog.Current = previous; }
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private sealed class RenderFailure : Exception
    {
        internal RenderFailure() { HResult = unchecked((int)0x88980406); }
    }

    [Fact]
    public void RecognizesWpfRenderFailureSignatures()
    {
        Assert.True(CrashLog.IsRenderFailure(new OutOfMemoryException()));
        Assert.True(CrashLog.IsRenderFailure(new RenderFailure()));
        Assert.True(CrashLog.IsRenderFailure(new Exception("UCEERR_CHANNEL")));
        Assert.True(CrashLog.IsRenderFailure(new Exception("error 0x88980406")));
        Assert.False(CrashLog.IsRenderFailure(new Exception("render failed")));
    }

    [Fact]
    public async Task FatalDispatcherFaultExitsOnceAndNeverShowsDialog()
    {
        var dir = NewDir();
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                var exits = 0;
                var dialogs = 0;
                var log = new CrashLog(dir, "1", _ => dialogs++, () => exits++);
                Dispatcher.UIThread.UnhandledException += log.DispatcherFault;
                try
                {
                    foreach (var error in new Exception[] { new OutOfMemoryException(), new Exception("ordinary"), new RenderFailure() })
                    {
                        var done = new TaskCompletionSource<bool>();
                        void Check(object? _, DispatcherUnhandledExceptionEventArgs e) => done.TrySetResult(e.Handled);
                        Dispatcher.UIThread.UnhandledException += Check;
                        try
                        {
                            Dispatcher.UIThread.Post(() => throw error);
                            Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                        }
                        finally { Dispatcher.UIThread.UnhandledException -= Check; }
                    }
                    Assert.Equal(1, exits);
                    Assert.Equal(0, dialogs);
                    Assert.Equal(3, File.ReadAllText(Path.Combine(dir, "crash.log")).Split("CRASH REPORT").Length - 1);
                }
                finally { Dispatcher.UIThread.UnhandledException -= log.DispatcherFault; }
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(1816, "quota 1816")]
    [InlineData(1450, "quota 1450")]
    [InlineData(42, "Not enough quota UCEERR_test")]
    public async Task QuotaFaultsAreLoggedHandledAndDoNotConsumeTheDialog(int code, string message)
    {
        var dir = NewDir();
        try
        {
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                var dialogs = 0;
                var exits = 0;
                var log = new CrashLog(dir, "1", _ => dialogs++, () => exits++);
                Dispatcher.UIThread.UnhandledException += log.DispatcherFault;
                try
                {
                    var faults = new Exception[]
                    {
                        new System.ComponentModel.Win32Exception(code, message),
                        code == 42 ? new InvalidOperationException("Not enough quota") :
                            new System.ComponentModel.Win32Exception(42, "other error")
                    };
                    for (var i = 0; i < faults.Length; i++)
                    {
                        var done = new TaskCompletionSource<bool>();
                        void Check(object? _, DispatcherUnhandledExceptionEventArgs e) => done.TrySetResult(e.Handled);
                        Dispatcher.UIThread.UnhandledException += Check;
                        try
                        {
                            var error = faults[i];
                            Dispatcher.UIThread.Post(() => throw error);
                            Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                            Assert.Equal(i == 1 ? 1 : 0, dialogs);
                            Assert.Equal(0, exits);
                        }
                        finally { Dispatcher.UIThread.UnhandledException -= Check; }
                    }
                    var text = File.ReadAllText(Path.Combine(dir, "crash.log"));
                    Assert.Equal(2, text.Split("CRASH REPORT").Length - 1);
                    Assert.Contains(message, text);
                }
                finally { Dispatcher.UIThread.UnhandledException -= log.DispatcherFault; }
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void FatalExitUsesBackgroundThreadsAndBackstopWhenExitHangs()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var exited = new ManualResetEventSlim();
        using var terminated = new ManualResetEventSlim();
        var callerThread = Environment.CurrentManagedThreadId;
        var exitThread = 0;
        var backstopThread = 0;
        try
        {
            CrashLog.BeginExit(() =>
            {
                exitThread = Environment.CurrentManagedThreadId;
                entered.Set();
                release.Wait();
                exited.Set();
            }, () => { backstopThread = Environment.CurrentManagedThreadId; terminated.Set(); }, 100);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(callerThread, exitThread);
            Assert.True(terminated.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(callerThread, backstopThread);
            Assert.NotEqual(exitThread, backstopThread);
        }
        finally
        {
            release.Set();
            Assert.True(exited.Wait(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void RepeatedSignatureHasFiveReportsAndOneSuppression()
    {
        var dir = NewDir();
        try
        {
            var log = new CrashLog(dir, "1", _ => { });
            var error = new Exception("same");
            for (var i = 0; i < 20; i++) log.Write("TASK", error);
            var text = File.ReadAllText(Path.Combine(dir, "crash.log"));
            Assert.Equal(5, text.Split("CRASH REPORT").Length - 1);
            Assert.Equal(1, text.Split("suppressed").Length - 1);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void DomainFaultLogsAndFlushesOnlyWhenTerminating()
    {
        var dir = NewDir();
        try
        {
            var log = new CrashLog(dir, "1", _ => { });
            var flushes = 0;
            log.DomainFault(new UnhandledExceptionEventArgs(new Exception("domain fault"), false), () => flushes++);
            Assert.Equal(0, flushes);
            log.DomainFault(new UnhandledExceptionEventArgs(new Exception("domain fault"), true), () => flushes++);
            Assert.Equal(1, flushes);
            Assert.Equal(2, File.ReadAllText(Path.Combine(dir, "crash.log")).Split("Source: DOMAIN").Length - 1);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TaskFaultIsObservedAndLateSocketCloseIsIgnored()
    {
        var dir = NewDir();
        try
        {
            var log = new CrashLog(dir, "1", _ => { });
            var error = new UnobservedTaskExceptionEventArgs(new AggregateException(new Exception("task failed")));
            log.TaskFault(null, error);
            Assert.True(error.Observed);
            Assert.Contains("task failed", File.ReadAllText(Path.Combine(dir, "crash.log")));
            var before = File.ReadAllText(Path.Combine(dir, "crash.log"));
            Exception close;
            try { FakeWebSocket.CloseAsync(); throw new Exception(); } catch (WebSocketException ex) { close = ex; }
            var late = new UnobservedTaskExceptionEventArgs(new AggregateException(close));
            log.TaskFault(null, late);
            Assert.True(late.Observed);
            Assert.Equal(before, File.ReadAllText(Path.Combine(dir, "crash.log")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void RuntimeUnobservedTaskEventIsObservedAndLogged()
    {
        var dir = NewDir();
        var log = new CrashLog(dir, "1", _ => { });
        bool? observed = null;
        void Check(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.InnerExceptions.Any(x => x.Message == "unobserved crash test")) observed = e.Observed;
        }
        TaskScheduler.UnobservedTaskException += log.TaskFault;
        TaskScheduler.UnobservedTaskException += Check;
        try
        {
            MakeUnobservedFault();
            for (int i = 0; i < 10 && observed is null; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.True(observed);
            Assert.Contains("unobserved crash test", File.ReadAllText(Path.Combine(dir, "crash.log")));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= log.TaskFault;
            TaskScheduler.UnobservedTaskException -= Check;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void MakeUnobservedFault() => _ = Task.FromException(new Exception("unobserved crash test"));

    private static class FakeWebSocket
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static void CloseAsync() => throw new WebSocketException(WebSocketError.InvalidState);
    }

    [Fact]
    public void RotationUsesVersionAndSize()
    {
        var dir = NewDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "crash.log");
            var marker = path + ".version";
            File.WriteAllText(marker, "1");
            File.WriteAllText(path, "old");
            new CrashLog(dir, "1", _ => { }).Rotate();
            Assert.Equal("old", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".prev"));
            new CrashLog(dir, "2", _ => { }).Rotate();
            Assert.Equal("old", File.ReadAllText(path + ".prev"));
            Assert.False(File.Exists(path));
            File.WriteAllText(path, new string('x', 512 * 1024 + 1));
            new CrashLog(dir, "2", _ => { }).Rotate();
            Assert.Equal(512 * 1024 + 1, new FileInfo(path + ".prev").Length);
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
