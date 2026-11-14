using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Sparkles.Claims;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF SparklesGameEnd / SparklesPageResults / SparklesSlip / GlassroadRules.SparklesClaimFor on
/// the Core claim service (restack b96, from #3830 and #3831).</summary>
public class SparklesGameEndTests
{
    [Theory]
    [InlineData("race", "solo", null)]
    [InlineData("trial", "trial", "gold")]
    [InlineData("ghost", "ghost", null)]
    [InlineData("online", "online", null)]
    public void RaceMapsPlaceMedalDuelAndRoom(string mode, string expected, string? medal)
    {
        var o = JObject.Parse("{type:'race-result',finished:true,laps:3,fullLaps:3,place:2,of:8,timeS:600,medal:'glass',ghost:'win',roomId:'room-1'}");
        o["mode"] = mode;
        var c = SparklesPageResults.Glassroad(o)!;
        Assert.Equal(("rt3d", expected), (c.Game, c.Mode));
        Assert.Equal(medal, c.Result.Medal);
        Assert.Equal(mode == "online" ? "room-1" : null, (string?)c.Proof?["roomId"]);
        Assert.Equal(600, c.Duration.TotalSeconds);
        o["finished"] = false;
        Assert.Null(SparklesPageResults.Glassroad(o));
    }

    [Fact]
    public void ShortTrialsAndRoomlessOnlineResultsClaimNothing()
    {
        Assert.Null(SparklesPageResults.Glassroad(JObject.Parse("{type:'race-result',mode:'trial',finished:true,laps:2,fullLaps:3,timeS:60,medal:'gold'}")));
        Assert.Null(SparklesPageResults.Glassroad(JObject.Parse("{type:'race-result',mode:'online',finished:true,laps:3,fullLaps:3,place:1,of:4,timeS:60,roomId:'bad id'}")));
        Assert.Null(SparklesPageResults.Glassroad(JObject.Parse("{type:'race-result',finished:true,flagged:true,laps:3,fullLaps:3,place:1,of:4,timeS:60}")));
    }

    [Theory]
    [InlineData(0, "gold")]
    [InlineData(1, "silver")]
    [InlineData(2, "bronze")]
    [InlineData(3, "none")]
    public void DemoWallsPayLikeFullGameAndSkippedWallsDoNot(int lost, string medal)
    {
        var o = new JObject { ["ballsLost"] = lost, ["wallMs"] = 42000, ["demo"] = true };
        var c = SparklesPageResults.BreakoutWall(o)!;
        Assert.Equal(("breakout", "solo", medal, 42d), (c.Game, c.Mode, c.Result.Medal, c.Duration.TotalSeconds));
        o["skipped"] = true;
        Assert.Null(SparklesPageResults.BreakoutWall(o));
    }

    [Fact]
    public void ChessCarriesServerProofAndRejectsAbandonedGames()
    {
        var chess = JObject.Parse("{mode:'online',outcome:'draw',plies:60,durationMs:300000,matchId:'match-1'}");
        Assert.Equal("match-1", (string?)SparklesPageResults.Chess(chess)!.Proof!["matchId"]);
        chess["reason"] = "abandoned";
        Assert.Null(SparklesPageResults.Chess(chess));
        Assert.Null(SparklesPageResults.Chess(JObject.Parse("{mode:'hotseat',outcome:'win'}")));
    }

    [Theory]
    [InlineData(250, 250, "gold")]
    [InlineData(150, 250, "silver")]
    [InlineData(75, 250, "bronze")]
    [InlineData(10, 250, "none")]
    [InlineData(10, 0, "none")]
    public void RatioMedalFollowsTheRunCeiling(double score, double cap, string medal) =>
        Assert.Equal(medal, SparklesGameEnd.RatioMedal(score, cap));

    [Theory]
    [InlineData(95, "gold")]
    [InlineData(70, "silver")]
    [InlineData(50, "bronze")]
    [InlineData(49, "none")]
    public void IntakeMedalFollowsTheMarks(double total, string medal) =>
        Assert.Equal(medal, SparklesGameEnd.IntakeMedal(total, 100, 90));

    [Theory]
    [InlineData("{result:{kind:'medal',medal:'gold'},taper:0.5}", "Sparkles_Slip_Gold", "Sparkles_Slip_HalfRate")]
    [InlineData("{place:2,of:8,reason:'short'}", "Sparkles_Slip_Place", "Sparkles_Slip_Short")]
    [InlineData("{result:{kind:'duel',outcome:'draw'},reason:'ceiling'}", "Sparkles_Slip_Draw", "Sparkles_Slip_CeilingToday")]
    public void SlipsNameResultsAndExplainRates(string json, string result, string reason)
    {
        var outcome = new SparklesClaimOutcome(SparklesReceipt.Parse(JObject.Parse(json)), null);
        var msg = SparklesSlip.Build(outcome, false, key => key)!;
        Assert.Equal(result, (string?)msg["lines"]![0]);
        Assert.Equal(reason, (string?)msg["lines"]![1]);
        Assert.Null(SparklesSlip.Build(null, false, key => key));
        Assert.Null(SparklesSlip.Build(SparklesClaimOutcome.Refused("pending"), false, key => key));
    }

    [Fact]
    public void ShadowReceiptsNeverProduceASlip() =>
        Assert.Null(SparklesSlip.Build(new SparklesClaimOutcome(new SparklesReceipt { Shadow = true, Paid = 8 }, null), false, key => key));

    [Theory]
    [InlineData(80, 100, 100)]
    [InlineData(120, 100, 120)]
    public void SlipChipShowsTheHigherBalance(int server, int local, int shown)
    {
        var msg = SparklesSlip.Build(new SparklesClaimOutcome(new SparklesReceipt { Paid = 8, Credited = 8, Sp = server }, null), false, k => k);
        Assert.Equal(shown, (int)SparklesSlip.WithWallet(msg, local)!["sp"]!);
    }

    private sealed class Wire : HttpMessageHandler
    {
        internal JObject? Body;
        internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            Body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent("{ok:true,receipt:{credited:0,shadow:true}}") };
        }
    }

    [Fact]
    public async Task GameEndTransmitsOnlineProof()
    {
        var wire = new Wire();
        var service = new SparklesClaimService(new HttpClient(wire), () => ("account", "token"), "https://example.test");
        await SparklesGameEnd.Claim(service, "chess", "online", SparklesResult.ForDuel("win"), TimeSpan.FromMinutes(3),
            proof: new JObject { ["matchId"] = "match-1" });
        Assert.Equal("match-1", (string?)wire.Body?["proof"]?["matchId"]);
    }

    [Fact]
    public async Task NoServiceOrNoClaimSendsNothing()
    {
        Assert.Null(await SparklesGameEnd.Claim(null, "chess", "solo", SparklesResult.ForDuel("win"), TimeSpan.FromMinutes(1)));
        var wire = new Wire();
        var service = new SparklesClaimService(new HttpClient(wire), () => ("account", "token"), "https://example.test");
        Assert.Null(await SparklesPageResults.Send(null, service));
        Assert.Equal(0, wire.Calls);
    }
}
