using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.GameRewards;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Core.Tests;
public class SparklesPortTests
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
        var c = GlassroadRewards.SparklesClaimFor(o)!;
        Assert.Equal(expected, c.Mode);
        Assert.Equal(medal, c.Result.Medal);
        Assert.Equal(mode == "online" ? "room-1" : null, (string?)c.Proof?["roomId"]);
        Assert.Equal(600, c.Duration.TotalSeconds);
        o["finished"] = false;
        Assert.Null(GlassroadRewards.SparklesClaimFor(o));
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
    public void ChessAndGoonCarryServerProofAndRejectAbandonedGames()
    {
        var chess = JObject.Parse("{mode:'online',outcome:'draw',plies:60,durationMs:300000,matchId:'match-1'}");
        Assert.Equal("match-1", (string?)SparklesPageResults.Chess(chess)!.Proof!["matchId"]);
        chess["reason"] = "abandoned";
        Assert.Null(SparklesPageResults.Chess(chess));
        var goon = JObject.Parse("{practice:false,code:'ABCD',result:{endReason:1,winnerIsHost:true,localIsHost:true,survivedMs:60000}}");
        Assert.Equal("ABCD", (string?)SparklesPageResults.Goon(goon)!.Proof!["goonMatchId"]);
        goon["result"]!["endReason"] = 2;
        Assert.Null(SparklesPageResults.Goon(goon));
    }
    [Theory]
    [InlineData(true, false, 5, 20, false, 20)]
    [InlineData(true, false, 30, 20, true, 30)]
    [InlineData(false, false, 30, 20, false, 20)]
    [InlineData(true, true, 30, 20, false, 20)]
    public void WalletOnlyRaisesForSameAccountPayingReceipts(bool same, bool shadow, int server, int local, bool adopt, int expected)
    {
        var receipt = new SparklesReceipt { Credited = 5, Sp = server, Shadow = shadow };
        Assert.Equal(adopt, SparklesClaimService.DecideWallet(same, receipt, local, out var next));
        Assert.Equal(expected, next);
    }
    private sealed class Wire : HttpMessageHandler
    {
        internal JObject? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK) { Content = new StringContent("{ok:true,receipt:{credited:0,shadow:true}}") };
        }
    }
    [Fact]
    public async Task GameEndTransmitsOnlineProof()
    {
        var wire = new Wire();
        var service = new SparklesClaimService(new HttpClient(wire), () => ("account", "token"), "https://example.test");
        await SparklesGameEnd.Claim("chess", "online", SparklesResult.ForDuel("win"), TimeSpan.FromMinutes(3),
            proof: new JObject { ["matchId"] = "match-1" }, svc: service);
        Assert.Equal("match-1", (string?)wire.Body?["proof"]?["matchId"]);
    }
}
