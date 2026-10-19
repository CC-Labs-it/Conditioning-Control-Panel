using System;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The daily share of Scrolller media (owner, 2026-10-19): free users get a capped day, Basic and
/// Prime far more; the meter shows a percentage only; the day resets at local midnight; the
/// server can retune the limits without a release.
/// </summary>
[Collection("ScrolllerAllowance")]
public class ScrolllerAllowanceTests : IDisposable
{
    private DateTime _now = new(2026, 10, 19, 14, 0, 0);
    private ScrolllerPlan _plan = ScrolllerPlan.Free;

    public ScrolllerAllowanceTests()
    {
        ScrolllerAllowance.ResetForTests();
        ScrolllerAllowance.LedgerPath = () => null;
        ScrolllerAllowance.Now = () => _now;
        ScrolllerAllowance.PlanProvider = () => _plan;
    }

    public void Dispose()
    {
        ScrolllerAllowance.ResetForTests();
        ScrolllerAllowance.Now = () => DateTime.Now;
    }

    [Fact]
    public void Defaults_are_300_mb_2_gb_6_gb()
    {
        Assert.Equal(300 * ScrolllerAllowance.MB, ScrolllerAllowance.DailyBytes(ScrolllerPlan.Free));
        Assert.Equal(2048 * ScrolllerAllowance.MB, ScrolllerAllowance.DailyBytes(ScrolllerPlan.Basic));
        Assert.Equal(6144 * ScrolllerAllowance.MB, ScrolllerAllowance.DailyBytes(ScrolllerPlan.Prime));
    }

    [Fact]
    public void A_free_install_gets_500_mb_on_its_first_day_then_300()
    {
        Assert.Equal(500 * ScrolllerAllowance.MB, ScrolllerAllowance.LimitBytes);
        ScrolllerAllowance.Charge(400 * ScrolllerAllowance.MB);
        Assert.False(ScrolllerAllowance.Spent);
        Assert.Equal(80, ScrolllerAllowance.Percent);
        _now = _now.AddDays(1);
        Assert.False(ScrolllerAllowance.IsFirstDay);
        Assert.Equal(300 * ScrolllerAllowance.MB, ScrolllerAllowance.LimitBytes);
        _now = _now.AddDays(5);
        Assert.Equal(300 * ScrolllerAllowance.MB, ScrolllerAllowance.LimitBytes);
    }

    [Fact]
    public void The_first_day_is_the_first_day_with_online_media_not_the_install_date()
    {
        _now = _now.AddDays(3);   // nothing fetched yet: still the first day
        Assert.True(ScrolllerAllowance.IsFirstDay);
        ScrolllerAllowance.Charge(ScrolllerAllowance.MB);
        _now = _now.AddDays(1);
        Assert.False(ScrolllerAllowance.IsFirstDay);
    }

    [Fact]
    public void Paid_plans_never_drop_to_the_first_day_figure()
    {
        _plan = ScrolllerPlan.Basic;
        Assert.Equal(2048 * ScrolllerAllowance.MB, ScrolllerAllowance.LimitBytes);
    }

    [Fact]
    public void A_first_day_figure_below_the_daily_one_is_ignored()
    {
        ScrolllerAllowance.ApplyLimits(300, null, null, 100);
        Assert.Equal(300 * ScrolllerAllowance.MB, ScrolllerAllowance.LimitBytes);
    }

    [Fact]
    public void Free_is_spent_at_its_limit_and_prime_is_not()
    {
        ScrolllerAllowance.Charge(ScrolllerAllowance.MB);
        _now = _now.AddDays(1);   // past the first day
        ScrolllerAllowance.Charge(300 * ScrolllerAllowance.MB);
        Assert.True(ScrolllerAllowance.Spent);
        Assert.Equal(100, ScrolllerAllowance.Percent);
        _plan = ScrolllerPlan.Prime;
        Assert.False(ScrolllerAllowance.Spent);
        Assert.Equal(4, ScrolllerAllowance.Percent);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(42, 100, 42)]
    [InlineData(996, 1000, 99)]   // nearly done still reads 99: 100 always means spent
    [InlineData(1000, 1000, 100)]
    [InlineData(5000, 1000, 100)]
    [InlineData(1, 0, 100)]       // a zero limit is spent at once
    public void Percent_floors_and_100_means_spent(long used, long limit, int expected)
        => Assert.Equal(expected, ScrolllerLedger.Percent(used, limit));

    [Fact]
    public void The_share_resets_at_local_midnight()
    {
        ScrolllerAllowance.Charge(ScrolllerAllowance.MB);
        _now = _now.AddDays(-1);  // that charge was on an earlier day: today is not the first day
        ScrolllerAllowance.ResetForTests();
        ScrolllerAllowance.Charge(ScrolllerAllowance.MB);
        _now = _now.AddDays(1);
        ScrolllerAllowance.Charge(249 * ScrolllerAllowance.MB);
        Assert.Equal(83, ScrolllerAllowance.Percent);
        _now = new DateTime(2026, 10, 19, 23, 59, 59);
        Assert.Equal(83, ScrolllerAllowance.Percent);
        _now = new DateTime(2026, 10, 20, 0, 0, 1);
        Assert.Equal(0, ScrolllerAllowance.Percent);
        Assert.Equal(new DateTime(2026, 10, 21), ScrolllerAllowance.NextResetLocal);
    }

    [Fact]
    public void Spent_edge_fires_once_a_day()
    {
        var fired = 0;
        EventHandler h = (_, _) => fired++;
        ScrolllerAllowance.SpentReached += h;
        try
        {
            ScrolllerAllowance.Charge(300 * ScrolllerAllowance.MB);
            Assert.Equal(0, fired);
            ScrolllerAllowance.Charge(200 * ScrolllerAllowance.MB);
            Assert.Equal(1, fired);
            ScrolllerAllowance.Charge(10 * ScrolllerAllowance.MB);
            Assert.Equal(1, fired);
            _now = _now.AddDays(1);
            ScrolllerAllowance.Charge(400 * ScrolllerAllowance.MB);
            Assert.Equal(2, fired);
        }
        finally { ScrolllerAllowance.SpentReached -= h; }
    }

    [Fact]
    public void Midnight_and_a_plan_change_raise_changed_without_a_charge()
    {
        var changed = 0;
        EventHandler h = (_, _) => changed++;
        ScrolllerAllowance.Changed += h;
        try
        {
            ScrolllerAllowance.Tick();   // first look
            changed = 0;
            ScrolllerAllowance.Tick();
            Assert.Equal(0, changed);
            _now = new DateTime(2026, 10, 20, 0, 0, 1);
            ScrolllerAllowance.Tick();
            Assert.Equal(1, changed);
            _plan = ScrolllerPlan.Prime;
            ScrolllerAllowance.Tick();
            Assert.Equal(2, changed);
            ScrolllerAllowance.ApplyLimits(100, null, null);
            Assert.Equal(3, changed);
        }
        finally { ScrolllerAllowance.Changed -= h; }
    }

    [Fact]
    public void Spent_edge_fires_for_a_lower_limit_or_a_plan_drop_once_a_day()
    {
        var fired = 0;
        EventHandler h = (_, _) => fired++;
        ScrolllerAllowance.SpentReached += h;
        try
        {
            _plan = ScrolllerPlan.Basic;
            ScrolllerAllowance.Charge(1000 * ScrolllerAllowance.MB);
            Assert.Equal(0, fired);
            _plan = ScrolllerPlan.Free;             // plan drop: 1000 MB is past the free day
            ScrolllerAllowance.Tick();
            Assert.Equal(1, fired);
            _plan = ScrolllerPlan.Basic;
            ScrolllerAllowance.Tick();
            _plan = ScrolllerPlan.Free;             // same day: never twice
            ScrolllerAllowance.Tick();
            Assert.Equal(1, fired);

            _now = _now.AddDays(1);
            _plan = ScrolllerPlan.Basic;
            ScrolllerAllowance.Charge(250 * ScrolllerAllowance.MB);
            ScrolllerAllowance.ApplyLimits(null, 200, null);   // a lower limit uses it up
            Assert.Equal(2, fired);
        }
        finally { ScrolllerAllowance.SpentReached -= h; }
    }

    [Fact]
    public void A_day_already_spent_at_startup_never_fires()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-allow-" + Guid.NewGuid().ToString("N") + ".json");
        System.IO.File.WriteAllText(path, "{\"day\":\"2026-10-19\",\"used\":" + 400 * ScrolllerAllowance.MB + ",\"first\":\"2026-10-18\"}");
        ScrolllerAllowance.LedgerPath = () => path;
        var fired = 0;
        EventHandler h = (_, _) => fired++;
        ScrolllerAllowance.SpentReached += h;
        try
        {
            ScrolllerAllowance.Tick();
            ScrolllerAllowance.Charge(ScrolllerAllowance.MB);
            Assert.True(ScrolllerAllowance.Spent);
            Assert.Equal(0, fired);
        }
        finally
        {
            ScrolllerAllowance.SpentReached -= h;
            try { System.IO.File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void A_clock_set_back_keeps_the_day_used_and_never_a_fresh_share()
    {
        ScrolllerAllowance.Charge(ScrolllerAllowance.MB);
        _now = _now.AddDays(1);
        ScrolllerAllowance.Charge(250 * ScrolllerAllowance.MB);
        _now = _now.AddDays(-2);
        Assert.Equal(250 * ScrolllerAllowance.MB, ScrolllerAllowance.UsedToday);
        ScrolllerAllowance.Charge(50 * ScrolllerAllowance.MB);
        _now = _now.AddDays(2);
        Assert.True(ScrolllerAllowance.Spent);
        _now = _now.AddDays(1);
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void A_charge_after_flush_still_lands_on_disk()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-allow-" + Guid.NewGuid().ToString("N") + ".json");
        ScrolllerAllowance.LedgerPath = () => path;
        try
        {
            ScrolllerAllowance.Charge(ScrolllerAllowance.MB);
            ScrolllerAllowance.Flush();
            ScrolllerAllowance.Charge(ScrolllerAllowance.MB);
            ScrolllerAllowance.Flush();
            Assert.Contains("\"used\":" + 2 * ScrolllerAllowance.MB, System.IO.File.ReadAllText(path));
        }
        finally { try { System.IO.File.Delete(path); } catch { } }
    }

    [Fact]
    public void Nothing_and_negatives_charge_nothing()
    {
        ScrolllerAllowance.Charge(0);
        ScrolllerAllowance.Charge(-500);
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void Server_limits_apply_and_bad_values_keep_the_default()
    {
        ScrolllerAllowance.ApplyLimits(100, null, -3);
        Assert.Equal(100 * ScrolllerAllowance.MB, ScrolllerAllowance.DailyBytes(ScrolllerPlan.Free));
        Assert.Equal(ScrolllerAllowance.DefaultBasicBytes, ScrolllerAllowance.DailyBytes(ScrolllerPlan.Basic));
        Assert.Equal(ScrolllerAllowance.DefaultPrimeBytes, ScrolllerAllowance.DailyBytes(ScrolllerPlan.Prime));
    }

    [Fact]
    public void Config_parses_daily_mb()
    {
        var a = ScrolllerPolicyService.Parse("{\"enabled\":true,\"daily_mb\":{\"free\":250,\"basic\":\"x\",\"prime\":8000,\"free_first_day\":600}}")!;
        Assert.Equal(600, a.FreeFirstDayMb);
        Assert.Equal(250, a.FreeMb);
        Assert.Null(a.BasicMb);
        Assert.Equal(8000, a.PrimeMb);
        var none = ScrolllerPolicyService.Parse("{\"enabled\":true}")!;
        Assert.Null(none.FreeMb);
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void Fresh_fetch_needs_switch_on_share_left_and_someone_there(bool enabled, bool spent, bool away, bool expected)
        => Assert.Equal(expected, ScrolllerPolicyService.FreshRule(enabled, spent, away));

    [Fact]
    public void Other_hosts_are_never_held_back()
    {
        ScrolllerAllowance.Charge(600 * ScrolllerAllowance.MB);
        Assert.True(ScrolllerPolicyService.MayFetchFresh("https://cclabs.app/a.jpg"));
        Assert.False(ScrolllerPolicyService.MayFetchFresh("https://images.scrolller.com/a.jpg"));
    }
}
