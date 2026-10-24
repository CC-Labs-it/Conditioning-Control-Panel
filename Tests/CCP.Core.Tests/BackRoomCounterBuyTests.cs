using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The standalone v2 buy now in Core (row feat-backroom-counter-buy): V2PurchaseService over the
/// real BackRoomApi through a fake handler. Never reaches a server.</summary>
public sealed class BackRoomCounterBuyTests
{
    private const string State = "{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"sp\":50,\"catalog\":[{\"id\":\"flashes_v2\",\"priceSp\":30,\"sale\":\"on\"}]}";
    private const string Bought = "{\"ok\":true,\"sp\":20,\"prizes\":{\"revision\":7,\"grants\":[\"fx.flash.drift_bounce\",\"fx.flash.pendulum\"]}}";

    internal sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string? Body)> Seen = new();
        public readonly Queue<Func<HttpResponseMessage>> Replies = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.Method.Method, r.RequestUri!.ToString(), r.Content == null ? null : await r.Content.ReadAsStringAsync(ct)));
            return Replies.TryDequeue(out var next) ? next() : throw new HttpRequestException("no scripted reply");
        }
        public void Reply(string json, HttpStatusCode code = HttpStatusCode.OK) =>
            Replies.Enqueue(() => new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private static (V2PurchaseService Svc, Fake H, List<(string, bool, int)> Adopted, List<string[]> Applied) Make(int sp = 50)
    {
        var h = new Fake();
        var adopted = new List<(string, bool, int)>();
        var applied = new List<string[]>();
        var granted = new HashSet<string>();
        var api = new BackRoomApi(new HttpClient(h), () => ("u-1", "t-1"), adoptSp: null,
            (_, _, g) => { applied.Add(g); granted.UnionWith(g); }, "http://127.0.0.1:9");
        var svc = new V2PurchaseService(api, () => "u-1", id => V2PurchaseRule.OwnsPrize(id, granted.Contains), () => sp,
            (a, fromBuy, v) => adopted.Add((a, fromBuy, v)));
        return (svc, h, adopted, applied);
    }

    [Fact]
    public async Task Buy_reads_the_counter_then_buys_at_its_version_with_a_kept_idem_and_adopts_the_receipt()
    {
        var (svc, h, adopted, applied) = Make();
        h.Reply(State);
        h.Reply(Bought);
        var bought = new List<string>();
        svc.Bought += bought.Add;

        Assert.True(await svc.BuyAsync(V2PurchaseRule.FlashesPrizeId, 30));

        Assert.Equal(("GET", "http://127.0.0.1:9/v2/backroom/counter/state?unified_id=u-1", (string?)null), h.Seen[0]);
        Assert.Equal("http://127.0.0.1:9/v2/backroom/counter/buy", h.Seen[1].Url);
        var body = JObject.Parse(h.Seen[1].Body!);
        Assert.Equal("flashes_v2", (string?)body["prizeId"]);
        Assert.Equal(4, (int?)body["catalogVersion"]);
        Assert.Equal("u-1", (string?)body["unified_id"]);
        Assert.Equal(32, ((string?)body["idem"])?.Length);
        // The state read may only raise (fromBuy false); the receipt settles (fromBuy true).
        Assert.Equal(new[] { ("u-1", false, 50), ("u-1", true, 20) }, adopted);
        Assert.Equal(new[] { "fx.flash.drift_bounce", "fx.flash.pendulum" }, Assert.Single(applied));
        Assert.Equal(new[] { "flashes_v2" }, bought);
        Assert.Equal(V2PurchaseRowState.Hidden, svc.RowFor(V2PurchaseRule.FlashesPrizeId).State);
    }

    [Fact]
    public async Task A_price_the_user_did_not_confirm_is_never_sent()
    {
        var (svc, h, _, _) = Make();
        h.Reply(State);
        Assert.False(await svc.BuyAsync(V2PurchaseRule.FlashesPrizeId, 25));
        Assert.Single(h.Seen);   // the state read only, no buy
        var row = svc.RowFor(V2PurchaseRule.FlashesPrizeId);
        Assert.Equal((V2PurchaseRowState.Failed, "v2_get_error_changed", 30), (row.State, row.MessageKey, row.PriceSp));
    }

    [Fact]
    public async Task Insufficient_and_network_failure_put_WPFs_keys_on_the_row_and_the_retry_replays_the_idem()
    {
        var (svc, h, adopted, _) = Make();
        h.Reply(State);
        h.Reply("{\"ok\":false,\"reason\":\"insufficient\",\"sp\":10}", HttpStatusCode.PaymentRequired);
        Assert.False(await svc.BuyAsync(V2PurchaseRule.FlashesPrizeId, 30));
        Assert.Equal("v2_get_error_sp", svc.RowFor(V2PurchaseRule.FlashesPrizeId).MessageKey);
        Assert.Equal(("u-1", false, 10), adopted[^1]);   // a refusal is a snapshot: may only raise

        h.Replies.Enqueue(() => throw new HttpRequestException("down"));
        h.Replies.Enqueue(() => throw new HttpRequestException("down"));
        Assert.False(await svc.BuyAsync(V2PurchaseRule.FlashesPrizeId, 30));
        Assert.Equal("v2_get_error_offline", svc.RowFor(V2PurchaseRule.FlashesPrizeId).MessageKey);
        var idems = new HashSet<string?>();
        foreach (var s in h.Seen) if (s.Method == "POST") idems.Add((string?)JObject.Parse(s.Body!)["idem"]);
        Assert.Single(idems);   // three POSTs (one buy, one buy + network retry), one key
    }

    [Fact]
    public async Task Head_hooks_receive_prizes_and_every_reply_when_the_caller_names_none()
    {
        var (apply, reply) = (BackRoomApi.DefaultApplyPrizes, BackRoomApi.MergedReply);
        try
        {
            var applied = new List<(string, long)>();
            var replies = new List<int>();
            BackRoomApi.DefaultApplyPrizes = (a, rev, _) => applied.Add((a, rev));
            BackRoomApi.MergedReply = (code, _) => { replies.Add(code); return false; };
            var h = new Fake();
            h.Reply(Bought);
            var res = await new BackRoomApi(new HttpClient(h), () => ("u-1", "t-1"), null, baseUrl: "http://127.0.0.1:9")
                .RelayAsync("counter", "buy", "k", new JObject { ["prizeId"] = "flashes_v2" });
            Assert.True(res.Ok);
            Assert.Equal(new[] { ("u-1", 7L) }, applied);
            Assert.Equal(new[] { 200 }, replies);
        }
        finally { (BackRoomApi.DefaultApplyPrizes, BackRoomApi.MergedReply) = (apply, reply); }
    }
}
