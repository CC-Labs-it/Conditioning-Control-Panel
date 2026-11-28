using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Sparkles;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>T&amp;C sticker Sparkles (SPARKLES-WIRE.md, 2026-11-25): which answers reach the page, the one retry,
/// and the queue that sends a burst of stickers one claim at a time.</summary>
public class TcStickerClaimsTests
{
    private static SparklesClaimOutcome Paid(string id, int credited, bool shadow = false, string? reason = null) => new(
        SparklesReceipt.Parse(JObject.Parse(
            "{\"ok\":true,\"receipt\":{\"kind\":\"place\",\"game\":\"tc\",\"mode\":\"sticker\"," +
            "\"result\":{\"kind\":\"sticker\",\"id\":\"" + id + "\",\"tier\":\"hard\"},\"base\":40,\"online\":1,\"taper\":1," +
            "\"fee\":false,\"paid\":" + (reason == null ? 40 : 0) + ",\"credited\":" + credited + ",\"reason\":" +
            (reason == null ? "null" : "\"" + reason + "\"") + ",\"sp\":540,\"shadow\":" + (shadow ? "true" : "false") + "}}")),
        null, Adopted: credited > 0 && !shadow);

    private static SparklesClaimOutcome Refused(string why) => SparklesClaimOutcome.Refused(why);

    // ---- rules ----

    [Fact]
    public void Only_a_real_credit_reaches_the_page()
    {
        Assert.True(TcStickerRules.ShouldPost(Paid("ac_5k", 40)));
        Assert.False(TcStickerRules.ShouldPost(Paid("ac_5k", 0, reason: "already")));
        Assert.False(TcStickerRules.ShouldPost(Paid("ac_5k", 0, reason: "unknown")));
        Assert.False(TcStickerRules.ShouldPost(Paid("ac_5k", 0, reason: "retired")));
        Assert.False(TcStickerRules.ShouldPost(Paid("ac_5k", 0, reason: "sticker_cap")));
        Assert.False(TcStickerRules.ShouldPost(Paid("ac_5k", 40, shadow: true)));
        Assert.False(TcStickerRules.ShouldPost(Refused("mode")));
        Assert.False(TcStickerRules.ShouldPost(Refused("signin")));
        Assert.False(TcStickerRules.ShouldPost(null));
    }

    [Fact]
    public void Quiet_answers_have_one_key_each()
    {
        Assert.Equal("already", TcStickerRules.QuietKey(Paid("a", 0, reason: "already")));
        Assert.Equal("sticker_cap", TcStickerRules.QuietKey(Paid("a", 0, reason: "sticker_cap")));
        Assert.Equal("shadow", TcStickerRules.QuietKey(Paid("a", 40, shadow: true)));
        Assert.Equal("zero", TcStickerRules.QuietKey(Paid("a", 0)));
        Assert.Equal("mode", TcStickerRules.QuietKey(Refused("mode")));
        Assert.Equal("no-service", TcStickerRules.QuietKey(null));
    }

    [Fact]
    public void Only_too_fast_earns_a_retry_and_only_a_cap_or_refusal_may_be_sent_again()
    {
        Assert.True(TcStickerRules.ShouldRetry(Refused("too_fast")));
        Assert.False(TcStickerRules.ShouldRetry(Refused("busy")));      // the claim service already asked again
        Assert.False(TcStickerRules.ShouldRetry(Refused("disabled")));
        Assert.False(TcStickerRules.ShouldRetry(Paid("a", 0, reason: "already")));
        Assert.True(TcStickerRules.IsFinal(Paid("a", 40)));
        Assert.True(TcStickerRules.IsFinal(Paid("a", 0, reason: "already")));
        Assert.False(TcStickerRules.IsFinal(Paid("a", 0, reason: "sticker_cap")));
        Assert.False(TcStickerRules.IsFinal(Refused("mode")));
        Assert.False(TcStickerRules.IsFinal(null));
    }

    [Fact]
    public void A_sticker_claim_is_the_wire_shape_with_a_fresh_id()
    {
        var a = TcStickerRules.Claim("ac_5k");
        var b = TcStickerRules.Claim("ac_5k");
        Assert.True(a.IsWellFormed);
        Assert.NotEqual(a.ClaimId, b.ClaimId);
        Assert.Equal(("tc", "sticker", 0L, "ac_5k"), (a.Game, a.Mode, a.DurationMs, a.Result.Id));
    }

    [Fact]
    public void A_credited_sticker_slip_has_the_stamp_and_no_result_line()
    {
        var msg = SparklesSlip.Build(Paid("ac_5k", 40), gentle: true, k => k);
        Assert.NotNull(msg);
        Assert.Equal("sparkles-receipt", (string?)msg!["type"]);
        Assert.Equal(40, (int)msg["credited"]!);
        Assert.Equal(540, (int)msg["sp"]!);
        Assert.Equal("Sparkles_Slip_Paid", (string?)msg["stamp"]);
        Assert.True((bool)msg["gentle"]!);
        Assert.Empty((JArray)msg["lines"]!);
        Assert.Null(msg["medal"]);
    }

    // ---- the queue ----

    private sealed class Rig
    {
        public readonly ConcurrentQueue<SparklesClaim> Sent = new();
        public readonly ConcurrentQueue<TimeSpan> Waits = new();
        public readonly ConcurrentQueue<TcStickerQueue.Settled> Settled = new();
        public Func<SparklesClaim, int, SparklesClaimOutcome?> Answer = (c, _) => Paid(c.Result.Id!, 40);
        public int InFlight, MaxInFlight, Calls;
        public readonly TcStickerQueue Queue;

        public Rig()
        {
            Queue = new TcStickerQueue(async c =>
            {
                var now = Interlocked.Increment(ref InFlight);
                lock (this) MaxInFlight = Math.Max(MaxInFlight, now);
                var n = Interlocked.Increment(ref Calls);
                Sent.Enqueue(c);
                await Task.Delay(2);
                Interlocked.Decrement(ref InFlight);
                return Answer(c, n);
            }, t => { Waits.Enqueue(t); return Task.CompletedTask; }, s => Settled.Enqueue(s));
        }

        public async Task Drain()
        {
            for (var i = 0; i < 200; i++)
            {
                await Queue.WhenIdle;
                await Task.Delay(5);
                if (Queue.WhenIdle.IsCompleted) return;
            }
        }
    }

    [Fact]
    public async Task A_burst_goes_out_one_claim_at_a_time_with_a_gap()
    {
        var rig = new Rig();
        var owner = new object();
        foreach (var id in new[] { "ac_5k", "e_drop", "n_decline", "n_terms50", "t_all" })
            Assert.True(rig.Queue.Offer(id, owner));
        await rig.Drain();

        Assert.Equal(1, rig.MaxInFlight);
        Assert.Equal(new[] { "ac_5k", "e_drop", "n_decline", "n_terms50", "t_all" }, rig.Sent.Select(c => c.Result.Id));
        Assert.Equal(5, rig.Sent.Select(c => c.ClaimId).Distinct().Count());
        Assert.All(rig.Waits, w => Assert.Equal(TcStickerRules.Gap, w));
        Assert.Equal(5, rig.Settled.Count);
        Assert.All(rig.Settled, s => { Assert.True(s.Post); Assert.Same(owner, s.Owner); });
    }

    [Fact]
    public async Task A_repeat_is_dropped_until_a_cap_or_refusal_frees_it()
    {
        var rig = new Rig();
        Assert.True(rig.Queue.Offer("ac_5k", null));
        Assert.False(rig.Queue.Offer("ac_5k", null));      // waiting or in flight
        await rig.Drain();
        Assert.False(rig.Queue.Offer("ac_5k", null));      // paid: settled for this window
        Assert.False(rig.Queue.Offer("AC_5K", null));      // malformed
        Assert.False(rig.Queue.Offer(null, null));

        rig.Answer = (c, _) => Paid(c.Result.Id!, 0, reason: "sticker_cap");
        Assert.True(rig.Queue.Offer("e_drop", null));
        await rig.Drain();
        Assert.True(rig.Queue.Offer("e_drop", null));      // the cap is per UTC day: it may go again
        await rig.Drain();
        Assert.Equal(3, rig.Calls);
    }

    [Fact]
    public async Task Too_fast_is_asked_once_more_with_the_same_claim_id()
    {
        var rig = new Rig { Answer = (_, _) => Refused("too_fast") };
        Assert.True(rig.Queue.Offer("ac_5k", null));
        await rig.Drain();

        var sent = rig.Sent.ToList();
        Assert.Equal(2, sent.Count);
        Assert.Equal(sent[0].ClaimId, sent[1].ClaimId);
        Assert.Equal(TcStickerRules.RetryWait, rig.Waits.First());
        var settled = Assert.Single(rig.Settled);
        Assert.False(settled.Post);
        Assert.Equal("too_fast", TcStickerRules.QuietKey(settled.Outcome));

        // Paid on the retry: posted.
        rig.Answer = (c, n) => n % 2 == 1 ? Refused("too_fast") : Paid(c.Result.Id!, 15);
        Assert.True(rig.Queue.Offer("e_drop", null));
        await rig.Drain();
        Assert.True(rig.Settled.Last().Post);
    }

    [Fact]
    public async Task Quiet_answers_are_flagged_for_the_log_once_a_window()
    {
        var rig = new Rig { Answer = (c, _) => Paid(c.Result.Id!, 0, reason: "already") };
        foreach (var id in new[] { "a1", "a2", "a3" }) rig.Queue.Offer(id, null);
        await rig.Drain();
        Assert.Equal(new[] { true, false, false }, rig.Settled.Select(s => s.FirstQuiet));
        Assert.All(rig.Settled, s => Assert.False(s.Post));

        rig.Queue.ResetWindow();
        rig.Queue.Offer("a4", null);
        await rig.Drain();
        Assert.True(rig.Settled.Last().FirstQuiet);
        Assert.True(rig.Queue.Offer("a1", null));          // a new window may send a settled id again
    }

    [Fact]
    public async Task A_window_offers_at_most_its_cap()
    {
        var rig = new Rig();
        var taken = 0;
        for (var i = 0; i < TcStickerQueue.MaxOffersPerWindow + 30; i++)
        {
            if (rig.Queue.Offer("s" + i, null)) taken++;
            if (i % 50 == 49) await rig.Drain();   // stay under the waiting cap
        }
        await rig.Drain();
        Assert.Equal(TcStickerQueue.MaxOffersPerWindow, taken);
        Assert.Equal(TcStickerQueue.MaxOffersPerWindow, rig.Calls);
    }

    [Fact]
    public void The_waiting_line_has_a_cap()
    {
        var gate = new TaskCompletionSource<SparklesClaimOutcome?>();
        var q = new TcStickerQueue(_ => gate.Task, _ => Task.CompletedTask, _ => { });
        var taken = Enumerable.Range(0, TcStickerQueue.MaxWaiting + 10).Count(i => q.Offer("w" + i, null));
        // One may already be in flight, so the line holds MaxWaiting plus at most one.
        Assert.InRange(taken, TcStickerQueue.MaxWaiting, TcStickerQueue.MaxWaiting + 1);
        gate.SetResult(null);
    }
}
