using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Real producers, fake relay, sandbox profile; no windows or physical effects.</summary>
public sealed class QuestProducerTests
{
    private sealed class RelayServer : HttpMessageHandler
    {
        internal string Poll = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/start") ? "{\"code\":\"ABC123\"}" : "{}";
            if (path.EndsWith("/poll"))
            {
                body = Poll;
                Poll = "{}"; // the real relay dequeues delivered commands on poll
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public Task CompletedSessionsAndAcceptedRemoteCommandsCreditTheBoardWithoutATab() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldQuests = AvApp.Quests;
        var oldSettings = CoreSettings.ServiceProvider;
        var oldRunning = CoreSession.IsSessionRunningProvider;
        var oldBonus = CoreQuests.CheckPerfectWeekBonusProvider;
        var oldEffects = CoreQuests.PlayCompletionEffectsProvider;
        var oldXp = CoreProgression.AddXPProvider;
        CoreQuests.CheckPerfectWeekBonusProvider = null;
        CoreQuests.PlayCompletionEffectsProvider = null;
        CoreProgression.AddXPProvider = (_, _) => { };
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var dir = Directory.CreateTempSubdirectory("ccp-quest-producers-").FullName;
        var logs = new SessionLogService();
        using var quests = new QuestService(null, dir);
        using var server = new RelayServer();
        using var relay = new RemoteRelay(() => "token", () => "subject", "test", (action, _) =>
            action == "show_spiral" ? "refused" : null, _ => { }, server) { AutoPoll = false };
        var runner = new SessionRunner(logs);
        try
        {
            AvApp.Quests = quests;
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("daily_devotion_d"), new("handed_over_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("streak_keeper_w");
            AvApp.WireQuestProgress(runner, relay, loginStreak: 3);
            Assert.Equal(3, quests.Progress.WeeklyQuest.CurrentProgress);
            quests.Progress.WeeklyQuest = new ActiveQuest("weekly_devotion_w");
            var session = new Session { Id = "quest-producer-test", Name = "Quest test", DurationMinutes = 1,
                Settings = new SessionSettings() };
            runner.Start(session);
            runner.Stop(completed: false);
            Assert.Equal(0, quests.Progress.WeeklyQuest.CurrentProgress);
            runner.Start(session);
            runner.Tick(TimeSpan.FromMinutes(1));
            Assert.True(quests.Progress.DailyQuests[0]!.IsCompleted);
            Assert.Equal(1, quests.Progress.WeeklyQuest.CurrentProgress);
            runner.Stop(completed: true); // stopped runner cannot pay twice
            Assert.Equal(1, quests.Progress.WeeklyQuest.CurrentProgress);

            Assert.Equal("ABC123", await relay.StartAsync("light"));
            server.Poll = "{\"commands\":[{\"id\":\"1\",\"action\":\"trigger_flash\"},{\"id\":\"2\",\"action\":\"show_spiral\"}]}";
            await relay.PollOnceAsync();
            Assert.Equal(1, quests.Progress.DailyQuests[1]!.CurrentProgress); // quiet accepted verb counts, refusal does not
            await relay.PollOnceAsync();
            Assert.Equal(1, quests.Progress.DailyQuests[1]!.CurrentProgress); // subsequent empty polls earn nothing
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            AvApp.Quests = oldQuests;
            CoreSettings.ServiceProvider = oldSettings;
            CoreSession.IsSessionRunningProvider = oldRunning;
            CoreQuests.CheckPerfectWeekBonusProvider = oldBonus;
            CoreQuests.PlayCompletionEffectsProvider = oldEffects;
            CoreProgression.AddXPProvider = oldXp;
            foreach (var file in Directory.GetFiles(logs.LogsFolder, "*_quest-producer-test.json")) File.Delete(file);
            Directory.Delete(dir, true);
        }
    });
}
