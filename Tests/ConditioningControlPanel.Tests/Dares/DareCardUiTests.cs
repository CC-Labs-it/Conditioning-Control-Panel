using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Services.Dares;
using Xunit;
using static ConditioningControlPanel.Tests.Dares.DareUiTestKit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>The bet cards' rules: the debt card's words and ops, the Lockdown consent steps, the
/// landing plan (offers once, debts on the ladder, never mid-session), the strip, and that every
/// dares_* string is in all ten languages.</summary>
public class DareCardUiTests
{
    private static DareBet Owed(DareStakeKind k, string id = "d1", DareDebtState s = DareDebtState.Owed, DateTimeOffset? settled = null)
        => Bet(id, me: DareSide.To, state: DareState.Settled, winner: DareSide.From,
            to: new DareStake(k, k is DareStakeKind.Dare ? 0 : 20, k is DareStakeKind.Dare ? 1 : 0),
            debt: new DareDebt(s, false, false), settledAt: settled ?? DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(DareStakeKind.Sp, "dares_pay_sp", DareOp.Pay)]
    [InlineData(DareStakeKind.Lock, "dares_pay_lock", DareOp.Pay)]
    [InlineData(DareStakeKind.Dare, "dares_pay_dare", DareOp.DidIt)]
    [InlineData(DareStakeKind.Lockdown, "dares_pay_lockdown", DareOp.StartLockdown)]
    public void Pay_is_worded_by_stake_and_runs_its_own_op(DareStakeKind k, string key, DareOp op)
    {
        Assert.Equal(key, DareCardRules.PayKey(k));
        Assert.Equal(op, DareCardRules.PayOp(k));
        Assert.Equal("Pay", En("dares_pay_sp"));
        Assert.Equal("Add the minutes", En("dares_pay_lock"));
        Assert.Equal("I did it", En("dares_pay_dare"));
        Assert.Equal("Start Lockdown", En("dares_pay_lockdown"));
    }

    [Fact]
    public void Only_a_Lockdown_asks_first_and_a_startup_PC_asks_twice()
    {
        Assert.Empty(DareCardRules.ConsentSteps(DareStakeKind.Sp, runsAtStartup: true));
        Assert.Equal(new[] { "lockdown" }, DareCardRules.ConsentSteps(DareStakeKind.Lockdown, false));
        Assert.Equal(new[] { "lockdown", "startup" }, DareCardRules.ConsentSteps(DareStakeKind.Lockdown, true));
    }

    [Fact]
    public void The_launch_card_is_the_oldest_owed_debt_never_one_being_served()
    {
        var older = Owed(DareStakeKind.Sp, "old", settled: DateTimeOffset.UtcNow.AddDays(-2));
        var newer = Owed(DareStakeKind.Lock, "new");
        var serving = Owed(DareStakeKind.Lockdown, "srv", DareDebtState.Serving, DateTimeOffset.UtcNow.AddDays(-3));
        Assert.Equal("old", DareCardRules.DebtToShow(Snap(newer, serving, older))!.Id);
        Assert.Null(DareCardRules.DebtToShow(Snap(serving)));
        Assert.Null(DareCardRules.DebtToShow(DaresSnapshot.Dark));
    }

    [Fact]
    public void Landing_posts_each_offer_once_drops_gone_ones_and_holds_debts_while_busy()
    {
        var offer = Bet("o1", me: DareSide.To, state: DareState.Sent);
        var debt = Owed(DareStakeKind.Sp);
        var posted = new HashSet<string> { "gone1" };
        var (post, gone, d) = DareCardRules.LandingPlan(Snap(offer, debt), posted, new HashSet<string>(), holding: false);
        Assert.Equal(new[] { "o1" }, post.Select(b => b.Id));
        Assert.Equal(new[] { "gone1" }, gone);
        Assert.Equal("d1", d!.Id);

        posted = new HashSet<string> { "o1" };
        (post, _, d) = DareCardRules.LandingPlan(Snap(offer, debt), posted, new HashSet<string> { "d1" }, holding: false);
        Assert.Empty(post);
        Assert.Null(d);

        (_, _, d) = DareCardRules.LandingPlan(Snap(debt), new HashSet<string>(), new HashSet<string>(), holding: true);
        Assert.Null(d);
    }

    [Fact]
    public void The_strip_puts_answers_and_debts_first_and_keeps_a_few_settled()
    {
        var settled = Enumerable.Range(0, 6).Select(i => Bet("s" + i, state: DareState.Passed, at: DateTimeOffset.UtcNow.AddHours(-i))).ToArray();
        var running = Bet("run");
        var offer = Bet("off", me: DareSide.To, state: DareState.Sent);
        var owe = Owed(DareStakeKind.Sp, "owe");
        var order = DareCardRules.StripOrder(Snap(settled.Concat(new[] { running, owe, offer }).ToArray())).Select(b => b.Id).ToList();
        Assert.Equal(new[] { "off", "owe", "run" }, order.Take(3));
        Assert.Equal(3 + DareCardRules.SettledShown, order.Count);
        Assert.Equal("s0", order[3]);
    }

    [Fact]
    public void Row_states_read_from_my_side()
    {
        Assert.Equal(DareRowState.Offer, DareCardRules.RowState(Bet(me: DareSide.To, state: DareState.Sent)));
        Assert.Equal(DareRowState.Waiting, DareCardRules.RowState(Bet(me: DareSide.From, state: DareState.Sent)));
        Assert.Equal(DareRowState.IOwe, DareCardRules.RowState(Owed(DareStakeKind.Sp)));
        var theyOwe = Owed(DareStakeKind.Dare, s: DareDebtState.Claimed) with { Me = DareSide.From };
        Assert.Equal(DareRowState.TheyOwe, DareCardRules.RowState(theyOwe));
        Assert.True(DareCardRules.ShowsTick(theyOwe));
        Assert.Equal(DareRowState.Draw, DareCardRules.RowState(Bet(state: DareState.Settled)));
        foreach (var s in Enum.GetValues<DareRowState>()) Assert.NotEqual(DareCardRules.RowStateKey(s), En(DareCardRules.RowStateKey(s)));
    }

    [Fact]
    public void Buttons_on_a_row_follow_the_bet()
    {
        Assert.True(DareCardRules.ShowsCancel(Bet(state: DareState.Writing)));
        Assert.False(DareCardRules.ShowsCancel(Bet(me: DareSide.To, state: DareState.Sent)));
        Assert.True(DareCardRules.ShowsSettleNow(Bet(), preview: true));
        Assert.False(DareCardRules.ShowsSettleNow(Bet(), preview: false));
        var cantDare = Bet(me: DareSide.To, what: new DareWhat(DareKind.Dare, "1"));
        Assert.True(DareCardRules.ShowsDidIt(cantDare));
        Assert.False(DareCardRules.ShowsDidIt(cantDare with { ToCount = 1 }));
    }

    [Fact]
    public void Bars_and_time_left()
    {
        Assert.Equal(0.5, DareCardRules.CantFraction(Bet(me: DareSide.From, toCount: 250)));
        Assert.Equal(1, DareCardRules.CantFraction(Bet(what: new DareWhat(DareKind.Session, "easy"), toCount: 1)));
        Assert.Equal((0.5, 1.0), DareCardRules.DuelBars(5, 10));
        Assert.Equal((0.0, 0.0), DareCardRules.DuelBars(0, 0));
        Assert.Equal("dares_left_hours", DareCardRules.Left(TimeSpan.FromMinutes(130)).Key);
        Assert.Equal(new object[] { 2, 10 }, DareCardRules.Left(TimeSpan.FromMinutes(130)).Args);
        Assert.Equal("dares_left_days", DareCardRules.Left(TimeSpan.FromHours(30)).Key);
        Assert.Equal("dares_left_minutes", DareCardRules.Left(TimeSpan.FromSeconds(20)).Key);
        Assert.Equal("dares_left_ending", DareCardRules.Left(TimeSpan.FromSeconds(-1)).Key);
    }

    [Fact]
    public void Every_create_and_act_answer_has_words_of_its_own()
    {
        foreach (var s in Enum.GetValues<DareCreateStatus>()) Assert.NotEqual(DareCardRules.CreateKey(s), En(DareCardRules.CreateKey(s)));
        foreach (var r in Enum.GetValues<DareActResult>()) Assert.NotEqual(DareCardRules.ActKey(r), En(DareCardRules.ActKey(r)));
        Assert.Equal("dares_create_lockdown_needs_sub", DareCardRules.CreateKey(DareCreateStatus.LockdownNeedsSub));
        Assert.Equal("dares_act_lockdown_needs_sub", DareCardRules.ActKey(DareActResult.LockdownNeedsSub));
        Assert.Equal("/bet write code:AB12CD", DareCardRules.WriteLine("AB12CD"));
    }

    [Fact]
    public void Every_dares_string_is_in_all_ten_languages_and_reads_clean()
    {
        var en = Strings("en").Where(kv => kv.Key.StartsWith("dares_", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
        Assert.True(en.Count > 150);
        var tokens = new Regex(@"\{[a-z0-9]+\}");
        foreach (var lang in new[] { "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var other = Strings(lang);
            foreach (var (k, v) in en)
            {
                Assert.True(other.ContainsKey(k), $"{lang}.json is missing {k}");
                Assert.Equal(tokens.Matches(v).Select(m => m.Value).OrderBy(x => x), tokens.Matches(other[k]).Select(m => m.Value).OrderBy(x => x));
            }
            var bytes = File.ReadAllBytes(Path.Combine(LanguagesDir, lang + ".json"));
            Assert.False(bytes.Length > 2 && bytes[0] == 0xEF && bytes[1] == 0xBB, $"{lang}.json carries a BOM");
        }
        foreach (var v in en.Values)
        {
            Assert.DoesNotContain("—", v);
            Assert.DoesNotContain("–", v);
            Assert.DoesNotContain("!", v);
        }
    }

    [Fact]
    public void Every_key_the_Dares_code_names_exists()
    {
        var en = Strings("en");
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var files = new[] { "Controls/Dares", "Dialogs/Dares" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(app, d), "*.cs"))
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Dares"), "*.cs"))
            .Append(Path.Combine(app, "Controls/Friends/FriendsDrawer.Dares.cs"));
        var literal = new Regex("\"(dares_[a-z0-9_]*[a-z0-9])\"");
        foreach (var f in files)
            foreach (Match m in literal.Matches(File.ReadAllText(f)))
                Assert.True(en.ContainsKey(m.Groups[1].Value), $"{Path.GetFileName(f)} names {m.Groups[1].Value}, not in en.json");

        // The keys built at run time.
        var built = new List<string>();
        foreach (var k in Enum.GetValues<DareKind>()) { built.Add(DareSlipRules.TileKey(k)); built.Add(DareSlipRules.TipOf(k)); }
        foreach (var k in Enum.GetValues<DareStakeKind>()) { built.Add(DareSlipRules.TileKey(k)); built.Add(DareSlipRules.TipOf(k)); }
        foreach (var t in DareGrammar.SessionTargets) { built.Add(DareSlipRules.TargetLabelKey(t)); built.Add("dares_chip_session_" + t); }
        foreach (var s in Enum.GetValues<DareStep>()) { built.Add(DareSlipRules.BlankKey(s)); built.Add(DareSlipRules.AskKey(s, DareType.Cant)); built.Add(DareSlipRules.AskKey(s, DareType.Duel)); }
        foreach (var w in Enum.GetValues<DareDeadline>()) built.Add(DareSlipRules.WhenKey(w));
        foreach (var k in built) Assert.True(en.ContainsKey(k), $"built key {k} is not in en.json");
    }
}
