using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>The dares wire: the door, reading B and every status, the slip out, the numbering.</summary>
public class DaresApiCoreTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Reply = "{\"ok\":true}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Reply, Encoding.UTF8, "application/json") };
        }
    }

    internal static JObject BetJson(string id = "b1", string role = "to", string state = "running", string type = "cant",
        string kind = "pics", object? target = null, string? debt = null, string? winner = null) => new()
    {
        ["id"] = id, ["role"] = role, ["type"] = type, ["state"] = state, ["deadline"] = "3h",
        ["friend"] = new JObject { ["id"] = "u_f", ["name"] = "Fen", ["avatar"] = null },
        ["what"] = target == null ? new JObject { ["kind"] = kind } : new JObject { ["kind"] = kind, ["target"] = JToken.FromObject(target) },
        ["ends_at"] = "2026-11-18T13:00:00.000Z", ["at"] = "2026-11-18T09:00:00.000Z", ["accepted_at"] = "2026-11-18T10:00:00.000Z",
        ["camera"] = false,
        ["stakes"] = new JObject { ["from"] = new JObject { ["kind"] = "sp", ["amount"] = 100 }, ["to"] = new JObject { ["kind"] = "dare", ["n"] = 1 } },
        ["dares"] = new JArray(new JObject { ["n"] = 1, ["by"] = "to" }),
        ["winner"] = winner, ["counts"] = new JObject { ["from"] = 0, ["to"] = 42 },
        ["camera_pass"] = new JObject { ["from"] = null, ["to"] = true },
        ["debt"] = debt == null ? null : new JObject { ["state"] = debt, ["retry_used"] = false, ["ended_early"] = false },
    };

    [Fact]
    public async Task The_door_posts_to_v2_dares_with_the_token_and_the_unified_id()
    {
        var h = new Handler();
        var api = new DaresApi(new HttpClient(h), () => ("u_me", "tok"), "https://x.test");
        var reply = await api.CallAsync("state", new JObject());
        Assert.True(reply!.Value<bool>("ok"));
        Assert.Equal("https://x.test/v2/dares/state", h.Last!.RequestUri!.ToString());
        Assert.Equal("tok", h.Last.Headers.GetValues("X-Auth-Token").Single());
        Assert.Equal("u_me", JObject.Parse(h.Body!).Value<string>("unified_id"));
    }

    [Fact]
    public async Task No_account_sends_nothing_and_429_reads_too_fast()
    {
        var h = new Handler();
        Assert.Null(await new DaresApi(new HttpClient(h), () => null, "https://x.test").CallAsync("state", new JObject()));
        Assert.Null(h.Last);
        h.Status = (HttpStatusCode)429;
        h.Reply = "slow down";
        var r = await new DaresApi(new HttpClient(h), () => ("u", "t"), "https://x.test").CallAsync("state", new JObject());
        Assert.Equal("too_fast", r!.Value<string>("reason"));
    }

    [Fact]
    public void A_bet_reads_whole_and_junk_is_dropped()
    {
        var b = DaresParse.Bet(BetJson(target: 250))!;
        Assert.Equal(DareSide.To, b.Me);
        Assert.Equal("250", b.What.Target);
        Assert.Equal(42, b.ToCount);
        Assert.Equal(DareStakeKind.Dare, b.ToStake.Kind);
        Assert.Equal(1, b.ToStake.DareNumber);
        Assert.Single(b.Dares);
        Assert.True(b.ToCameraPass);
        Assert.Null(b.FromCameraPass);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 13, 0, 0, TimeSpan.Zero), b.EndsAt);

        var junk = BetJson();
        junk["what"]!["kind"] = "lockcards";
        Assert.Null(DaresParse.Bet(junk));

        var state = DaresParse.State(new JObject { ["ok"] = true, ["me"] = new JObject { ["discord"] = true }, ["bets"] = new JArray(BetJson(), junk) })!;
        Assert.True(state.Available);
        Assert.True(state.Me.Discord);
        Assert.Single(state.Bets);
    }

    [Fact]
    public void Who_owes_reads_from_the_winner_and_the_debt()
    {
        var lost = DaresParse.Bet(BetJson(role: "to", state: "settled", winner: "from", debt: "owed"))!;
        Assert.True(lost.IOwe);
        Assert.False(lost.TheyOwe);
        var won = DaresParse.Bet(BetJson(role: "from", state: "settled", winner: "from", debt: "claimed"))!;
        Assert.True(won.TheyOwe);
    }

    [Theory]
    [InlineData("sent", DareCreateStatus.Sent)]
    [InlineData("writing", DareCreateStatus.Writing)]
    [InlineData("lockdown_needs_sub", DareCreateStatus.LockdownNeedsSub)]
    [InlineData("friend_banned", DareCreateStatus.FriendBanned)]
    [InlineData("discord_needed", DareCreateStatus.DiscordNeeded)]
    [InlineData("something_new", DareCreateStatus.Refused)]
    public void Create_statuses_map(string wire, DareCreateStatus want)
    {
        var o = DaresParse.Create(new JObject { ["ok"] = true, ["status"] = wire, ["id"] = "b9", ["write_code"] = "ABC123" });
        Assert.Equal(want, o.Status);
        Assert.Equal("ABC123", o.WriteCode);
        Assert.Equal(DareCreateStatus.Off, DaresParse.Create(new JObject { ["ok"] = false, ["reason"] = "off" }).Status);
        Assert.Equal(DareCreateStatus.TryLater, DaresParse.Create(null).Status);
        Assert.Equal(DareCreateStatus.Refused, DaresParse.Create(new JObject { ["ok"] = false, ["reason"] = "bad_input" }).Status);
    }

    [Theory]
    [InlineData("running", DareActResult.Done)]
    [InlineData("paid", DareActResult.Done)]
    [InlineData("gone", DareActResult.Gone)]
    [InlineData("lockdown_busy", DareActResult.LockdownBusy)]
    [InlineData("lockdown_needs_sub", DareActResult.LockdownNeedsSub)]
    [InlineData("refused", DareActResult.Refused)]
    public void Act_statuses_map(string wire, DareActResult want) =>
        Assert.Equal(want, DaresParse.Act(new JObject { ["ok"] = true, ["status"] = wire }));

    [Fact]
    public void A_slip_goes_out_without_any_text_and_with_its_numbers()
    {
        var slip = new DareSlip(DareType.Cant, new DareWhat(DareKind.Dare, null), DareDeadline.Today,
            new DareStake(DareStakeKind.Dare), new DareStake(DareStakeKind.Lockdown, 20), Camera: true);
        var n = DareSlipCheck.Numbered(slip);
        Assert.Equal("1", n.What.Target);
        Assert.Equal(2, n.FromStake.DareNumber);
        Assert.False(n.Camera);
        Assert.True(DareSlipCheck.IsValid(n));
        var wire = DaresParse.SlipToWire(n);
        Assert.Equal(1, wire["what"]!.Value<int>("target"));
        Assert.Equal(2, wire["stakes"]!["from"]!.Value<int>("n"));
        Assert.Equal(20, wire["stakes"]!["to"]!.Value<int>("amount"));
        Assert.DoesNotContain("text", wire.ToString());
        var seals = DareSlipCheck.Seals(slip);
        Assert.Equal(new[] { new DareSeal(1, DareSide.To), new DareSeal(2, DareSide.From) }, seals);
    }

    [Fact]
    public void Off_grammar_slips_are_refused_locally()
    {
        var ok = new DareSlip(DareType.Cant, new DareWhat(DareKind.Pics, "250"), DareDeadline.ThreeHours,
            new DareStake(DareStakeKind.Sp, 100), new DareStake(DareStakeKind.Lock, 10), true);
        Assert.True(DareSlipCheck.IsValid(ok));
        Assert.False(DareSlipCheck.IsValid(ok with { What = new DareWhat(DareKind.Pics, "251") }));
        Assert.False(DareSlipCheck.IsValid(ok with { FromStake = new DareStake(DareStakeKind.Sp, 99) }));
        Assert.False(DareSlipCheck.IsValid(ok with { What = new DareWhat(DareKind.Bubbles, null) }));
        Assert.False(DareSlipCheck.IsValid(ok with { Type = DareType.Duel }));
        Assert.True(DareSlipCheck.IsValid(ok with { Type = DareType.Duel, What = new DareWhat(DareKind.Bubbles, null), Camera = false }));
        Assert.False(DareSlipCheck.LockdownTierOk(0));
        Assert.True(DareSlipCheck.LockdownTierOk(1));
    }
}
