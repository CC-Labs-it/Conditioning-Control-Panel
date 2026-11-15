using System;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
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
                CrashLog.Current = log;
                CrashLog.RegisterDispatcherLast();
                try
                {
                    for (var i = 0; i < 2; i++)
                    {
                        var done = new TaskCompletionSource<bool>();
                        void Check(object? _, DispatcherUnhandledExceptionEventArgs e) => done.TrySetResult(e.Handled);
                        Dispatcher.UIThread.UnhandledException += Check;
                        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("/home/alice/private"));
                        Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                        Dispatcher.UIThread.UnhandledException -= Check;
                    }
                    Assert.Equal(1, dialogs);
                    var text = File.ReadAllText(Path.Combine(dir, "crash.log"));
                    Assert.DoesNotContain("/home/alice", text);
                    Assert.Equal(2, text.Split("CRASH REPORT").Length - 1);
                }
                finally { CrashLog.Current = null; }
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
                CrashLog.Current = log;
                CrashLog.RegisterDispatcherLast();
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
                    CrashLog.Current = null;
                }
            });
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
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
