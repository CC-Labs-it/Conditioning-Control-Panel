using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;
using Program = ConditioningControlPanel.Avalonia.Program;

namespace CCP.Avalonia.Tests;

/// <summary>ccp-bugs #1704: a teardown step that hangs on exit froze the app for good, and every SIGTERM
/// was swallowed, so only kill -9 ended it. WPF ends OnExit with TerminateProcess (App.xaml.cs:6740-6751).</summary>
public sealed class ExitBackstopTests
{
    [Fact]
    public void HangingExitStep_IsKilledByTheBackstop_AndTheLogNamesTheStep()
    {
        var oldLogger = Serilog.Log.Logger;
        var sink = new Capture();
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var terminated = new ManualResetEventSlim();
        try
        {
            App.ExitStep("libvlc teardown");
            App.ArmExitBackstop(terminate: _ => terminated.Set(), graceMs: 100);
            // The UI thread stays stuck in that step; nothing else ends the process.
            Assert.True(terminated.Wait(TimeSpan.FromSeconds(5)), "a hung exit was never terminated");
            Assert.Contains("libvlc teardown", sink.Message);
        }
        finally
        {
            App.ExitStep("start");
            Serilog.Log.Logger = oldLogger;
        }
    }

    [Fact]
    public void SecondSigterm_IsNotCancelled()
    {
        try
        {
            var first = new PosixSignalContext(PosixSignal.SIGTERM);
            Program.OnSigterm(first);
            Assert.True(first.Cancel, "the first SIGTERM shuts down through the lifetime so settings flush");
            var second = new PosixSignalContext(PosixSignal.SIGTERM);
            Program.OnSigterm(second);
            Assert.False(second.Cancel, "a second SIGTERM was swallowed: terminate never ends a stuck exit");
        }
        finally { Program.ResetSigterm(); }
    }

    [Fact]
    public void OnDesktopExit_SavesAndArmsTheBackstopBeforeTheTeardownThatCanHang()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs"));
        var exit = src[src.IndexOf("private void OnDesktopExit", StringComparison.Ordinal)..];
        int stop = exit.IndexOf("ShutdownStop()", StringComparison.Ordinal);
        int save = exit.IndexOf("SaveSettingsOnExit(", StringComparison.Ordinal);
        int achievements = exit.IndexOf("SaveAchievementsOnExit(", StringComparison.Ordinal);
        int arm = exit.IndexOf("ArmExitBackstop()", StringComparison.Ordinal);
        int libvlc = exit.IndexOf("CoreEngine.Video?.Dispose()", StringComparison.Ordinal);
        Assert.True(stop > 0 && stop < save && save < libvlc && achievements < libvlc, "save after toys stop, before libvlc teardown");
        Assert.True(stop < arm && arm < libvlc, "toys stop before the backstop can kill; it is armed before the teardown");
    }

    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "CCP.Avalonia", "App.axaml.cs"))) dir = Path.GetDirectoryName(dir)!;
        return dir;
    }

    private sealed class Capture : ILogEventSink
    {
        public string Message = "";
        public void Emit(LogEvent logEvent) { lock (this) Message += logEvent.RenderMessage(); }
    }
}
