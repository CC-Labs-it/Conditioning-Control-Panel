using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>RemoteControlService.cs:1597 through the relay and the Avalonia session host.</summary>
public sealed class RemoteSessionControlTests
{
    private sealed class RelayHandler : HttpMessageHandler
    {
        public string Poll = "{}";
        public readonly List<JObject> Status = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v2/remote/status") Status.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(ct)));
            var body = path == "/v2/remote/start" ? "{\"code\":\"ABC123\"}" : path == "/v2/remote/poll" ? Poll : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public Task Controller_stop_ends_the_running_session() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        const string action = "stop_session";
        var saved = (CoreDispatch.InvokeProvider, AvApp.Sessions);
        var runner = new SessionRunner(new SessionLogService());
        var s = CoreSettings.Current;
        var previousFrequency = s.FlashFrequency;
        var handler = new RelayHandler();
        using var relay = new RemoteRelay(() => "token", () => "user", "1", RemoteControlTabView.ExecuteRemoteCommand,
            RemoteCommands.StopEffects, handler) { AutoPoll = false };
        // Relay's production dispatcher hops to the UI; this test's handler returns synchronously.
        try
        {
            AvApp.Sessions = runner;
            CoreDispatch.InvokeProvider = (f, _) => (true, f());
            runner.Start(new Session { Id = "remote-test", Name = "Remote test", DurationMinutes = 30,
                Settings = new SessionSettings { FlashEnabled = true, FlashPerHour = 37, SubliminalEnabled = false } });
            Assert.True(runner.IsRunning && CoreEngine.IsRunning);
            Assert.Equal(37, s.FlashFrequency);
            await relay.StartAsync("full");
            handler.Poll = $"{{\"controller_connected\":true,\"commands\":[{{\"id\":\"1\",\"action\":\"{action}\"}}]}}";
            await relay.PollOnceAsync();
            Assert.Equal("ok", (string?)handler.Status[^1]["last_executed"]!["status"]);
            Assert.False(runner.IsRunning || CoreEngine.IsRunning || CoreFlash.IsRunning);
            Assert.Equal(previousFrequency, s.FlashFrequency);
            Assert.Null(RemoteControlTabView.ExecuteRemoteCommand(action, null));
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteControlTabView.ExecuteRemoteCommand("resume_session", null));
        }
        finally
        {
            try { runner.Stop(); CoreEngine.Stop(); }
            finally { (CoreDispatch.InvokeProvider, AvApp.Sessions) = saved; }
        }
    });

    [Fact]
    public Task Stop_also_stops_an_engine_without_a_session() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var saved = AvApp.Sessions;
        try
        {
            AvApp.Sessions = null;
            CoreEngine.Start();
            Assert.True(CoreEngine.IsRunning);
            Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("stop_session", null));
            Assert.False(CoreEngine.IsRunning);
        }
        finally
        {
            try { CoreEngine.Stop(); }
            finally { AvApp.Sessions = saved; }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Stop_invalidates_a_start_waiting_for_the_portal() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var saved = AvApp.Sessions;
        try
        {
            AvApp.Sessions = null;
            CoreEngine.Stop();
            var field = typeof(MainShellWindow).GetField("_engineGen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var before = (int)field.GetValue(null)!;
            Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("stop_session", null));
            Assert.NotEqual(before, (int)field.GetValue(null)!); // StartEffect's queued callback compares this generation
        }
        finally { AvApp.Sessions = saved; }
        return Task.CompletedTask;
    });
}
