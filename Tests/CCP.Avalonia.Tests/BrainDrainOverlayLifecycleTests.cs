using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class BrainDrainOverlayLifecycleTests
{
    private sealed class Server : HttpMessageHandler
    {
        public string Poll = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/start") ? "{\"code\":\"ABC123\"}" : path.EndsWith("/poll") ? Poll : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static void Step(int first, int last)
    {
        for (var second = first; second <= last; second++)
            App.SampleBrainDrainQuestMinutes(TimeSpan.FromSeconds(second));
    }

    [Fact]
    public void PausedSessionDoesNotCreditAndResumeRestartsSampling() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldQuests = App.Quests;
        var oldSessions = App.Sessions;
        var oldRunning = CoreSession.IsSessionRunningProvider;
        var oldPaused = CoreSession.IsSessionPausedProvider;
        var oldStart = CoreEngine.StartBrainDrain;
        var oldStop = CoreEngine.StopBrainDrain;
        var dir = Directory.CreateTempSubdirectory("ccp-brain-pause-").FullName;
        using var quests = new QuestService(null, dir);
        var quest = new ActiveQuest("soft_static_d");
        quests.Progress.DailyQuests = new List<ActiveQuest> { quest };
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        App.Quests = quests;
        var runner = App.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            settings.Current.FlashEnabled = false;
            settings.Current.BrainDrainEnabled = true;
            App.WireBrainDrainQuestMinutes();
            runner.Start(new Session { Name = "test", DurationMinutes = 10 });
            settings.Current.BrainDrainEnabled = true;
            Step(0, 60);
            Assert.Equal(1, quest.CurrentProgress);
            runner.Pause();
            Step(61, 180);
            Assert.Equal(1, quest.CurrentProgress);
            runner.Resume();
            Step(181, 241);
            Assert.Equal(2, quest.CurrentProgress);
            runner.Pause();
            runner.Resume(); // A pause between two ticks must not credit the gap.
            Step(242, 301);
            Assert.Equal(2, quest.CurrentProgress);
            CoreEngine.Stop();
            Step(302, 362);
            Assert.Equal(2, quest.CurrentProgress);
            runner.Pause();
            runner.Resume(); // WPF resumes the session overlay even after the engine stopped.
            Step(363, 423);
            Assert.Equal(3, quest.CurrentProgress);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            App.StopBrainDrainQuestMinutes();
            CoreEngine.StartBrainDrain = oldStart;
            CoreEngine.StopBrainDrain = oldStop;
            CoreSession.IsSessionRunningProvider = oldRunning;
            CoreSession.IsSessionPausedProvider = oldPaused;
            CoreSettings.ServiceProvider = oldSettings;
            App.Quests = oldQuests;
            App.Sessions = oldSessions;
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]
    public Task ControllerConnectionOwnsCreditUntilStopped() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldQuests = App.Quests;
        var oldSessions = App.Sessions;
        var oldRelay = RemoteControlTabView.Relay;
        var oldStart = CoreEngine.StartBrainDrain;
        var oldStop = CoreEngine.StopBrainDrain;
        var oldAttention = MainShellWindow.RemoteJoinAttention;
        var oldSink = ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        var dir = Directory.CreateTempSubdirectory("ccp-brain-remote-").FullName;
        using var quests = new QuestService(null, dir);
        var quest = new ActiveQuest("soft_static_d");
        quests.Progress.DailyQuests = new List<ActiveQuest> { quest };
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        App.Quests = quests;
        App.Sessions = null;
        using var server = new Server();
        using var relay = new RemoteRelay(() => "token", () => "subject", "test", RemoteCommands.Execute, RemoteCommands.StopEffects, server) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        MainShellWindow.RemoteJoinAttention = _ => { };
        ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        MainShellWindow? shell = null;
        try
        {
            settings.Current.BrainDrainEnabled = true;
            settings.Current.StopEffectsOnRemoteDisconnect = false;
            App.WireBrainDrainQuestMinutes();
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Step(0, 60);
            Assert.Equal(0, quest.CurrentProgress);
            Assert.Equal("ABC123", await relay.StartAsync("light"));
            server.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreEngine.IsRunning);
            Step(61, 121);
            Assert.Equal(1, quest.CurrentProgress);
            server.Poll = "{\"controller_connected\":false}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Step(122, 181); // Default disconnect leaves the overlay running.
            Assert.Equal(2, quest.CurrentProgress);
            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            // Stop and reconnect between ticks must discard the stopped interval.
            Assert.Equal("ABC123", await relay.StartAsync("light"));
            server.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Step(240, 240);
            Assert.Equal(2, quest.CurrentProgress);
            Step(241, 301);
            Assert.Equal(3, quest.CurrentProgress);
            RemoteCommands.StopEffects(force: true);
            Step(302, 362);
            Assert.Equal(3, quest.CurrentProgress);
        }
        finally
        {
            await relay.StopAsync();
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            CoreEngine.Stop();
            App.StopBrainDrainQuestMinutes();
            CoreEngine.StartBrainDrain = oldStart;
            CoreEngine.StopBrainDrain = oldStop;
            CoreSettings.ServiceProvider = oldSettings;
            App.Quests = oldQuests;
            App.Sessions = oldSessions;
            RemoteControlTabView.Relay = oldRelay;
            MainShellWindow.RemoteJoinAttention = oldAttention;
            ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = oldSink;
            try { Directory.Delete(dir, true); } catch { }
        }
    });
    [Fact]
    public Task QueuedControllerJoinCannotUndoPanic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldRelay = RemoteControlTabView.Relay;
        var oldAttention = MainShellWindow.RemoteJoinAttention;
        var oldSink = ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        using var server = new Server();
        using var relay = new RemoteRelay(() => "token", () => "subject", "test", RemoteCommands.Execute, RemoteCommands.StopEffects, server) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        MainShellWindow.RemoteJoinAttention = _ => { };
        ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("ABC123", await relay.StartAsync("light"));
            server.Poll = "{\"controller_connected\":true}";
            var pending = relay.PollOnceAsync();
            Assert.True(relay.ControllerConnected); // The UI join callback is still queued.
            RemoteCommands.StopEffects(force: true);
            await pending;
            Dispatcher.UIThread.RunJobs();
            Assert.False(RemoteCommands.OverlayRunning);
            Assert.True(RemoteCommands.OverlaysStopped);
        }
        finally
        {
            await relay.StopAsync();
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            CoreEngine.Stop();
            CoreSettings.ServiceProvider = oldSettings;
            RemoteControlTabView.Relay = oldRelay;
            MainShellWindow.RemoteJoinAttention = oldAttention;
            ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = oldSink;
        }
    });

}
