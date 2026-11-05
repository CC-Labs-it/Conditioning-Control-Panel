using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Sparkles;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed partial class GameWindowTests
{
    private sealed class RewardWire : HttpMessageHandler
    {
        internal int Calls;
        internal JObject? Body;
        internal readonly TaskCompletionSource<HttpResponseMessage> Reply = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Body = JObject.Parse(await req.Content!.ReadAsStringAsync(ct));
            return await Reply.Task;
        }
    }
    [Theory]
    [InlineData(0)] // Paid receipt.
    [InlineData(1)] // Shadow receipt.
    [InlineData(2)] // Host closes before the receipt.
    [InlineData(3)] // Sandbox with settings, but no loopback override.
    public void ChessSlipReachesOnlyItsOpenHostAndSandboxCannotClaim(int scenario)
    {
        var previousHooks = (SparklesHead.MakeService, CoreSecrets.RetrieveProvider);
        Run((_, _) =>
    {
        var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            var wire = new RewardWire();
            using var http = new HttpClient(wire);
            var s = CoreSettings.Current;
            s.UnifiedId = "account";
            s.OfflineMode = false;
            CoreSecrets.RetrieveProvider = _ => "test-token";
            s.SkillPoints = 100;
            s.GentleVisuals = true;
            SparklesHead.MakeService = () => scenario == 3 ? SparklesHead.Create("sandbox", null, http)
                : new SparklesClaimService(http, () => ("account", "test-token"), "http://localhost");
            var host = GameWindow.Launch(GameWindow.PbpId)!;
            host.Web.OnNavigationCompleted(host.PageUrl!);
            var posted = new List<JObject>();
            host.Posted += json => { Assert.True(Dispatcher.UIThread.CheckAccess()); posted.Add(JObject.Parse(json)); };
            host.Web.OnWebMessage("{type:'pbp:game-over',mode:'solo',outcome:'win',plies:44,durationMs:600000}");
            if (scenario == 2) host.Close();
            wire.Reply.SetResult(new(HttpStatusCode.OK) { Content = new StringContent(
                "{ok:true,receipt:{paid:8,credited:8,sp:80,result:{kind:'duel',outcome:'win'},shadow:" + (scenario == 1 ? "true" : "false") + "}}") });
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!host.SparklesPending.IsCompleted && DateTime.UtcNow < deadline)
            { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            Assert.True(host.SparklesPending.IsCompletedSuccessfully);
            Assert.Equal(scenario == 3 ? 0 : 1, wire.Calls);
            if (scenario != 0) { Assert.Empty(posted); return; }
            var receipt = Assert.Single(posted);
            Assert.Equal("sparkles-receipt", (string?)receipt["type"]);
            Assert.Equal(Loc.Get("Sparkles_Slip_Win"), (string?)receipt["lines"]![0]);
            Assert.Equal("Bezahlt", (string?)receipt["stamp"]);
            Assert.True((bool)receipt["gentle"]!);
            Assert.Equal(100, (int)receipt["sp"]!);
        }
        finally
        {
            (SparklesHead.MakeService, CoreSecrets.RetrieveProvider) = previousHooks;
            LocalizationManager.Instance.SetLanguage(previousLanguage);
        }
    });
        Assert.Same(previousHooks.MakeService, SparklesHead.MakeService);
        Assert.Same(previousHooks.RetrieveProvider, CoreSecrets.RetrieveProvider);
    }
    [Theory]
    [InlineData("dtrh", false)]
    [InlineData("dtrh", true)]
    [InlineData("race", false)]
    [InlineData("intake", false)]
    public void CompletedHostRunsClaimOnceAndDeliverTheirMedal(string id, bool abandoned) => Run((_, _) =>
    {
        var previous = (SparklesHead.MakeService, GameWindow.RaceBankSparks, GameWindow.DtrhBankSparks, CoreProgression.AddXPProvider);
        var dir = Directory.CreateTempSubdirectory("reward-intake-").FullName;
        var wire = new RewardWire();
        using var transport = new HttpClient(wire);
        var game = id == "intake" ? null : new GameWindow(GameWindow.Games[id]);
        var intake = id == "intake" ? new IntakeHostWindow { SessionsFolder = dir } : null;
        var window = (global::Avalonia.Controls.Window?)game ?? intake!;
        try
        {
            SparklesHead.MakeService = () => new SparklesClaimService(transport, () => ("account", "test-token"), "http://localhost");
            GameWindow.RaceBankSparks = _ => 0;
            GameWindow.DtrhBankSparks = (_, _) => 0;
            CoreProgression.AddXPProvider = (_, _) => { };
            if (game != null) game.Load(GameWindow.Server()); else intake!.Load(GameWindow.Server());
            window.Show();
            var web = game?.Web ?? intake!.Web;
            web.OnNavigationCompleted((game?.PageUrl ?? intake!.PageUrl)!);
            var posted = new List<JObject>();
            void Collect(string json) => posted.Add(JObject.Parse(json));
            if (game != null) game.Posted += Collect; else intake!.Posted += Collect;
            web.OnWebMessage("{type:'run-started'}");
            var end = id == "intake" ? "{type:'quiz-result',result:{totalScore:95,maxScore:100,niche:'bambi'}}"
                : "{type:'run-ended',score:5000,durationSec:60,difficultyMult:1,abandoned:" + (abandoned ? "true" : "false") + "}";
            web.OnWebMessage(end);
            web.OnWebMessage(end);
            wire.Reply.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{ok:true,receipt:{credited:8,result:{kind:'medal',medal:'gold'}}}") });
            var pending = game?.SparklesPending ?? intake!.SparklesPending;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!pending.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            Assert.True(pending.IsCompletedSuccessfully);
            if (abandoned) { Assert.Equal(0, wire.Calls); Assert.DoesNotContain(posted, o => (string?)o["type"] == "sparkles-receipt"); return; }
            Assert.Equal(1, wire.Calls);
            Assert.Equal(id == "race" ? "caucus" : id, (string?)wire.Body!["game"]);
            Assert.Equal("gold", (string?)wire.Body["result"]!["medal"]);
            Assert.Equal("gold", (string?)Assert.Single(posted.FindAll(o => (string?)o["type"] == "sparkles-receipt"))["medal"]);
        }
        finally
        {
            window.Close();
            (SparklesHead.MakeService, GameWindow.RaceBankSparks, GameWindow.DtrhBankSparks, CoreProgression.AddXPProvider) = previous;
            Directory.Delete(dir, true);
        }
    });
}
