using System;
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
        internal readonly TaskCompletionSource<HttpResponseMessage> Reply = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Reply.Task;
        }
    }
    [Theory]
    [InlineData(0)] // Paid receipt.
    [InlineData(1)] // Shadow receipt.
    [InlineData(2)] // Host closes before the receipt.
    [InlineData(3)] // Sandbox with settings, but no loopback override.
    public void ChessSlipReachesOnlyItsOpenHostAndSandboxCannotClaim(int scenario) => Run((_, _) =>
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
        finally { LocalizationManager.Instance.SetLanguage(previousLanguage); }
    });
}
