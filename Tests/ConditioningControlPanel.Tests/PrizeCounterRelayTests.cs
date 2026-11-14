using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.PrizeCounter;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Prize Counter host seam (web tree prizecounter/CONTRACT.md "Host seam"): two ops, two routes,
/// the token door added by the host, the page's idem passed through, and the wallet rule: only a
/// receipt (or an <c>insufficient</c> buy refusal) may lower the balance.
/// </summary>
public class PrizeCounterRelayTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Url, string? Token, string? Body)> Seen = new();
        public Func<int, HttpResponseMessage> Answer = _ => Json(200, "{\"ok\":true}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            Seen.Add((r.Method, r.RequestUri!.ToString(), r.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null, body));
            return Answer(Seen.Count);
        }
    }

    private sealed record Adopted(string Account, bool FromBuy, int Sp);

    private static HttpResponseMessage Json(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body) };

    private static (PrizeCounterRelay Relay, FakeHandler H, List<Adopted> Sp, List<string[]> Prizes) Make(
        bool signedOut = false, bool offline = false)
    {
        var h = new FakeHandler();
        var sp = new List<Adopted>();
        var prizes = new List<string[]>();
        var relay = new PrizeCounterRelay(new HttpClient(h), () => signedOut ? null : ("uid-1", "tok-1"), () => offline,
            (a, b, s) => sp.Add(new Adopted(a, b, s)), (_, _, g) => prizes.Add(g));
        return (relay, h, sp, prizes);
    }

    private static JObject BuyBody() => new() { ["itemId"] = "rain.dress.red", ["catalogVersion"] = "c9", ["week"] = 44, ["unified_id"] = "someone-else" };

    [Theory]
    [InlineData("shop-state", "GET", "/v2/shop/state")]
    [InlineData("shop-buy", "POST", "/v2/shop/buy")]
    public void Whitelist_MapsEachOpToItsRoute(string op, string method, string path)
    {
        Assert.True(PrizeCounterRelay.TryRoute(op, out var m, out var p));
        Assert.Equal(method, m.Method);
        Assert.Equal(path, p);
    }

    [Theory]
    [InlineData("counter-buy")]
    [InlineData("SHOP-STATE")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnknownOps_AreRefusedWithoutAFetch(string? op)
    {
        var (relay, h, _, _) = Make();
        var reply = await relay.RelayAsync(op, new JObject(), "idem-1");
        Assert.False(reply.Value<bool>("ok"));
        Assert.Equal("bad_input", reply.Value<string>("reason"));
        Assert.Empty(h.Seen);
    }

    [Fact]
    public async Task State_IsAGetWithTheTokenDoor()
    {
        var (relay, h, _, _) = Make();
        h.Answer = _ => Json(200, "{\"ok\":true,\"week\":44,\"tabs\":[]}");
        var reply = await relay.RelayAsync("shop-state", null, null);
        Assert.True(reply.Value<bool>("ok"));
        Assert.Equal(44, reply.Value<int>("week"));
        var seen = Assert.Single(h.Seen);
        Assert.Equal(HttpMethod.Get, seen.Method);
        Assert.EndsWith("/v2/shop/state?unified_id=uid-1", seen.Url);
        Assert.Equal("tok-1", seen.Token);
    }

    [Fact]
    public async Task Buy_PostsTheContractBody_WithTheIdemAndTheHostsAccount()
    {
        var (relay, h, _, _) = Make();
        await relay.RelayAsync("shop-buy", BuyBody(), "idem-7");
        var seen = Assert.Single(h.Seen);
        Assert.Equal(HttpMethod.Post, seen.Method);
        Assert.EndsWith("/v2/shop/buy", seen.Url);
        Assert.Equal("tok-1", seen.Token);
        var body = JObject.Parse(seen.Body!);
        Assert.Equal("idem-7", body.Value<string>("idem"));
        Assert.Equal("rain.dress.red", body.Value<string>("itemId"));
        Assert.Equal("c9", body.Value<string>("catalogVersion"));
        Assert.Equal(44, body.Value<int>("week"));
        Assert.Equal("uid-1", body.Value<string>("unified_id"));   // the page cannot name another account
        Assert.Equal(5, body.Properties().Count());
    }

    [Fact]
    public async Task OnlyALandedBuy_FeedsTheDayLogSink()
    {
        var (relay, h, _, _) = Make();
        int landed = 0;
        relay.BuyLanded = () => landed++;
        h.Answer = _ => Json(200, "{\"ok\":true,\"sp\":10}");
        await relay.RelayAsync("shop-state", null, null);
        Assert.Equal(0, landed);                                   // a read is not a buy
        await relay.RelayAsync("shop-buy", BuyBody(), "idem-1");
        Assert.Equal(1, landed);
        h.Answer = _ => Json(409, "{\"ok\":false,\"reason\":\"insufficient\",\"sp\":3}");
        await relay.RelayAsync("shop-buy", BuyBody(), "idem-2");
        Assert.Equal(1, landed);                                   // a refusal is not a buy
        relay.BuyLanded = () => throw new InvalidOperationException("sink");
        h.Answer = _ => Json(200, "{\"ok\":true,\"sp\":1}");
        var reply = await relay.RelayAsync("shop-buy", BuyBody(), "idem-3");
        Assert.True(reply.Value<bool>("ok"));                     // a throwing sink never breaks the reply
    }

    [Fact]
    public async Task Buy_WithoutAnIdem_IsRefused()
    {
        var (relay, h, _, _) = Make();
        var reply = await relay.RelayAsync("shop-buy", BuyBody(), null);
        Assert.Equal("bad_input", reply.Value<string>("reason"));
        Assert.Empty(h.Seen);
    }

    [Fact]
    public async Task ANetworkFailure_IsRetriedOnce_WithTheSameIdem()
    {
        var (relay, h, _, _) = Make();
        h.Answer = n => n == 1 ? throw new HttpRequestException("reset") : Json(200, "{\"ok\":true}");
        var reply = await relay.RelayAsync("shop-buy", BuyBody(), "idem-9");
        Assert.True(reply.Value<bool>("ok"));
        Assert.Equal(2, h.Seen.Count);
        Assert.All(h.Seen, s => Assert.Equal("idem-9", JObject.Parse(s.Body!).Value<string>("idem")));
    }

    [Theory]
    [InlineData(true, false, "signed_out")]
    [InlineData(false, true, "offline")]
    public async Task HostRefusals_AreSignedOutOrOffline(bool signedOut, bool offline, string reason)
    {
        var (relay, h, _, _) = Make(signedOut, offline);
        var reply = await relay.RelayAsync("shop-state", null, null);
        Assert.Equal(reason, reply.Value<string>("reason"));
        Assert.Empty(h.Seen);
    }

    [Fact]
    public async Task AnUnwordedServerPage_IsOffline()
    {
        var (relay, h, _, _) = Make();
        h.Answer = _ => Json(502, "<html>bad gateway</html>");
        var reply = await relay.RelayAsync("shop-state", null, null);
        Assert.Equal("offline", reply.Value<string>("reason"));
    }

    [Fact]
    public async Task AReceipt_IsAdoptedAsABuy_AndAppliesItsPrizes()
    {
        var (relay, h, sp, prizes) = Make();
        h.Answer = _ => Json(200, "{\"ok\":true,\"idem\":\"i\",\"itemId\":\"x\",\"paidSp\":40,\"spBefore\":100,\"sp\":60,\"prizes\":{\"revision\":3,\"grants\":[\"cos.x\"]}}");
        await relay.RelayAsync("shop-buy", BuyBody(), "i");
        Assert.Equal(new Adopted("uid-1", true, 60), Assert.Single(sp));
        Assert.Equal(new[] { "cos.x" }, Assert.Single(prizes));
        // The page's wallet frame for the same balance is honoured; a different number is not.
        Assert.False(relay.HandleWalletFrame(5));
        Assert.True(relay.HandleWalletFrame(60));
    }

    [Fact]
    public async Task AnInsufficientRefusal_AdoptsTheServerBalance()
    {
        var (relay, h, sp, _) = Make();
        h.Answer = _ => Json(200, "{\"ok\":false,\"reason\":\"insufficient\",\"sp\":12}");
        await relay.RelayAsync("shop-buy", BuyBody(), "i");
        Assert.Equal(new Adopted("uid-1", true, 12), Assert.Single(sp));
    }

    [Theory]
    [InlineData("shop-state", "{\"ok\":true,\"sp\":10}")]
    [InlineData("shop-buy", "{\"ok\":false,\"reason\":\"busy\",\"sp\":10}")]
    [InlineData("shop-buy", "{\"ok\":false,\"reason\":\"stock_changed\",\"sp\":10}")]
    public async Task EveryOtherBalance_IsASnapshot_ThatMayOnlyRaise(string op, string reply)
    {
        var (relay, h, sp, _) = Make();
        h.Answer = _ => Json(200, reply);
        await relay.RelayAsync(op, BuyBody(), "i");
        Assert.Equal(new Adopted("uid-1", false, 10), Assert.Single(sp));
        Assert.False(relay.HandleWalletFrame(10));   // no receipt: the page cannot move the wallet
    }

    [Fact]
    public void WalletRule_ABuyLowers_ASnapshotNever()
    {
        Assert.True(ConditioningControlPanel.Services.Prizes.V2WalletAdoption.Decide(true, PrizeCounterRelay.IsSettlement("shop-buy", false, "insufficient"), 12, 50, out var n1));
        Assert.Equal(12, n1);
        Assert.False(ConditioningControlPanel.Services.Prizes.V2WalletAdoption.Decide(true, PrizeCounterRelay.IsSettlement("shop-state", true, null), 12, 50, out _));
    }
}
