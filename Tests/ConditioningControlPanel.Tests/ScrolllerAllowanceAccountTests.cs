using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The daily share is per account (owner, 2026-10-19: a Basic test account signed in on the same
/// PC started at 4 %, the other account's usage). Each account keeps its own day, its own first
/// day and its own spent notice; signed out has a bucket of its own; the machine-wide ledger of
/// earlier builds goes to whoever is signed in at the first load, once.
/// </summary>
[Collection("ScrolllerAllowance")]
public class ScrolllerAllowanceAccountTests : IDisposable
{
    private const long MB = ScrolllerAllowance.MB;
    private DateTime _now = new(2026, 10, 19, 14, 0, 0);
    private string? _account = "account-a";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-allow-acct-" + Guid.NewGuid().ToString("N"));
    private string BookPath => Path.Combine(_dir, "scrolller-accounts.json");
    private string LegacyPath => Path.Combine(_dir, "scrolller-today.json");

    public ScrolllerAllowanceAccountTests()
    {
        Directory.CreateDirectory(_dir);
        Rig();
    }

    /// <summary>A fresh process: in-memory state gone, the files on disk kept.</summary>
    private void Rig()
    {
        ScrolllerAllowance.ResetForTests();
        ScrolllerAllowance.LedgerPath = () => null;
        ScrolllerAllowance.LegacyLedgerPath = () => null;
        ScrolllerAllowance.AccountProvider = () => _account;
        ScrolllerAllowance.Now = () => _now;
        ScrolllerAllowance.PlanProvider = () => ScrolllerPlan.Free;
    }

    private void OnDisk()
    {
        ScrolllerAllowance.LedgerPath = () => BookPath;
        ScrolllerAllowance.LegacyLedgerPath = () => LegacyPath;
    }

    private void SignIn(string? account)
    {
        _account = account;
        ScrolllerAllowance.AccountChanged();
    }

    public void Dispose()
    {
        ScrolllerAllowance.ResetForTests();
        ScrolllerAllowance.Now = () => DateTime.Now;
        ScrolllerAllowance.AccountProvider = () => null;
        ScrolllerAllowance.LedgerPath = () => null;
        ScrolllerAllowance.LegacyLedgerPath = () => null;
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Two_accounts_on_one_pc_keep_separate_usage()
    {
        ScrolllerAllowance.Charge(100 * MB);
        Assert.Equal(20, ScrolllerAllowance.Percent);   // 100 of the 500 MB first day

        SignIn("account-b");
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
        Assert.Equal(0, ScrolllerAllowance.Percent);
        ScrolllerAllowance.Charge(50 * MB);

        SignIn("account-a");
        Assert.Equal(100 * MB, ScrolllerAllowance.UsedToday);
        SignIn("account-b");
        Assert.Equal(50 * MB, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void Switching_accounts_reloads_from_disk_and_the_id_never_lands_in_the_file()
    {
        OnDisk();
        ScrolllerAllowance.Charge(100 * MB);
        SignIn("account-b");
        ScrolllerAllowance.Charge(30 * MB);
        ScrolllerAllowance.Flush();

        var text = File.ReadAllText(BookPath);
        Assert.DoesNotContain("account-a", text);
        Assert.Contains(ScrolllerAllowance.KeyFor("account-a"), text);
        Assert.Matches("^[0-9a-f]{16}$", ScrolllerAllowance.KeyFor("account-a"));

        Rig(); OnDisk();                                 // the next launch, signed in as b
        Assert.Equal(30 * MB, ScrolllerAllowance.UsedToday);
        SignIn("account-a");
        Assert.Equal(100 * MB, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void Account_change_raises_changed_for_the_meter()
    {
        var changed = 0;
        EventHandler h = (_, _) => Interlocked.Increment(ref changed);
        ScrolllerAllowance.Changed += h;
        try
        {
            ScrolllerAllowance.Charge(10 * MB);
            var before = Volatile.Read(ref changed);
            SignIn("account-b");
            Assert.Equal(before + 1, Volatile.Read(ref changed));
        }
        finally { ScrolllerAllowance.Changed -= h; }
    }

    [Fact]
    public void Signed_out_uses_the_anonymous_bucket()
    {
        Assert.Equal(ScrolllerAllowance.AnonymousKey, ScrolllerAllowance.KeyFor(null));
        Assert.Equal(ScrolllerAllowance.AnonymousKey, ScrolllerAllowance.KeyFor(""));

        ScrolllerAllowance.Charge(200 * MB);
        SignIn(null);
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
        ScrolllerAllowance.Charge(10 * MB);
        SignIn("account-a");
        Assert.Equal(200 * MB, ScrolllerAllowance.UsedToday);
        SignIn("");
        Assert.Equal(10 * MB, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void The_old_ledger_goes_to_the_first_account_only_and_only_once()
    {
        OnDisk();
        File.WriteAllText(LegacyPath, "{\"day\":\"2026-10-19\",\"used\":" + 120 * MB + ",\"first\":\"2026-10-12\"}");

        Assert.Equal(120 * MB, ScrolllerAllowance.UsedToday);   // a, signed in at the first load
        Assert.False(ScrolllerAllowance.IsFirstDay);            // its first day came with it
        SignIn("account-b");
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
        Assert.True(ScrolllerAllowance.IsFirstDay);
        ScrolllerAllowance.Flush();
        Assert.False(File.Exists(LegacyPath));
        Assert.Contains("\"legacy_adopted\":true", File.ReadAllText(BookPath));

        // The old file turns up again (a downgrade and back): it is never adopted a second time.
        File.WriteAllText(LegacyPath, "{\"day\":\"2026-10-19\",\"used\":" + 300 * MB + "}");
        Rig(); OnDisk();
        _account = "account-c";
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
        SignIn("account-a");
        Assert.Equal(120 * MB, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void Signed_out_at_the_first_load_keeps_the_old_ledger_off_every_account()
    {
        OnDisk();
        File.WriteAllText(LegacyPath, "{\"day\":\"2026-10-19\",\"used\":" + 80 * MB + "}");
        _account = null;
        Assert.Equal(80 * MB, ScrolllerAllowance.UsedToday);
        SignIn("account-a");
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void The_free_first_day_is_each_accounts_own()
    {
        ScrolllerAllowance.Charge(MB);
        _now = _now.AddDays(1);
        Assert.False(ScrolllerAllowance.IsFirstDay);
        Assert.Equal(300 * MB, ScrolllerAllowance.LimitBytes);

        SignIn("account-b");                             // b's first day of online media here
        Assert.True(ScrolllerAllowance.IsFirstDay);
        Assert.Equal(500 * MB, ScrolllerAllowance.LimitBytes);
        ScrolllerAllowance.Charge(MB);
        _now = _now.AddDays(1);
        Assert.False(ScrolllerAllowance.IsFirstDay);
    }

    [Fact]
    public void The_spent_notice_is_per_account_and_a_switch_is_silent()
    {
        var fired = 0;
        EventHandler h = (_, _) => Interlocked.Increment(ref fired);
        ScrolllerAllowance.SpentReached += h;
        try
        {
            ScrolllerAllowance.Charge(500 * MB);         // a spends its first day
            Assert.Equal(1, Volatile.Read(ref fired));

            SignIn("account-b");
            ScrolllerAllowance.Charge(499 * MB);
            Assert.Equal(1, Volatile.Read(ref fired));
            ScrolllerAllowance.Charge(MB);               // b spends too: b's own notice
            Assert.Equal(2, Volatile.Read(ref fired));

            SignIn("account-a");                         // back to an already spent day: silent
            ScrolllerAllowance.Tick();
            ScrolllerAllowance.Charge(MB);
            Assert.True(ScrolllerAllowance.Spent);
            Assert.Equal(2, Volatile.Read(ref fired));
        }
        finally { ScrolllerAllowance.SpentReached -= h; }
    }
}
