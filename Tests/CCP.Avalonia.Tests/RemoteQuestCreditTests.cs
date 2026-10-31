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

public sealed class RemoteQuestCreditTests
{
    private sealed class Server : HttpMessageHandler
    {
        public string Poll = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("/start") ? "{\"code\":\"ABC123\"}" : "{}";
            if (request.RequestUri.AbsolutePath.EndsWith("/poll")) { body = Poll; Poll = "{}"; }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public Task OnlyExecutedCommandsCreditTheCurrentBoardWithoutAShell() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var previous = AvApp.Quests;
        var dir = Directory.CreateTempSubdirectory("ccp-remote-quest-").FullName;
        using var quests = new QuestService(null, dir);
        using var server = new Server();
        using var relay = new RemoteRelay(() => "token", () => "subject", "test",
            (action, _) => action == "show_spiral" ? "refused" : null, _ => { }, server) { AutoPoll = false };
        try
        {
            AvApp.Quests = null;
            Assert.Same(relay, AvApp.WireRemoteQuestProgress(relay));
            Assert.Equal("ABC123", await relay.StartAsync("light"));
            AvApp.Quests = quests; // resolve live board, not the null board at subscription time
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("handed_over_d") };
            server.Poll = "{\"commands\":[{\"id\":\"1\",\"action\":\"trigger_flash\"},{\"id\":\"2\",\"action\":\"show_spiral\"}]}";
            await relay.PollOnceAsync();
            Assert.Equal(1, quests.Progress.DailyQuests[0].CurrentProgress); // quiet accepted verb counts, refusal does not
            await relay.PollOnceAsync();
            Assert.Equal(1, quests.Progress.DailyQuests[0].CurrentProgress);
        }
        finally { AvApp.Quests = previous; quests.Dispose(); Directory.Delete(dir, true); }
    });
}
