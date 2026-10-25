using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using CCP.Tests.Shared;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Invites;
using ConditioningControlPanel.Services.Stakes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class StakeHostTests
{
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(AppFonts.Inter)), out _))
        {
            var builder = AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register);
            builder.AfterSetupCallback(builder);
        }
        var secrets = new Dictionary<string, string?>();
        var oldSecrets = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        var service = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldHooks = (StakeBridge.TimeOkHook, StakeBridge.EnableLossRowHook, StakeSettlement.CreateShared);
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.OfflineMode = false;
        try { await body(); }
        finally
        {
            CoreSettings.ServiceProvider = oldSettings;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = oldSecrets;
            (StakeBridge.TimeOkHook, StakeBridge.EnableLossRowHook, StakeSettlement.CreateShared) = oldHooks;
        }
    });

    [Fact]
    public Task Gate_loss_row_and_settlement_use_the_live_Chaster_tab() => Run(async () =>
    {
        var s = CoreSettings.Current;
        var previous = ChasterHead.Service;
        var tokens = new SecretChasterTokenStore();
        tokens.Write(new ChasterStoredTokens("access", "refresh", DateTime.UtcNow.AddHours(1)));
        using var c = ChasterHead.Create("sandbox", null);
        ChasterHead.Service = c;
        try
        {
            s.UnifiedId = "stake-player";
            s.AuthToken = "test-token";
            s.ChasterTabEnabled = true;
            s.ChasterPrices = new List<string> { "session" };
            StakeHead.Seed();
            Assert.True(StakeBridge.TimeOkHook());
            s.ChasterPaused = true;
            Assert.False(StakeBridge.TimeOkHook());
            s.ChasterPaused = false;
            s.ChasterTabEnabled = false;
            Assert.False(StakeBridge.TimeOkHook());
            s.ChasterTabEnabled = true;
            var before = s.ChasterPrices;
            await Task.Run(StakeBridge.EnableLossRowHook);
            Assert.NotSame(before, s.ChasterPrices);
            Assert.DoesNotContain("pvp_loss", before);
            StakeBridge.EnableLossRowHook();
            Assert.Single(s.ChasterPrices.Where(x => x == "pvp_loss"));
            var settlement = StakeHead.CreateSettlement();
            var loss = JObject.Parse("{ok:true,settled:{result:'lost',time_s:900,sp:20}}");
            Assert.Equal(900, settlement.Settle("pbp", "one", "stake-player", loss).BookedSeconds);
            Assert.Equal(0, settlement.Settle("pbp", "one", "stake-player", loss).BookedSeconds);
            Assert.Equal(0, settlement.Settle("pbp", "two", "another-player", loss).BookedSeconds);
            c.NoteSafetyExit();
            Assert.False(StakeBridge.TimeOkHook());
            Assert.Equal(0, settlement.Settle("pbp", "three", "stake-player", loss).BookedSeconds);
            tokens.Clear();
            Assert.False(StakeBridge.TimeOkHook());
            ChasterHead.Service = null;
            Assert.False(StakeBridge.TimeOkHook());
        }
        finally { ChasterHead.Service = previous; tokens.Clear(); }
    });

    [Fact]
    public Task Wallet_rechecks_account_inside_dispatch_and_distinguishes_receipts() => Run(() =>
    {
        var s = CoreSettings.Current;
        s.UnifiedId = "stake-player";
        s.AuthToken = "test-token";
        s.SkillPoints = 50;
        var settlement = StakeHead.CreateSettlement();
        settlement.AdoptBalance("limits", "stake-player", JObject.Parse("{ok:true,sp:10}"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(50, s.SkillPoints);
        settlement.AdoptBalance("offer", "stake-player", JObject.Parse("{ok:true,sp:25}"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(25, s.SkillPoints);
        settlement.AdoptBalance("limits", "stake-player", JObject.Parse("{ok:true,sp:60}"));
        s.UnifiedId = "changed-player";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(25, s.SkillPoints);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Pbp_routes_all_stake_frames_and_watches_survive_panic_and_reopen() => Run(async () =>
    {
        using var api = new FakeServer();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool wait = false;
        api.Handle = async ctx =>
        {
            if (wait) { entered.TrySetResult(); await gate.Task; }
            await FakeServer.Send(ctx, Encoding.UTF8.GetBytes(ctx.Request.Url!.AbsolutePath.EndsWith("state")
                ? "{\"ok\":true,\"settled\":{\"result\":\"won\",\"sp\":55}}"
                : "{\"ok\":true,\"sp\":50}"));
        };
        var old = (InviteApi.DefaultIdentity, InviteApi.DefaultBaseUrl, GameWindow.PbpStakes);
        GameWindow? board = null;
        try
        {
            InviteApi.DefaultIdentity = () => ("stake-player", "test-token");
            InviteApi.DefaultBaseUrl = () => api.Prefix.TrimEnd('/');
            StakeHead.Seed();
            GameWindow.PbpStakes = null;
            board = new GameWindow(GameWindow.Games[GameWindow.PbpId]);
            board.Show();
            var replies = new List<JObject>();
            board.Posted += json => replies.Add(JObject.Parse(json));
            foreach (var type in new[] { "stake-limits", "stake-offer", "stake-state" })
            {
                board.HandleMessage(new JObject { ["type"] = type, ["match"] = "match", ["kind"] = "sp", ["amount"] = 5 }.ToString());
                await board.StakePending.WaitAsync(TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(new[] { "limits", "offer", "state" }, replies.Select(r => (string?)r["op"]));
            Assert.All(replies, r => { Assert.Equal("stake", (string?)r["type"]); Assert.Equal("pbp", (string?)r["game"]); });
            Assert.NotNull(replies[0]["labels"]);
            Assert.False((bool)replies[0]["time_ok"]!);
            board.HandleMessage("{type:'stake-offer',match:'match',kind:'time',amount:900}");
            await board.StakePending;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("no_chaster", (string?)replies.Last()["reason"]);
            wait = true;
            board.HandleMessage("{type:'stake-settle',match:'match'}");
            var pending = board.StakePending;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            GameWindow.CloseAllForPanic();
            Assert.False(GameWindow.IsAnyOpen());
            var count = replies.Count;
            board = new GameWindow(GameWindow.Games[GameWindow.PbpId]);
            board.Posted += json => replies.Add(JObject.Parse(json));
            board.Show();
            gate.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(count + 1, replies.Count);
            Assert.Equal("settled", (string?)replies.Last()["op"]);
            Assert.False(replies.Last().Value<bool>("gave_up"));
        }
        finally
        {
            gate.TrySetResult();
            board?.Close();
            (InviteApi.DefaultIdentity, InviteApi.DefaultBaseUrl, GameWindow.PbpStakes) = old;
        }
    });
}
