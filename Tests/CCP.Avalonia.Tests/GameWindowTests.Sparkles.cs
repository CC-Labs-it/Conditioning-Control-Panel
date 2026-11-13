using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Sparkles.Claims;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Sparkles by place on this head (restack b96, from #3830 and #3831): every game host claims
/// through the one SparklesHead service and the slip reaches only its own open page.</summary>
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
    [InlineData("piecebypiece", 0)] // Paid receipt.
    [InlineData("piecebypiece", 1)] // Shadow receipt.
    [InlineData("piecebypiece", 2)] // Host closes before the receipt.
    [InlineData("piecebypiece", 3)] // Sandbox without a loopback override: no service.
    [InlineData("breakoutdemo", 0)]
    [InlineData("breakoutdemo", 1)]
    [InlineData("breakoutdemo", 2)]
    [InlineData("breakoutdemo", 3)]
    [InlineData("glassroad", 0)]
    [InlineData("glassroad", 1)]
    [InlineData("glassroad", 2)]
    [InlineData("glassroad", 3)]
    public void SlipReachesOnlyItsOpenHostAndSandboxCannotClaim(string id, int scenario)
    {
        var previous = SparklesHead.Service;
        Run((_, _) =>
        {
            var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
            try
            {
                LocalizationManager.Instance.SetLanguage("de");
                var wire = new RewardWire();
                using var http = new HttpClient(wire);
                var s = CoreSettings.Current;
                s.SkillPoints = 100;
                s.GentleVisuals = true;
                SparklesHead.Service = scenario == 3 ? SparklesHead.Create("sandbox", null, http)
                    : new SparklesClaimService(http, () => ("account", "test-token"), "http://localhost");
                var host = new GameWindow(GameWindow.Games[id]);
                host.Load(GameWindow.Server());
                host.Show();
                host.Web.OnNavigationCompleted(host.PageUrl!);
                if (id == GameWindow.GlassroadId) host.Web.OnWebMessage("{type:'ready'}");
                var posted = new List<JObject>();
                host.Posted += json => { Assert.True(Dispatcher.UIThread.CheckAccess()); posted.Add(JObject.Parse(json)); };
                host.Web.OnWebMessage(id switch
                {
                    "glassroad" => "{type:'race-result',finished:true,laps:3,fullLaps:3,place:1,of:8,timeS:600}",
                    "breakoutdemo" => "{type:'breakout-wall',ballsLost:0,wallMs:600000}",
                    _ => "{type:'pbp:game-over',mode:'solo',outcome:'win',plies:44,durationMs:600000}",
                });
                if (scenario == 2) host.Close();
                wire.Reply.SetResult(new(HttpStatusCode.OK) { Content = new StringContent(
                    "{ok:true,receipt:{paid:8,credited:8,sp:80,result:{kind:'duel',outcome:'win'},shadow:" + (scenario == 1 ? "true" : "false") + "}}") });
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while ((!host.SparklesPending.IsCompleted || (scenario != 3 && wire.Calls == 0)) && DateTime.UtcNow < deadline)
                { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
                Assert.True(host.SparklesPending.IsCompletedSuccessfully);
                Assert.Equal(scenario == 3 ? 0 : 1, wire.Calls);
                if (scenario != 0) { Assert.DoesNotContain(posted, o => (string?)o["type"] == "sparkles-receipt"); if (scenario != 2) host.Close(); return; }
                var receipt = Assert.Single(posted.FindAll(o => (string?)o["type"] == "sparkles-receipt"));
                Assert.Equal(Loc.Get("Sparkles_Slip_Win"), (string?)receipt["lines"]![0]);
                Assert.Equal("Bezahlt", (string?)receipt["stamp"]);
                Assert.True((bool)receipt["gentle"]!);
                Assert.Equal(100, (int)receipt["sp"]!);   // the chip shows the higher local balance
                Assert.Equal(100, CoreSettings.Current.SkillPoints);   // and a lower server balance never lowers the wallet
                host.Close();
            }
            finally
            {
                SparklesHead.Service = previous;
                LocalizationManager.Instance.SetLanguage(previousLanguage);
            }
        });
    }

    [Theory]
    [InlineData("dtrh", false)]
    [InlineData("dtrh", true)]
    [InlineData("intake", false)]
    // No "race" (Caucus 2D) row: no door opens that bridge on this head ("race" resolves to Glassroad).
    public void CompletedHostRunsClaimOnceAndDeliverTheirMedal(string id, bool abandoned) => Run((_, _) =>
    {
        var previous = (SparklesHead.Service, GameWindow.RaceBankSparks, GameWindow.DtrhBankSparks, CoreProgression.AddXPProvider);
        var dir = Directory.CreateTempSubdirectory("reward-intake-").FullName;
        var wire = new RewardWire();
        using var transport = new HttpClient(wire);
        var game = id == "intake" ? null : new GameWindow(GameWindow.Games[id]);
        var intake = id == "intake" ? new IntakeHostWindow { SessionsFolder = dir } : null;
        var window = (global::Avalonia.Controls.Window?)game ?? intake!;
        try
        {
            SparklesHead.Service = new SparklesClaimService(transport, () => ("account", "test-token"), "http://localhost");
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
            Assert.Equal(id, (string?)wire.Body!["game"]);
            Assert.Equal("gold", (string?)wire.Body["result"]!["medal"]);
            Assert.Equal("gold", (string?)Assert.Single(posted.FindAll(o => (string?)o["type"] == "sparkles-receipt"))["medal"]);
        }
        finally
        {
            window.Close();
            (SparklesHead.Service, GameWindow.RaceBankSparks, GameWindow.DtrhBankSparks, CoreProgression.AddXPProvider) = previous;
            Directory.Delete(dir, true);
        }
    });
}
