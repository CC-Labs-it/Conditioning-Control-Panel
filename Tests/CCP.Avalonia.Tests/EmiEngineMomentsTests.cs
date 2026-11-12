using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class EmiEngineMomentsTests
{
    [Fact]
    public Task StartStopAndSessionSendTheirContext() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = new MainShellWindow();
        var oldHook = CoreEngine.StoppedHook;
        var oldSessions = AvApp.Sessions;
        var runner = new SessionRunner(new SessionLogService());
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var events = new List<(string Name, object Context)>();
        shell.EmiNow = () => now;
        shell.EmiMoment = (name, context) => events.Add((name, context));
        try
        {
            AvApp.Sessions = runner;
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.StartEngine(systemInitiated: true);
            Assert.Equal("engineStarted", Assert.Single(events).Name);
            Assert.Equal(true, events[0].Context.GetType().GetProperty("systemInitiated")!.GetValue(events[0].Context));
            now = now.AddMinutes(3).AddSeconds(59);
            MainShellWindow.StopEngine();
            Assert.Equal("engineStopped", events[1].Name);
            Assert.Equal(3, events[1].Context.GetType().GetProperty("minutes")!.GetValue(events[1].Context));
            MainShellWindow.StopEngine();
            Assert.Equal(0, events[2].Context.GetType().GetProperty("minutes")!.GetValue(events[2].Context));
            events.Clear();
            shell.StartSession(new Session { Id = "emi-moment", Name = "Moment", DurationMinutes = 10 });
            Assert.Equal("engineStarted", Assert.Single(events).Name);
            Assert.Equal(false, events[0].Context.GetType().GetProperty("systemInitiated")!.GetValue(events[0].Context));
        }
        finally
        {
            CoreEngine.StoppedHook = oldHook;
            runner.Stop();
            CoreEngine.Stop();
            AvApp.Sessions = oldSessions;
            shell.Close();
        }
        return Task.CompletedTask;
    });
}
