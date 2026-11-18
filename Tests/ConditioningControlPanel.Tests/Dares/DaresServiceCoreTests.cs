using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>The service over a fake wire and a fake host: the poll, the reports, every side effect.</summary>
public class DaresServiceCoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 30, 0, TimeSpan.Zero);

    private sealed class FakeApi : IDaresApi
    {
        public readonly List<(string Op, JObject Body)> Calls = new();
        public readonly Dictionary<string, Queue<JObject?>> Answers = new();
        public JObject? State = new() { ["ok"] = true, ["me"] = new JObject(), ["bets"] = new JArray() };

        public void Answer(string op, JObject? reply)
        {
            if (!Answers.TryGetValue(op, out var q)) Answers[op] = q = new Queue<JObject?>();
            q.Enqueue(reply);
        }

        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            Calls.Add((op, body));
            if (Answers.TryGetValue(op, out var q) && q.Count > 0) return Task.FromResult(q.Dequeue());
            return Task.FromResult(op == "state" ? State : new JObject { ["ok"] = true });
        }

        public int Count(string op) => Calls.Count(c => c.Op == op);
        public JObject LastBody(string op) => Calls.Last(c => c.Op == op).Body;
    }

    private sealed class FakeHost : IDareHost
    {
        public string? Who = "u_me";
        public bool Chaster = true;
        public int Booked;
        public readonly List<int> Adopted = new();
        public bool Active;
        public TimeSpan Started;
        public string? Account() => Who;
        public int TzMinutes() => 60;
        public bool ChasterReady() => Chaster;
        public int BookLockSeconds(int seconds) { Booked += seconds; return seconds; }
        public void AdoptSp(string account, int sp) => Adopted.Add(sp);
        public bool LockdownActive => Active;
        public bool StartLockdown(TimeSpan duration) { Active = true; Started = duration; return true; }
        public event Action<TimeSpan, TimeSpan>? LockdownEnded;
        public void End(TimeSpan ran) { Active = false; LockdownEnded?.Invoke(Started, ran); }
    }

    private static (DaresService Svc, FakeApi Api, FakeHost Host, DareMeter Meter, DareLockdownKeeper Keeper) Make(
        IDareServeStore? serves = null, FakeHost? host = null)
    {
        var api = new FakeApi();
        host ??= new FakeHost();
        var meter = new DareMeter();
        var keeper = new DareLockdownKeeper(host, serves ?? new DareLockdownKeeper.MemoryStore());
        return (new DaresService(api, host, meter, keeper, () => Now), api, host, meter, keeper);
    }

    private static JObject StateWith(params JObject[] bets) =>
        new() { ["ok"] = true, ["me"] = new JObject { ["discord"] = true }, ["bets"] = new JArray(bets) };

    private static JObject Owed(string stakeKind, int amount) =>
        new JObject(DaresApiCoreTests.BetJson(role: "to", state: "settled", winner: "from", debt: "owed"))
        {
            ["stakes"] = new JObject
            {
                ["from"] = new JObject { ["kind"] = "sp", ["amount"] = 50 },
                ["to"] = new JObject { ["kind"] = stakeKind, ["amount"] = amount },
            },
        };

    [Fact]
    public async Task A_poll_reads_the_state_and_reports_the_receivers_count()
    {
        var (svc, api, _, meter, _) = Make();
        var running = DaresApiCoreTests.BetJson(target: 100);
        running["ends_at"] = "2026-11-18T13:00:00.000Z";
        api.State = StateWith(running);
        var changed = 0;
        svc.Changed += () => changed++;
        await svc.TickAsync();
        Assert.True(svc.Snapshot.Available);
        Assert.True(changed > 0);
        Assert.Equal(1, api.Count("report"));
        Assert.Equal(0, api.LastBody("report").Value<int>("count"));
        Assert.Null(api.LastBody("report")["seen_s"]);

        meter.NotePics(12, Now);
        await svc.TickAsync();
        Assert.Equal(2, api.Count("report"));
        Assert.Equal(12, api.LastBody("report").Value<int>("count"));
        await svc.TickAsync();
        Assert.Equal(2, api.Count("report"));   // nothing moved: nothing sent
        Assert.Null(api.LastBody("state")["lockdown_ok"]);
    }

    [Fact]
    public async Task The_sender_of_a_cant_never_reports()
    {
        var (svc, api, _, _, _) = Make();
        api.State = StateWith(DaresApiCoreTests.BetJson(role: "from", target: 100));
        await svc.TickAsync();
        Assert.Equal(0, api.Count("report"));
    }

    [Fact]
    public async Task Off_hides_everything_and_slows_the_poll()
    {
        var (svc, api, _, _, _) = Make();
        api.State = new JObject { ["ok"] = false, ["reason"] = "off" };
        await svc.TickAsync();
        Assert.False(svc.Snapshot.Available);
        Assert.Equal(DaresPollRule.OffSeconds, svc.NextIntervalSeconds());
    }

    [Fact]
    public void The_cadence_follows_the_friends_poll()
    {
        Assert.Equal(DaresPollRule.IdleCheckSeconds, DaresPollRule.NextIntervalSeconds(false, false, false, true));
        Assert.Equal(20, DaresPollRule.NextIntervalSeconds(true, false, false, true));
        Assert.Equal(120, DaresPollRule.NextIntervalSeconds(true, false, false, false));
        Assert.Equal(120, DaresPollRule.NextIntervalSeconds(true, false, true, true));
    }

    [Fact]
    public async Task Paying_sparkles_adopts_the_debited_balance()
    {
        var (svc, api, host, _, _) = Make();
        api.State = StateWith(Owed("sp", 100));
        await svc.TickAsync();
        api.Answer("pay", new JObject { ["ok"] = true, ["status"] = "paid", ["sp"] = 340 });
        Assert.Equal(DareActResult.Done, await svc.ActAsync("b1", DareOp.Pay));
        Assert.Equal(new[] { 340 }, host.Adopted);
    }

    [Fact]
    public async Task Lock_minutes_book_on_the_tab_first_and_need_a_lock()
    {
        var (svc, api, host, _, _) = Make();
        api.State = StateWith(Owed("lock", 20));
        await svc.TickAsync();
        host.Chaster = false;
        Assert.Equal(DareActResult.NeedsChaster, await svc.ActAsync("b1", DareOp.Pay));
        Assert.Equal(0, api.Count("pay"));

        host.Chaster = true;
        api.Answer("pay", null);   // the wire drops: booked once, never twice
        Assert.Equal(DareActResult.TryLater, await svc.ActAsync("b1", DareOp.Pay));
        Assert.Equal(1200, host.Booked);
        Assert.Equal(DareActResult.Done, await svc.ActAsync("b1", DareOp.Pay));
        Assert.Equal(1200, host.Booked);
        Assert.True(api.LastBody("pay").Value<bool>("booked"));
    }

    [Fact]
    public async Task A_lockdown_debt_runs_the_real_lockdown_and_reports_how_it_ended()
    {
        var (svc, api, host, _, keeper) = Make();
        api.State = StateWith(Owed("lockdown", 10));
        await svc.TickAsync();
        api.Answer("start", new JObject { ["ok"] = true, ["status"] = "serving" });
        Assert.Equal(DareActResult.Done, await svc.ActAsync("b1", DareOp.StartLockdown));
        Assert.Equal(TimeSpan.FromMinutes(10), host.Started);
        Assert.True(keeper.Serving);
        Assert.Equal(DareActResult.LockdownBusy, await svc.ActAsync("b1", DareOp.StartLockdown));

        host.End(TimeSpan.FromMinutes(4));   // the exit phrase
        await Task.Yield();
        Assert.Equal("ended_early", api.LastBody("served").Value<string>("outcome"));
        Assert.Null(keeper.Pending);
    }

    [Fact]
    public async Task A_lockdown_cut_short_by_a_crash_is_reported_at_the_next_start()
    {
        var store = new DareLockdownKeeper.MemoryStore();
        store.Write(new DareServe("u_me", "b7", 20, Now.AddMinutes(-3), null));
        var (svc, api, _, _, keeper) = Make(store);
        Assert.Equal(DareLockdownRule.EndedEarly, keeper.Pending!.Outcome);
        await svc.TickAsync();
        Assert.Equal("b7", api.LastBody("served").Value<string>("id"));
        Assert.Null(store.Read());
    }

    [Fact]
    public void A_full_run_is_served_and_anything_less_ended_early()
    {
        Assert.Equal("served", DareLockdownRule.Outcome(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(2)));
        Assert.Equal("ended_early", DareLockdownRule.Outcome(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(9)));
    }

    [Fact]
    public async Task Create_numbers_the_slip_and_says_what_the_server_said()
    {
        var (svc, api, _, _, _) = Make();
        api.Answer("create", new JObject { ["ok"] = true, ["status"] = "lockdown_needs_sub" });
        var slip = new DareSlip(DareType.Duel, new DareWhat(DareKind.Bubbles, null), DareDeadline.Week,
            new DareStake(DareStakeKind.Lockdown, 30), new DareStake(DareStakeKind.Dare), false);
        var o = await svc.CreateAsync("u_f", slip);
        Assert.Equal(DareCreateStatus.LockdownNeedsSub, o.Status);
        var body = api.LastBody("create");
        Assert.Equal(60, body.Value<int>("tz_min"));
        Assert.Equal(1, body["slip"]!["stakes"]!["to"]!.Value<int>("n"));

        var bad = slip with { What = new DareWhat(DareKind.Session, "hard") };
        Assert.Equal(DareCreateStatus.Refused, (await svc.CreateAsync("u_f", bad)).Status);
        Assert.Equal(1, api.Count("create"));
    }

    [Fact]
    public async Task I_did_it_on_a_running_dare_bet_reports_one()
    {
        var (svc, api, _, _, _) = Make();
        api.State = StateWith(DaresApiCoreTests.BetJson(kind: "dare", target: 1));
        await svc.TickAsync();
        Assert.Equal(0, api.Count("report"));
        Assert.Equal(DareActResult.Done, await svc.ActAsync("b1", DareOp.DidIt));
        Assert.Equal(1, api.LastBody("report").Value<int>("count"));
        Assert.Equal(0, api.Count("did"));
    }

    [Fact]
    public async Task Signed_out_asks_nothing()
    {
        var (svc, api, host, _, _) = Make();
        host.Who = null;
        await svc.TickAsync();
        Assert.Empty(api.Calls);
        Assert.False(svc.Snapshot.Available);
        Assert.Equal(DareActResult.TryLater, await svc.ActAsync("b1", DareOp.Accept));
    }
}
