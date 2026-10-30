using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Sparkles;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Core.Tests;
public class SparklesClaimServiceTests
{
    private const string Me = "u_me";
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly Queue<(HttpStatusCode Status, string Body)> Replies = new();
        public (HttpStatusCode Status, string Body) Default = (HttpStatusCode.OK, "{\"ok\":false,\"reason\":\"bad\"}");
        public readonly List<(HttpRequestMessage Req, string Body)> Seen = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            var (status, body) = Replies.Count > 0 ? Replies.Dequeue() : Default;
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Rig
    {
        public readonly FakeHandler Handler = new();
        public readonly List<(string Account, int Sp)> Adopted = new();
        public readonly List<TimeSpan> Waits = new();
        public (string, string)? Identity = (Me, "tok");
        public readonly SparklesClaimService Service;
        public Rig()
        {
            Service = new SparklesClaimService(new HttpClient(Handler), () => Identity, "http://127.0.0.1:9",
                (a, sp) => Adopted.Add((a, sp)), (t, _) => { Waits.Add(t); return Task.CompletedTask; });
        }
    }
    private static SparklesClaim Claim(string mode = "solo") => new(
        "abcdef0123456789abcdef", "rt3d", mode, SparklesResult.ForPlace(2, 8), 612000,
        new JObject { ["laps"] = 3 }, mode == "online" ? new JObject { ["roomId"] = "r1" } : null, 1793283204000);
    private const string FullReceipt = """
        { "ok": true, "receipt": {
            "id": "spr_1", "claimId": "abcdef0123456789abcdef", "kind": "place", "game": "rt3d", "mode": "online",
            "result": { "kind": "place", "place": 2, "of": 8 },
            "base": 16, "online": 5, "taper": 1, "lapScale": 1, "fee": false,
            "paid": 80, "credited": 80, "reason": null, "sp": 4310, "shadow": false, "at": 1793283205000,
            "day": { "solo": { "paid": 40, "ceil": 150, "fees": 2, "jar": 30 },
                     "online": { "paid": 180, "ceil": 600, "fees": 0, "jar": 60 },
                     "games": { "rt3d": 3 } },
            "resetInMs": 18000000 } }
        """;
    [Fact]
    public void AFullReceiptReadsEveryField()
    {
        var r = SparklesReceipt.Parse(JObject.Parse(FullReceipt))!;
        Assert.Equal(("spr_1", "abcdef0123456789abcdef", "place", "rt3d", "online"), (r.Id, r.ClaimId, r.Kind, r.Game, r.Mode));
        Assert.Equal((2, 8, 16, 5.0, 80, 80), (r.Place, r.Of, r.Base, r.Online, r.Paid, r.Credited));
        Assert.Equal((null, 4310, false, false, 1793283205000L), (r.Reason, r.Sp, r.Shadow, r.Fee, r.At));
        Assert.Equal(new SparklesDaySide(40, 150, 2, 30), r.Day!.Solo);
        Assert.Equal(new SparklesDaySide(180, 600, 0, 60), r.Day.Online);
        Assert.Equal(3, r.Day.Games["rt3d"]);
        Assert.Equal(18000000, r.ResetInMs);
    }
    [Fact]
    public void AThinReceiptFallsBackWithoutThrowing()
    {
        var r = SparklesReceipt.Parse(JObject.Parse("""{ "receipt": { "paid": "lots", "credited": 12.6, "reason": "taper" } }"""))!;
        Assert.Equal((0, 13, "taper", (int?)null, 1.0, false), (r.Paid, r.Credited, r.Reason, r.Sp, r.Taper, r.Shadow));
        Assert.Null(r.Day);
        Assert.Null(SparklesReceipt.Parse(new JArray()));
        Assert.Null(SparklesReceipt.Parse(null));
    }
    [Fact]
    public void AShadowReceiptIsReadButNeverAdopted()
    {
        var r = SparklesReceipt.Parse(JObject.Parse("""{ "receipt": { "paid": 80, "credited": 0, "sp": 4230, "shadow": true } }"""))!;
        Assert.Equal((true, 80), (r.Shadow, r.Paid));
        Assert.False(SparklesClaimService.ShouldAdopt(r));
        Assert.False(SparklesClaimService.ShouldAdopt(r with { Credited = 80 }));
    }
    [Fact]
    public void APayingReceiptOnlyRaisesTheWallet()
    {
        var r = SparklesReceipt.Parse(JObject.Parse(FullReceipt))!;
        Assert.True(SparklesClaimService.DecideWallet(true, r, 4000, out var up));
        Assert.Equal(4310, up);
        Assert.False(SparklesClaimService.DecideWallet(true, r, 5000, out var kept));
        Assert.Equal(5000, kept);
    }
    [Fact]
    public void ZeroPayShadowRefusalAndOtherAccountsNeverMoveTheWallet()
    {
        var r = SparklesReceipt.Parse(JObject.Parse(FullReceipt))!;
        Assert.False(SparklesClaimService.DecideWallet(false, r, 4000, out _));
        Assert.False(SparklesClaimService.DecideWallet(true, r with { Credited = 0 }, 4000, out _));
        Assert.False(SparklesClaimService.DecideWallet(true, r with { Shadow = true }, 4000, out _));
        Assert.False(SparklesClaimService.DecideWallet(true, r with { Sp = null }, 4000, out _));
        Assert.False(SparklesClaimService.DecideWallet(true, null, 4000, out var next));
        Assert.Equal(4000, next);
    }
    [Fact]
    public async Task OnlyAPayingReceiptReachesTheAdoptCallback()
    {
        var rig = new Rig();
        rig.Handler.Replies.Enqueue((HttpStatusCode.OK, FullReceipt));
        var paid = await rig.Service.ClaimAsync(Claim());
        Assert.True(paid.Ok && paid.Adopted);
        Assert.Equal(new[] { (Me, 4310) }, rig.Adopted);
        rig.Handler.Replies.Enqueue((HttpStatusCode.OK, """{ "ok": true, "receipt": { "credited": 0, "sp": 1, "shadow": true } }"""));
        var shadow = await rig.Service.ClaimAsync(Claim());
        Assert.True(shadow.Ok);
        Assert.False(shadow.Adopted);
        rig.Handler.Replies.Enqueue((HttpStatusCode.OK, """{ "ok": false, "reason": "disabled" }"""));
        var refused = await rig.Service.ClaimAsync(Claim());
        Assert.False(refused.Ok);
        Assert.Equal("disabled", refused.Reason);
        Assert.Single(rig.Adopted);
    }
    [Fact]
    public async Task TheClaimBodyUsesTheContractNamesExactly()
    {
        var rig = new Rig();
        rig.Handler.Replies.Enqueue((HttpStatusCode.OK, FullReceipt));
        await rig.Service.ClaimAsync(Claim("online"));
        var (req, text) = Assert.Single(rig.Handler.Seen);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("http://127.0.0.1:9/v2/sparkles/claim", req.RequestUri!.ToString());
        Assert.Equal("tok", req.Headers.GetValues("X-Auth-Token").Single());
        var body = JObject.Parse(text);
        Assert.Equal(
            new[] { "unified_id", "claimId", "game", "mode", "result", "durationMs", "detail", "endedAt", "proof" }.OrderBy(x => x),
            body.Properties().Select(p => p.Name).OrderBy(x => x));
        Assert.Equal((Me, "online", "r1"), ((string?)body["unified_id"], (string?)body["mode"], (string?)body["proof"]!["roomId"]));
        Assert.Equal((612000L, 1793283204000L), ((long)body["durationMs"]!, (long)body["endedAt"]!));
        Assert.True(JToken.DeepEquals(JObject.Parse("""{"kind":"place","place":2,"of":8}"""), body["result"]));
    }
    [Fact]
    public void ResultShapesAndAllowListsFollowTheContract()
    {
        Assert.True(JToken.DeepEquals(JObject.Parse("""{"kind":"duel","outcome":"win"}"""), SparklesResult.ForDuel("win").ToJson()));
        Assert.True(JToken.DeepEquals(JObject.Parse("""{"kind":"medal","medal":"gold"}"""), SparklesResult.ForMedal("gold").ToJson()));
        Assert.Null(Claim().ToJson(Me)["proof"]);
        Assert.True(SparklesClaim.IsClaimId(SparklesClaim.NewClaimId()));
        Assert.False(SparklesClaim.IsClaimId("short"));
        Assert.False(SparklesClaim.IsClaimId("has spaces in it 0123456789"));
        Assert.False((Claim() with { Game = "pong" }).IsWellFormed);
        Assert.False((Claim() with { Mode = "ranked" }).IsWellFormed);
        var fresh = SparklesClaim.Create("chess", "online", SparklesResult.ForDuel("draw"), -5);
        Assert.True(fresh.IsWellFormed && fresh.DurationMs == 0 && fresh.EndedAt > 0);
    }
    [Fact]
    public async Task AMalformedClaimOrNoAccountSendsNothing()
    {
        var rig = new Rig();
        Assert.Equal("invalid", (await rig.Service.ClaimAsync(Claim() with { ClaimId = "x" })).Reason);
        rig.Identity = null;
        Assert.Equal("signin", (await rig.Service.ClaimAsync(Claim())).Reason);
        Assert.Empty(rig.Handler.Seen);
        rig.Identity = (Me, "tok");
        rig.Handler.Default = (HttpStatusCode.Unauthorized, """{"error":"bad token"}""");
        Assert.Equal("signin", (await rig.Service.ClaimAsync(Claim())).Reason);
        Assert.Single(rig.Handler.Seen);
    }
    [Fact]
    public async Task PendingRetriesTheSameClaimThenGivesUp()
    {
        var rig = new Rig();
        rig.Handler.Default = (HttpStatusCode.Accepted, """{"ok":false,"pending":true,"retryAfterMs":1500}""");
        var outcome = await rig.Service.ClaimAsync(Claim("online"));
        Assert.False(outcome.Ok);
        Assert.Equal("pending", outcome.Reason);
        Assert.Equal(SparklesClaimService.MaxAttempts, rig.Handler.Seen.Count);
        Assert.Single(rig.Handler.Seen.Select(s => (string?)JObject.Parse(s.Body)["claimId"]).Distinct());
        Assert.Equal(SparklesClaimService.MaxAttempts - 1, rig.Waits.Count);
        Assert.All(rig.Waits, w => Assert.Equal(TimeSpan.FromMilliseconds(1500), w));
        Assert.Empty(rig.Adopted);
    }
    [Fact]
    public async Task PendingThenAReceiptPaysOnce()
    {
        var rig = new Rig();
        rig.Handler.Replies.Enqueue((HttpStatusCode.Accepted, """{"ok":false,"pending":true,"retryAfterMs":99999}"""));
        rig.Handler.Replies.Enqueue((HttpStatusCode.OK, """{"ok":false,"reason":"busy"}"""));
        rig.Handler.Replies.Enqueue((HttpStatusCode.OK, FullReceipt));
        var outcome = await rig.Service.ClaimAsync(Claim("online"));
        Assert.True(outcome.Ok);
        Assert.Equal(3, rig.Handler.Seen.Count);
        Assert.Equal(SparklesClaimService.MaxRetryWait, rig.Waits[0]);
        Assert.Single(rig.Adopted);
    }
    [Fact]
    public async Task AWordedRefusalIsNotAskedAgain()
    {
        var rig = new Rig();
        rig.Handler.Default = (HttpStatusCode.OK, """{"ok":false,"reason":"unproven"}""");
        var outcome = await rig.Service.ClaimAsync(Claim("online"));
        Assert.Equal("unproven", outcome.Reason);
        Assert.Single(rig.Handler.Seen);
        Assert.Empty(rig.Waits);
    }
}
