using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StakeHostTests
{
    private sealed class Api : IStakeApi
    {
        internal Func<string, JObject, Task<JObject?>> Reply = (_, _) => Task.FromResult<JObject?>(new JObject { ["ok"] = true });
        internal readonly List<(string Op, JObject Body)> Calls = new();
        public string? Account() => "player";
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls.Add((op, body));
            return Reply(op, body);
        }
    }

    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var factory = GameWindow.PbpStakeFactory;
        var (oldGet, oldSet) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        CoreSettings.ServiceProvider = () => settings;
        GameWindow.ResetPbpStakesForTests();
        try { await body(); }
        finally
        {
            GameWindow.CloseAllForPanic();
            GameWindow.ResetPbpStakesForTests();
            GameWindow.PbpStakeFactory = factory;
            settings.SaveImmediate();
            CoreSettings.ServiceProvider = oldSettings;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldSet);
        }
    });

    [Fact]
    public Task PbpRoutesAllStakeVerbsAndPostsRepliesOnTheUiThread() => Run(async () =>
    {
        var api = new Api();
        GameWindow.PbpStakeFactory = post => new StakeBridge("pbp", post, api,
            new StakeSettlement((_, _) => 0, null, () => "player"), () => false, () => { });
        var w = new GameWindow(GameWindow.Games[GameWindow.PbpId]);
        var replies = new List<JObject>();
        w.Posted += json => { Assert.True(Dispatcher.UIThread.CheckAccess()); replies.Add(JObject.Parse(json)); };
        w.Show();
        foreach (var frame in new[]
        {
            "{type:'stake-limits'}",
            "{type:'stake-offer',match:'board',kind:'sp',amount:5}",
            "{type:'stake-state',match:'board'}",
        })
        {
            w.HandleMessage(frame);
            await w.StakePending;
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(new[] { "limits", "offer", "state" }, replies.Select(o => (string?)o["op"]));
        Assert.All(replies, o => { Assert.Equal("stake", (string?)o["type"]); Assert.Equal("pbp", (string?)o["game"]); });
        Assert.False(replies[0].Value<bool>("time_ok"));
        Assert.NotNull(replies[0]["labels"]);
        Assert.Equal("pbp", (string?)api.Calls[1].Body["game"]);
        Assert.Equal("board", (string?)api.Calls[1].Body["match"]);
        Assert.Equal("sp", (string?)api.Calls[1].Body["kind"]);
        w.Close();
    });

    [Fact]
    public Task SettlementSurvivesPanicAndDeduplicatesAcrossReopenedBoards() => Run(async () =>
    {
        var pending = new TaskCompletionSource<JObject?>();
        var api = new Api { Reply = (_, _) => pending.Task };
        int booked = 0, builds = 0, posts = 0;
        var settlement = new StakeSettlement((row, seconds) => { Assert.Equal("pvp_loss", row); booked += seconds; return seconds; }, null, () => "player");
        GameWindow.PbpStakeFactory = post => { builds++; return new StakeBridge("pbp", post, api, settlement, () => true, () => { }); };
        var w = new GameWindow(GameWindow.Games[GameWindow.PbpId]);
        w.Posted += _ => posts++;
        w.Show();
        w.HandleMessage("{type:'stake-settle',match:'board'}");
        var work = w.StakePending;
        GameWindow.CloseAllForPanic();
        pending.SetResult(JObject.Parse("{ok:true,settled:{result:'lost',time_s:900,sp:10}}"));
        await work;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(900, booked);
        Assert.Equal(0, posts);
        var next = new GameWindow(GameWindow.Games[GameWindow.PbpId]);
        next.Show();
        next.HandleMessage("{type:'stake-state',match:'board'}");
        await next.StakePending;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(900, booked);
        Assert.Equal(1, builds);
    });

    [Fact]
    public Task HeadGateConsentAndBookingUseTheLiveChasterTab() => Run(async () =>
    {
        var s = CoreSettings.Current;
        var oldChaster = ChasterHead.Service;
        var (get, set) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var dir = Directory.CreateTempSubdirectory("stake-tab-");
        using var chaster = new ChasterService(new ChasterClient(), new SecretChasterTokenStore(),
            Path.Combine(dir.FullName, "tab.json"), () => new ChasterOptions(s.ChasterTabEnabled,
                s.ChasterLockId, new HashSet<string>(s.ChasterPrices), Paused: s.ChasterPaused));
        ChasterHead.Service = chaster;
        try
        {
            Assert.False(StakeHead.TimeOk());
            new SecretChasterTokenStore().Write(new ChasterStoredTokens("acc", "ref", DateTime.UtcNow.AddHours(1)));
            s.ChasterLockId = "lock";
            s.ChasterTabEnabled = true;
            s.ChasterPaused = false;
            s.ChasterPrices = new List<string> { "attention" };
            Assert.True(StakeHead.TimeOk());
            s.ChasterPaused = true;
            Assert.False(StakeHead.TimeOk());
            s.ChasterPaused = false;
            var original = s.ChasterPrices;
            var api = new Api();
            api.Reply = (_, _) => { Assert.Contains("pvp_loss", s.ChasterPrices); return Task.FromResult<JObject?>(new JObject { ["ok"] = true }); };
            var bridge = new StakeBridge("pbp", _ => { }, api, StakeHead.CreateSettlement(), StakeHead.TimeOk, StakeHead.EnableLossRow);
            await bridge.Handle(JObject.Parse("{type:'stake-offer',match:'m',kind:'time',amount:900}"));
            Assert.DoesNotContain("pvp_loss", original);
            StakeHead.EnableLossRow();
            Assert.Equal(1, s.ChasterPrices.Count(x => x == "pvp_loss"));
            s.UnifiedId = "player"; s.AuthToken = "test"; s.OfflineMode = false;
            var settlement = StakeHead.CreateSettlement();
            var result = settlement.Settle("pbp", "m", "player", JObject.Parse("{ok:true,settled:{result:'lost',time_s:900}}"));
            Assert.Equal(900, result.BookedSeconds);
            chaster.NoteSafetyExit();
            Assert.False(StakeHead.TimeOk());
        }
        finally
        {
            ChasterHead.Service = oldChaster;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, set);
            dir.Delete(true);
        }
    });

    [Fact]
    public Task SettlementWalletRechecksAccountAfterDispatchAndPreservesLocalCredits() => Run(() =>
    {
        var s = CoreSettings.Current;
        s.UnifiedId = "player"; s.AuthToken = "test"; s.OfflineMode = false; s.SkillPoints = 50;
        var settlement = StakeHead.CreateSettlement();
        settlement.AdoptBalance("limits", "player", JObject.Parse("{ok:true,sp:10}"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, s.SkillPoints);
        settlement.AdoptBalance("offer", "player", JObject.Parse("{ok:true,sp:40}"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, s.SkillPoints);
        settlement.AdoptBalance("offer", "player", JObject.Parse("{ok:true,sp:5}"));
        s.UnifiedId = "another-player";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(40, s.SkillPoints);
        return Task.CompletedTask;
    });
}
