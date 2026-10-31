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
    [InlineData(0, "piecebypiece")]
    [InlineData(0, "glassroad")]
    [InlineData(0, "breakoutdemo")]
    [InlineData(1, "piecebypiece")] // Shadow receipt.
    [InlineData(2, "piecebypiece")] // Host closes before the receipt.
    [InlineData(3, "piecebypiece")] // Sandbox with settings, but no loopback override.
    public void GameSlipsReachOnlyTheirOpenHostAndSandboxCannotClaim(int scenario, string id)
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
            var host = new GameWindow(GameWindow.Games[id]);
            host.Load(GameWindow.Server());
            host.Show();
            host.Web.OnNavigationCompleted(host.PageUrl!);
            var posted = new List<JObject>();
            host.Posted += json => { Assert.True(Dispatcher.UIThread.CheckAccess()); var msg = JObject.Parse(json); if ((string?)msg["type"] == "sparkles-receipt") posted.Add(msg); };
            host.Web.OnWebMessage(id == "glassroad" ? "{type:'race-result',finished:true,laps:3,fullLaps:3,place:2,of:8,timeS:600}"
                : id == "breakoutdemo" ? "{type:'breakout-wall',ballsLost:0,wallMs:42000,demo:true}"
                : "{type:'pbp:game-over',mode:'solo',outcome:'win',plies:44,durationMs:600000}");
            if (scenario == 2) host.Close();
            if (scenario != 3) SpinWait.SpinUntil(() => wire.Calls > 0, 5000);
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
}
