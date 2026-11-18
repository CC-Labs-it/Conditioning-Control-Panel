using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>
/// The REAL client wire (DaresApi + DaresService) against the REAL server routes (CCP-Server
/// dares-routes.js on an in-memory Redis, run by a local harness outside this repo). Skipped unless
/// env <c>CCP_DARES_E2E</c> names the harness base url. Players: ann (Prime), bea (free), cat (Basic);
/// friends ann-bea and ann-cat; every player starts on 1000 Sparkles. Each test resets the clock.
/// </summary>
public class DaresWireE2ETests
{
    private static readonly string? Base = Environment.GetEnvironmentVariable("CCP_DARES_E2E");
    private static readonly HttpClient Http = new();
    private const string Ann = "u_ann00001", Bea = "u_bea00002", Cat = "u_cat00003";
    private const string BotToken = "local-bot-token-0123456789-0123456789-0123";

    private long _clockMs;

    private sealed class Host : IDareHost
    {
        public readonly string Who;
        public bool Chaster = true;
        public int Booked;
        public int? Adopted;
        public bool Active;
        public TimeSpan Started;
        public Host(string who) { Who = who; }
        public string? Account() => Who;
        public int TzMinutes() => 0;
        public bool ChasterReady() => Chaster;
        public int BookLockSeconds(int seconds) { Booked += seconds; return seconds; }
        public void AdoptSp(string account, int sp) => Adopted = sp;
        public bool LockdownActive => Active;
        public bool StartLockdown(TimeSpan duration) { Active = true; Started = duration; return true; }
        public event Action<TimeSpan, TimeSpan>? LockdownEnded;
        public void End(TimeSpan ran) { Active = false; LockdownEnded?.Invoke(Started, ran); }
    }

    private sealed record Player(DaresService Svc, Host Host, DareMeter Meter);

    private Player Make(string uid)
    {
        var host = new Host(uid);
        var meter = new DareMeter();
        var keeper = new DareLockdownKeeper(host, new DareLockdownKeeper.MemoryStore());
        var api = new DaresApi(Http, () => (uid, "tok-" + uid), Base);
        return new Player(new DaresService(api, host, meter, keeper, Now), host, meter);
    }

    private DateTimeOffset Now() => DateTimeOffset.FromUnixTimeMilliseconds(_clockMs);

    private static async Task<JObject> Door(string path, JObject body)
    {
        using var res = await Http.PostAsync($"{Base}{path}", new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
        return JObject.Parse(await res.Content.ReadAsStringAsync());
    }

    private async Task Start()
    {
        if (string.IsNullOrEmpty(Base)) Assert.Skip("Set CCP_DARES_E2E to the local dares harness base url.");
        _clockMs = (await Door("/__reset", new JObject())).Value<long>("now_ms");
    }

    private async Task Advance(TimeSpan by) =>
        _clockMs = (await Door("/__clock", new JObject { ["advance_ms"] = (long)by.TotalMilliseconds })).Value<long>("now_ms");

    private static async Task<int> Sp(string uid) => (await Door("/__user/get", new JObject { ["uid"] = uid })).Value<int>("skill_points");

    private static DareSlip Slip(DareType type, DareKind kind, string? target, DareStake from, DareStake to) =>
        new(type, new DareWhat(kind, target), DareDeadline.ThreeHours, from, to, Camera: false);

    private static DareBet BetOf(Player p, string id) => p.Svc.Snapshot.Bets.Single(b => b.Id == id);

    [Fact]
    public async Task A_cant_bet_on_pictures_runs_settles_and_pays_Sparkles_both_ways()
    {
        await Start();
        var ann = Make(Ann); var bea = Make(Bea);
        int annSp = await Sp(Ann), beaSp = await Sp(Bea);

        var sent = await ann.Svc.CreateAsync(Bea, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 100), new DareStake(DareStakeKind.Sp, 50)));
        Assert.Equal(DareCreateStatus.Sent, sent.Status);
        Assert.False(string.IsNullOrEmpty(sent.Id));

        await bea.Svc.TickAsync();
        var offer = BetOf(bea, sent.Id!);
        Assert.True(offer.IsOfferToMe);
        Assert.Equal("ANN", offer.Friend.Name);
        Assert.Equal(DareStakeKind.Sp, offer.MyStake.Kind);
        Assert.Equal(50, offer.MyStake.Amount);

        Assert.Equal(DareActResult.Done, await bea.Svc.ActAsync(sent.Id!, DareOp.Accept));
        Assert.Equal(DareState.Running, BetOf(bea, sent.Id!).State);

        // Bea sees 260 pictures inside the window; her client reports it on the next poll.
        await Advance(TimeSpan.FromMinutes(10));
        bea.Meter.NotePics(260, Now());
        await bea.Svc.TickAsync();
        await ann.Svc.TickAsync();
        Assert.Equal(260, BetOf(ann, sent.Id!).TheirCount);

        await Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5));
        await ann.Svc.TickAsync();
        await bea.Svc.TickAsync();
        var annView = BetOf(ann, sent.Id!);
        Assert.Equal(DareState.Settled, annView.State);
        Assert.Equal(DareSide.To, annView.Winner);
        Assert.True(annView.IOwe);
        Assert.True(BetOf(bea, sent.Id!).TheyOwe);

        Assert.Equal(DareActResult.Done, await ann.Svc.ActAsync(sent.Id!, DareOp.Pay));
        Assert.Equal(annSp - 100, ann.Host.Adopted);
        Assert.Equal(annSp - 100, await Sp(Ann));
        Assert.Equal(beaSp + 100, await Sp(Bea));
        Assert.False(BetOf(ann, sent.Id!).IOwe);
        await bea.Svc.TickAsync();
        Assert.False(BetOf(bea, sent.Id!).TheyOwe);
        // A second press is never a second payment.
        await ann.Svc.ActAsync(sent.Id!, DareOp.Pay);
        Assert.Equal(annSp - 100, await Sp(Ann));
    }

    [Fact]
    public async Task Pass_and_cancel_close_an_offer_and_nothing_moves()
    {
        await Start();
        var ann = Make(Ann); var bea = Make(Bea);
        int annSp = await Sp(Ann);
        var a = await ann.Svc.CreateAsync(Bea, Slip(DareType.Cant, DareKind.Minutes, DareGrammar.TargetsFor(DareKind.Minutes).First(), new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Sp, 50)));
        var b = await ann.Svc.CreateAsync(Bea, Slip(DareType.Cant, DareKind.Minutes, DareGrammar.TargetsFor(DareKind.Minutes).First(), new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Sp, 50)));
        Assert.Equal(DareCreateStatus.Sent, a.Status);
        Assert.Equal(DareCreateStatus.Sent, b.Status);
        await bea.Svc.TickAsync();
        Assert.Equal(DareActResult.Done, await bea.Svc.ActAsync(a.Id!, DareOp.Pass));
        Assert.Equal(DareActResult.Done, await ann.Svc.ActAsync(b.Id!, DareOp.Cancel));
        await ann.Svc.TickAsync();
        Assert.Equal(DareState.Passed, BetOf(ann, a.Id!).State);
        Assert.Equal(DareState.Cancelled, BetOf(ann, b.Id!).State);
        Assert.Equal(annSp, await Sp(Ann));
    }

    [Fact]
    public async Task A_free_player_cannot_stake_or_be_asked_for_a_Lockdown_and_a_Basic_one_can()
    {
        await Start();
        var ann = Make(Ann); var bea = Make(Bea);
        var lockdown = new DareStake(DareStakeKind.Lockdown, 20);
        var toFree = await ann.Svc.CreateAsync(Bea, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), lockdown));
        Assert.Equal(DareCreateStatus.LockdownNeedsSub, toFree.Status);
        var fromFree = await bea.Svc.CreateAsync(Ann, Slip(DareType.Cant, DareKind.Pics, "250", lockdown, new DareStake(DareStakeKind.Sp, 50)));
        Assert.Equal(DareCreateStatus.LockdownNeedsSub, fromFree.Status);
        var toBasic = await ann.Svc.CreateAsync(Cat, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), lockdown));
        Assert.Equal(DareCreateStatus.Sent, toBasic.Status);
    }

    [Fact]
    public async Task A_lost_Lockdown_stake_runs_the_real_Lockdown_and_one_early_end_gets_one_retry()
    {
        await Start();
        var ann = Make(Ann); var cat = Make(Cat);
        var sent = await ann.Svc.CreateAsync(Cat, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Lockdown, 20)));
        Assert.Equal(DareCreateStatus.Sent, sent.Status);
        await cat.Svc.TickAsync();
        Assert.Equal(DareActResult.Done, await cat.Svc.ActAsync(sent.Id!, DareOp.Accept));
        await Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5));   // cat saw nothing: ann wins
        await cat.Svc.TickAsync();
        Assert.True(BetOf(cat, sent.Id!).IOwe);

        Assert.Equal(DareActResult.Done, await cat.Svc.ActAsync(sent.Id!, DareOp.Pay));
        Assert.True(cat.Host.Active);
        Assert.Equal(TimeSpan.FromMinutes(20), cat.Host.Started);
        await cat.Svc.TickAsync();
        Assert.Equal(DareDebtState.Serving, BetOf(cat, sent.Id!).Debt?.State);

        // Ended after 3 minutes: owed again, retry used.
        cat.Host.End(TimeSpan.FromMinutes(3));
        await Task.Delay(300);
        await cat.Svc.TickAsync();
        var again = BetOf(cat, sent.Id!);
        Assert.True(again.IOwe);
        Assert.True(again.Debt?.RetryUsed);

        Assert.Equal(DareActResult.Done, await cat.Svc.ActAsync(sent.Id!, DareOp.Pay));
        cat.Host.End(TimeSpan.FromMinutes(20));
        await Task.Delay(300);
        await cat.Svc.TickAsync();
        Assert.False(BetOf(cat, sent.Id!).IOwe);
        Assert.Equal(DareDebtState.Paid, BetOf(cat, sent.Id!).Debt?.State);
    }

    [Fact]
    public async Task A_lost_Lockdown_ended_early_twice_reads_ended_early()
    {
        await Start();
        var ann = Make(Ann); var cat = Make(Cat);
        var sent = await ann.Svc.CreateAsync(Cat, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Lockdown, 20)));
        await cat.Svc.TickAsync();
        await cat.Svc.ActAsync(sent.Id!, DareOp.Accept);
        await Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5));
        await cat.Svc.TickAsync();
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(DareActResult.Done, await cat.Svc.ActAsync(sent.Id!, DareOp.Pay));
            cat.Host.End(TimeSpan.FromMinutes(2));
            await Task.Delay(300);
            await cat.Svc.TickAsync();
        }
        var end = BetOf(cat, sent.Id!);
        Assert.False(end.IOwe);
        Assert.True(end.Debt?.EndedEarly);
    }

    [Fact]
    public async Task A_lost_lock_minutes_stake_books_on_the_tab_then_pays()
    {
        await Start();
        var ann = Make(Ann); var cat = Make(Cat);
        var sent = await ann.Svc.CreateAsync(Cat, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Lock, 20)));
        Assert.Equal(DareCreateStatus.Sent, sent.Status);
        await cat.Svc.TickAsync();
        await cat.Svc.ActAsync(sent.Id!, DareOp.Accept);
        await Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5));
        await cat.Svc.TickAsync();
        Assert.True(BetOf(cat, sent.Id!).IOwe);

        cat.Host.Chaster = false;
        Assert.Equal(DareActResult.NeedsChaster, await cat.Svc.ActAsync(sent.Id!, DareOp.Pay));
        Assert.Equal(0, cat.Host.Booked);
        cat.Host.Chaster = true;
        Assert.Equal(DareActResult.Done, await cat.Svc.ActAsync(sent.Id!, DareOp.Pay));
        Assert.Equal(20 * 60, cat.Host.Booked);
        Assert.False(BetOf(cat, sent.Id!).IOwe);
    }

    [Fact]
    public async Task Renounce_clears_the_debt_and_bans_dares_for_seven_days()
    {
        await Start();
        var ann = Make(Ann); var bea = Make(Bea);
        var sent = await ann.Svc.CreateAsync(Bea, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Sp, 50)));
        await bea.Svc.TickAsync();
        await bea.Svc.ActAsync(sent.Id!, DareOp.Accept);
        await Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5));
        await bea.Svc.TickAsync();
        Assert.True(BetOf(bea, sent.Id!).IOwe);
        int beaSp = await Sp(Bea);

        Assert.Equal(DareActResult.Done, await bea.Svc.ActAsync(sent.Id!, DareOp.Renounce));
        Assert.Equal(beaSp, await Sp(Bea));
        var me = bea.Svc.Snapshot.Me;
        Assert.NotNull(me.BannedUntil);
        Assert.InRange((me.BannedUntil!.Value - Now()).TotalDays, 6.9, 7.1);
        Assert.Equal(DareCreateStatus.Banned, (await bea.Svc.CreateAsync(Ann, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Sp, 50)))).Status);
        await ann.Svc.TickAsync();
        Assert.Equal(DareSide.To, BetOf(ann, sent.Id!).RenouncedBy);
        await Advance(TimeSpan.FromDays(8));   // the ban ends on its own
    }

    [Fact]
    public async Task A_duel_on_bubbles_counts_both_sides_and_the_bigger_count_wins()
    {
        await Start();
        var ann = Make(Ann); var bea = Make(Bea);
        var sent = await ann.Svc.CreateAsync(Bea, Slip(DareType.Duel, DareKind.Bubbles, null, new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Sp, 50)));
        Assert.Equal(DareCreateStatus.Sent, sent.Status);
        await bea.Svc.TickAsync();
        await bea.Svc.ActAsync(sent.Id!, DareOp.Accept);
        await ann.Svc.TickAsync();
        await Advance(TimeSpan.FromMinutes(5));
        ann.Meter.NoteBubbles(40, Now());
        bea.Meter.NoteBubbles(25, Now());
        await ann.Svc.TickAsync();
        await bea.Svc.TickAsync();
        await Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5));
        await ann.Svc.TickAsync();
        await bea.Svc.TickAsync();
        var view = BetOf(bea, sent.Id!);
        Assert.Equal(DareState.Settled, view.State);
        Assert.Equal(40, view.TheirCount);
        Assert.Equal(25, view.MyCount);
        Assert.Equal(DareSide.From, view.Winner);
        Assert.True(view.IOwe);
    }

    [Fact]
    public async Task A_dare_stake_is_written_in_Discord_then_the_loser_says_I_did_it_and_the_winner_ticks()
    {
        await Start();
        await Door("/__user", new JObject { ["uid"] = Ann, ["patch"] = new JObject { ["discord_id"] = "100000000000000001" } });
        await Door("/__user", new JObject { ["uid"] = Bea, ["patch"] = new JObject { ["discord_id"] = "100000000000000002" } });
        var ann = Make(Ann); var bea = Make(Bea);
        var made = await ann.Svc.CreateAsync(Bea, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Dare)));
        Assert.Equal(DareCreateStatus.Writing, made.Status);
        Assert.False(string.IsNullOrEmpty(made.WriteCode));

        // The bot writes the dare's words (what /bet write code: does).
        using (var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/admin/dares/bot/write"))
        {
            req.Headers.Add("X-Dares-Token", BotToken);
            req.Content = new StringContent(new JObject { ["discord_id"] = "100000000000000001", ["code"] = made.WriteCode, ["texts"] = new JArray("sing the pink song") }.ToString(), Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req);
            var body = JObject.Parse(await res.Content.ReadAsStringAsync());
            Assert.True(body.Value<bool?>("ok") == true, body.ToString());
        }

        await bea.Svc.TickAsync();
        var offer = BetOf(bea, made.Id!);
        Assert.True(offer.IsOfferToMe);
        Assert.Equal(DareStakeKind.Dare, offer.MyStake.Kind);
        Assert.Single(offer.Dares);
        await bea.Svc.ActAsync(made.Id!, DareOp.Accept);
        await Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(5));
        await bea.Svc.TickAsync();
        Assert.True(BetOf(bea, made.Id!).IOwe);

        Assert.Equal(DareActResult.Done, await bea.Svc.ActAsync(made.Id!, DareOp.DidIt));
        await ann.Svc.TickAsync();
        Assert.Equal(DareDebtState.Claimed, BetOf(ann, made.Id!).Debt?.State);
        Assert.Equal(DareActResult.Done, await ann.Svc.ActAsync(made.Id!, DareOp.Tick));
        await bea.Svc.TickAsync();
        Assert.Equal(DareDebtState.Paid, BetOf(bea, made.Id!).Debt?.State);
    }

    [Fact]
    public async Task Allow_mode_hides_everything_from_an_unlisted_player()
    {
        await Start();
        await Door("/__flag", new JObject { ["mode"] = "allow", ["allow"] = new JArray(Ann, Cat) });
        try
        {
            var ann = Make(Ann); var bea = Make(Bea);
            await ann.Svc.TickAsync();
            await bea.Svc.TickAsync();
            Assert.True(ann.Svc.Snapshot.Available);
            Assert.False(bea.Svc.Snapshot.Available);
            Assert.Equal(DaresPollRule.OffSeconds, bea.Svc.NextIntervalSeconds());
            Assert.NotEqual(DareCreateStatus.Sent, (await ann.Svc.CreateAsync(Bea, Slip(DareType.Cant, DareKind.Pics, "250", new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Sp, 50)))).Status);
        }
        finally { await Door("/__flag", new JObject { ["mode"] = "on" }); }
    }
}
