using System;
using System.IO;
using ConditioningControlPanel.Services.Chaster;
using Newtonsoft.Json;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A leash cut drops the holder's leash time that has not reached the lock yet (owner, 2026-10-22).
/// Only "leash" time goes (net of "leash_credit"), never the player's own slip-ups; a push already
/// on the wire stands; the raffle ledger never reads the dropped seconds as cancelled by a credit.
/// </summary>
public class LeashCutTabDropTests
{
    private static readonly DateTime Oct5 = new(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc);

    // What the service does on every booking: the tab books, then Booked hands it to the ledger.
    private static int BookAt(TabState s, string id, int seconds, DateTime utc, bool raffleDay = false)
    {
        var booking = CircesTab.Book(s, id, seconds, utc, utc, utc, safetyExit: false, raffleDay: raffleDay);
        ChasterLadder.NoteBooked(s, booking.AppliedSeconds, utc);
        return booking.AppliedSeconds;
    }

    private static TabPush Plan(TabState s, DateTime utc, TabLimits? limits = null) =>
        CircesTab.PlanPush(s, canRemove: false, limits ?? TabLimits.Default, utc);

    [Fact]
    public void Only_leash_time_leaves_the_tab()
    {
        var s = new TabState();
        BookAt(s, "typo", 60, Oct5);
        BookAt(s, CircesTab.LeashEventId, 900, Oct5.AddSeconds(1));
        Assert.Equal(960, s.BalanceSeconds);

        Assert.Equal(900, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2)));

        Assert.Equal(60, s.BalanceSeconds);
        Assert.Equal(0, s.LeashOwedSeconds);
        Assert.Equal(60, Plan(s, Oct5).Seconds);
    }

    [Fact]
    public void A_holder_pardon_nets_against_the_leash_time_and_never_turns_into_credit()
    {
        var s = new TabState { PushedNetSeconds = 3600 };
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        BookAt(s, CircesTab.LeashCreditEventId, -900, Oct5.AddSeconds(1));
        Assert.Equal(0, s.LeashOwedSeconds);

        Assert.Equal(0, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2)));
        Assert.Equal(0, s.BalanceSeconds);
    }

    [Fact]
    public void Leash_time_already_on_the_lock_stays()
    {
        var s = new TabState();
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        CircesTab.ApplyPush(s, Plan(s, Oct5), Oct5, Oct5);

        Assert.Equal(0, s.LeashOwedSeconds);
        Assert.Equal(0, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(5)));
        Assert.Equal(900, s.PushedNetSeconds);
        Assert.Equal(0, s.BalanceSeconds);
    }

    [Fact]
    public void A_partial_push_takes_leash_time_last()
    {
        var s = new TabState();
        BookAt(s, "typo", 300, Oct5);
        BookAt(s, CircesTab.LeashEventId, 900, Oct5.AddSeconds(1));
        CircesTab.ApplyPush(s, Plan(s, Oct5, TabLimits.FromMinutes(15, 720)), Oct5, Oct5);
        Assert.Equal(300, s.BalanceSeconds);
        Assert.Equal(300, s.LeashOwedSeconds);

        Assert.Equal(300, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(5)));
        Assert.Equal(0, s.BalanceSeconds);
    }

    [Fact]
    public void A_push_on_the_wire_stands_when_it_lands()
    {
        var s = new TabState();
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        var plan = Plan(s, Oct5);
        CircesTab.MarkPending(s, plan, Oct5);

        Assert.Equal(0, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(1)));
        Assert.True(s.LeashCutHeld);
        Assert.Equal(900, s.BalanceSeconds);

        CircesTab.ClearPending(s);
        CircesTab.ApplyPush(s, plan, Oct5, Oct5.AddSeconds(2));

        Assert.False(s.LeashCutHeld);
        Assert.Equal(0, s.LeashOwedSeconds);
        Assert.Equal(0, s.BalanceSeconds);
        Assert.Equal(900, s.PushedNetSeconds);
    }

    [Fact]
    public void A_push_on_the_wire_that_fails_is_dropped_then()
    {
        var s = new TabState();
        BookAt(s, "typo", 60, Oct5);
        BookAt(s, CircesTab.LeashEventId, 900, Oct5.AddSeconds(1));
        CircesTab.MarkPending(s, Plan(s, Oct5), Oct5);
        Assert.Equal(0, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2)));

        Assert.Equal(900, CircesTab.PushFailed(s, Oct5.AddSeconds(3)));

        Assert.False(s.LeashCutHeld);
        Assert.Equal(0, s.PendingSeconds);
        Assert.Equal(60, s.BalanceSeconds);
    }

    [Fact]
    public void A_push_that_nobody_answered_counts_as_landed_and_stands()
    {
        var s = new TabState();
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        CircesTab.MarkPending(s, Plan(s, Oct5), Oct5);
        CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(1));

        Assert.Equal(900, CircesTab.ResolvePending(s, Oct5.AddSeconds(2)));

        Assert.False(s.LeashCutHeld);
        Assert.Equal(0, s.LeashOwedSeconds);
        Assert.Equal(0, s.BalanceSeconds);
    }

    [Fact]
    public void Leash_time_past_the_push_on_the_wire_goes_at_once()
    {
        var s = new TabState();
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        var plan = Plan(s, Oct5);
        CircesTab.MarkPending(s, plan, Oct5);
        BookAt(s, CircesTab.LeashEventId, 600, Oct5.AddSeconds(1));

        Assert.Equal(600, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2)));
        Assert.Equal(900, s.BalanceSeconds);

        CircesTab.ClearPending(s);
        CircesTab.ApplyPush(s, plan, Oct5, Oct5.AddSeconds(3));
        Assert.Equal(0, s.BalanceSeconds);
    }

    [Fact]
    public void A_credit_the_leash_time_cancelled_comes_back()
    {
        var s = new TabState { PushedNetSeconds = 1000 };
        BookAt(s, "session", -300, Oct5);
        BookAt(s, CircesTab.LeashEventId, 900, Oct5.AddSeconds(1));
        Assert.Equal(600, s.BalanceSeconds);

        Assert.Equal(900, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2)));
        Assert.Equal(-300, s.BalanceSeconds);
    }

    [Fact]
    public void The_floor_holds_after_a_drop()
    {
        var s = new TabState();
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        BookAt(s, "session", -900, Oct5.AddSeconds(1));

        Assert.Equal(0, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2)));
        Assert.Equal(0, s.BalanceSeconds);
    }

    [Fact]
    public void A_credit_spent_on_leash_time_at_the_floor_never_lets_the_cut_take_a_later_slip_up()
    {
        // Without the leash the credit would have hit the floor and done nothing, so the slip-up
        // after it is the player's own and stays.
        var s = new TabState();
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        BookAt(s, "session", -900, Oct5.AddSeconds(1));
        BookAt(s, "typo", 600, Oct5.AddSeconds(2));
        Assert.Equal(0, s.LeashOwedSeconds);

        Assert.Equal(0, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(3)));
        Assert.Equal(600, s.BalanceSeconds);
    }

    [Fact]
    public void A_credit_with_room_below_the_floor_still_comes_back_after_a_cut()
    {
        // With pushes on the lock the credit would have landed anyway: the cut gives it back.
        var s = new TabState { PushedNetSeconds = 1000 };
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        BookAt(s, "session", -900, Oct5.AddSeconds(1));
        BookAt(s, "typo", 600, Oct5.AddSeconds(2));
        Assert.Equal(900, s.LeashOwedSeconds);

        Assert.Equal(900, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(3)));
        Assert.Equal(-300, s.BalanceSeconds);
    }

    [Fact]
    public void A_raffle_day_set_aside_credit_comes_back_with_the_drop()
    {
        var s = new TabState { PushedNetSeconds = 1000 };
        BookAt(s, "session", -300, Oct5);
        BookAt(s, CircesTab.LeashEventId, 900, Oct5.AddSeconds(1), raffleDay: true);
        Assert.Equal(300, s.ParkedCreditSeconds);
        Assert.Equal(900, s.BalanceSeconds);

        Assert.Equal(900, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2)));

        Assert.Equal(0, s.ParkedCreditSeconds);
        Assert.Equal(-300, s.BalanceSeconds);
    }

    [Fact]
    public void The_raffle_never_counts_dropped_seconds_as_cancelled_by_a_credit()
    {
        var s = new TabState { PushedNetSeconds = 1000 };
        BookAt(s, "session", -300, Oct5);
        BookAt(s, CircesTab.LeashEventId, 900, Oct5.AddSeconds(1));
        CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2));

        Assert.Empty(ChasterLadder.Claims(s, Oct5.AddMinutes(1)));
        Assert.Equal(0, ChasterLadder.ThisMonth(s, Oct5));
    }

    [Fact]
    public void Leash_time_owed_overnight_and_dropped_the_next_day_claims_nothing()
    {
        var s = new TabState();
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        var next = Oct5.AddDays(1);
        BookAt(s, "typo", 60, next);
        CircesTab.DropUnpushedLeash(s, next.AddSeconds(1));
        CircesTab.ApplyPush(s, Plan(s, next), next, next.AddSeconds(2));

        var claims = ChasterLadder.Claims(s, next.AddMinutes(1));
        Assert.False(claims.ContainsKey("2026-10-05"));
        Assert.Equal(new LadderClaim(60, 60), claims["2026-10-06"]);
    }

    [Fact]
    public void The_jackpot_takes_the_leash_part_too()
    {
        var s = new TabState();
        BookAt(s, CircesTab.LeashEventId, 900, Oct5);
        CircesTab.Wipe(s, Oct5.AddSeconds(1), Oct5);

        Assert.Equal(0, s.LeashOwedSeconds);
        Assert.Equal(0, CircesTab.DropUnpushedLeash(s, Oct5.AddSeconds(2)));
    }

    [Fact]
    public void A_hand_edited_leash_count_is_clamped_at_load()
    {
        var s = new TabState { LeashOwedSeconds = -50 };
        Assert.True(CircesTab.Sanitise(s, TabLimits.Default));
        Assert.Equal(0, s.LeashOwedSeconds);

        var back = JsonConvert.DeserializeObject<TabState>(JsonConvert.SerializeObject(new TabState { LeashOwedSeconds = 120, LeashCutHeld = true }))!;
        Assert.Equal(120, back.LeashOwedSeconds);
        Assert.True(back.LeashCutHeld);
    }

    [Fact]
    public void A_planned_push_is_held_to_what_is_still_due()
    {
        var planned = new TabPush(TabPushKind.Add, 900);
        Assert.Equal(TabPushKind.None, CircesTab.StillDue(planned, new TabPush(TabPushKind.None, 0)).Kind);
        Assert.Equal(300, CircesTab.StillDue(planned, new TabPush(TabPushKind.Add, 300)).Seconds);
        Assert.Equal(900, CircesTab.StillDue(planned, new TabPush(TabPushKind.Add, 1200)).Seconds);
    }

    [Fact]
    public void The_settle_plans_again_before_the_pending_mark_and_a_failure_drops_held_leash_time()
    {
        var src = File.ReadAllText(Path.Combine(SourceRoot(), "Services", "Chaster", "ChasterService.cs"));
        var settle = src.Substring(src.IndexOf("public async Task<SettleOutcome> SettleAsync(", StringComparison.Ordinal));
        var still = settle.IndexOf("CircesTab.StillDue(plan, CircesTab.PlanPush(", StringComparison.Ordinal);
        var mark = settle.IndexOf("CircesTab.MarkPending(_tab, plan", StringComparison.Ordinal);
        var send = settle.IndexOf("_client.AddTimeAsync(", StringComparison.Ordinal);
        Assert.True(still > 0 && mark > still && send > mark, "re-plan, then mark, then send");
        Assert.Contains("if (!added.Ok) CircesTab.PushFailed(_tab, _utcNow());", settle);
        Assert.Contains("CircesTab.PushFailed(_tab, _utcNow()); SaveTab();", settle);

        var cut = File.ReadAllText(Path.Combine(SourceRoot(), "Services", "Leash", "LeashCutSafety.cs"));
        Assert.Contains("App.Chaster?.DropUnpushedLeash()", cut);
        Assert.DoesNotContain("DropUnpushedLeashBookings() => false", cut);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
