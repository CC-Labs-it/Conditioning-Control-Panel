using System;
using System.IO;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Serilog.Core;
using Serilog.Events;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class CrashHandlersTests
{
    [Fact]
    public void EngineMarkerTracksStopAndIsConsumedAtStartup() => AvaloniaTestDispatcher.Run(() =>
    {
        var path = Path.Combine(CorePaths.UserData, "logs", "engine_session.active");
        var oldLogger = Serilog.Log.Logger;
        var sink = new Capture();
        Serilog.Log.Logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        var shell = new MainShellWindow();
        try
        {
            EngineCrashSentinel.Clear();
            shell.OnEngineStarted();
            Assert.True(File.Exists(path));
            shell.OnEngineStopped();
            Assert.False(File.Exists(path));
            shell.OnEngineStarted();
            Assert.True(File.Exists(path));
            App.RecoverEngineCrash();
            Assert.False(File.Exists(path));
            Assert.Contains("[ENGINECRASH]", sink.Message);
            sink.Message = "";
            App.RecoverEngineCrash();
            Assert.Equal("", sink.Message);
        }
        finally
        {
            EngineCrashSentinel.Clear();
            shell.Close();
            Serilog.Log.Logger = oldLogger;
        }
    });

    private sealed class Capture : ILogEventSink
    {
        public string Message = "";
        public void Emit(LogEvent logEvent) => Message += logEvent.RenderMessage();
    }
}
