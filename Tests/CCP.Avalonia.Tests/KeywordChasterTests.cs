using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class KeywordChasterTests
{
    private sealed class Tokens : IChasterTokenStore
    {
        public ChasterStoredTokens? Read() => new("test", "test", DateTime.UtcNow.AddDays(1));
        public void Write(ChasterStoredTokens tokens) { }
        public void Clear() { }
    }

    [Fact]
    public Task PerTriggerCapCountsOnlySuccessfulBookingsAndSurvivesReload() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var saved = ChasterHead.Service;
        var path = Path.Combine(ConditioningControlPanel.CorePaths.UserData, "keyword-test-tab.json");
        var options = new ChasterOptions(false, "test-lock", new HashSet<string> { "watcher" });
        // NoteSeconds is local accounting. No request leaves this service in this test.
        using var service = new ChasterService(new ChasterClient(new HttpClientHandler()), new Tokens(), path, () => options);
        var trigger = new KeywordTrigger { Id = "keyword-test-" + Guid.NewGuid().ToString("N") };
        try
        {
            ChasterHead.Service = service;
            Assert.Equal(0, KeywordResponses.Book(5, trigger));
            options = options with { TabEnabled = true };
            Assert.Equal(300, KeywordResponses.Book(5, trigger));
            Assert.Equal(300, KeywordResponses.Book(5, trigger));
            Assert.Equal(300, KeywordResponses.Book(60, trigger));
            Assert.Equal(0, KeywordResponses.Book(5, trigger));
            Assert.Equal(900, service.BalanceSeconds);
            var caps = ConditioningControlPanel.Services.KeywordTriggerChasterCap.Load(
                Path.Combine(ConditioningControlPanel.CorePaths.UserData, "chaster_trigger_caps.json"));
            Assert.Equal(0, caps.Allowance(trigger.Id, 300, DateTime.Now));
            Assert.Equal(900, caps.State[trigger.Id].Seconds);
            options = options with { Prices = new HashSet<string>() };
            Assert.Equal(0, KeywordResponses.Book(5, new KeywordTrigger()));
        }
        finally { ChasterHead.Service = saved; File.Delete(path); }
        return Task.CompletedTask;
    });
}
