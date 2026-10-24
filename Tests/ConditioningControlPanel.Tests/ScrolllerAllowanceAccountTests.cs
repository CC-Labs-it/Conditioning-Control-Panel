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
[Collection(ScrolllerPolicyCollection.Name)] // ScrolllerPolicyService.Accept reads the allowance: never in parallel
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

    /// <summary>A fresh process: in-memory state gone, the files on disk kept. Everything is set
    /// BEFORE the reset: tests outside this collection read the share (the web gate asks
    /// MayFetchFresh), and a read in between would load the book from the wrong place.</summary>
    private void Rig(bool onDisk = false)
    {
        if (onDisk)
        {
            ScrolllerAllowance.LedgerPath = () => BookPath;
            ScrolllerAllowance.LegacyLedgerPath = () => LegacyPath;
        }
        else
        {
            ScrolllerAllowance.LedgerPath = () => null;
            ScrolllerAllowance.LegacyLedgerPath = () => null;
        }
        ScrolllerAllowance.AccountProvider = () => _account;
        ScrolllerAllowance.Now = () => _now;
        ScrolllerAllowance.PlanProvider = () => ScrolllerPlan.Free;
        ScrolllerAllowance.ReadRetry = TimeSpan.Zero;
        ScrolllerAllowance.RunInBackground = a => a();   // no stray save from one test into the next
        ScrolllerAllowance.ResetForTests();
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
        ScrolllerAllowance.ReadRetry = TimeSpan.FromSeconds(5);
        ScrolllerAllowance.RunInBackground = a => System.Threading.Tasks.Task.Run(a);
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
        Rig(onDisk: true);
        ScrolllerAllowance.Charge(100 * MB);
        SignIn("account-b");
        ScrolllerAllowance.Charge(30 * MB);
        ScrolllerAllowance.Flush();

        var text = File.ReadAllText(BookPath);
        Assert.DoesNotContain("account-a", text);
        Assert.Contains(ScrolllerAllowance.KeyFor("account-a"), text);
        Assert.Matches("^[0-9a-f]{16}$", ScrolllerAllowance.KeyFor("account-a"));

        Rig(onDisk: true);                               // the next launch, signed in as b
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
        File.WriteAllText(LegacyPath, "{\"day\":\"2026-10-19\",\"used\":" + 120 * MB + ",\"first\":\"2026-10-12\"}");
        Rig(onDisk: true);

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
        _account = "account-c";
        Rig(onDisk: true);
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
        SignIn("account-a");
        Assert.Equal(120 * MB, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void A_sign_in_later_in_startup_still_gets_the_old_ledger()
    {
        // Patreon sign-in lands after the first look: a read while signed out adopts nothing.
        File.WriteAllText(LegacyPath, "{\"day\":\"2026-10-19\",\"used\":" + 80 * MB + "}");
        _account = null;
        Rig(onDisk: true);
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
        SignIn("account-a");
        Assert.Equal(80 * MB, ScrolllerAllowance.UsedToday);
        SignIn(null);
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void Signed_out_claims_the_old_ledger_only_with_a_charge()
    {
        File.WriteAllText(LegacyPath, "{\"day\":\"2026-10-19\",\"used\":" + 80 * MB + "}");
        _account = null;
        Rig(onDisk: true);
        ScrolllerAllowance.Charge(MB);
        Assert.Equal(81 * MB, ScrolllerAllowance.UsedToday);
        SignIn("account-a");
        Assert.Equal(0, ScrolllerAllowance.UsedToday);
    }

    [Fact]
    public void A_locked_book_is_retried_and_never_overwritten()
    {
        File.WriteAllText(BookPath, "{\"v\":1,\"legacy_adopted\":true,\"accounts\":{\""
            + ScrolllerAllowance.KeyFor("account-a") + "\":{\"day\":\"2026-10-19\",\"used\":" + 100 * MB + ",\"first\":\"2026-10-01\"}}}");
        using (new FileStream(BookPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Rig(onDisk: true);                           // OneDrive or an antivirus holds it at launch
            Assert.Equal(0, ScrolllerAllowance.UsedToday);
            ScrolllerAllowance.Charge(10 * MB);
        }
        Assert.Equal(110 * MB, ScrolllerAllowance.UsedToday);   // read on the retry, the interim charge merged
        Assert.False(ScrolllerAllowance.IsFirstDay);            // the account's real first day came back
        ScrolllerAllowance.Flush();
        Assert.Contains("\"used\":" + 110 * MB, File.ReadAllText(BookPath));
    }

    [Fact]
    public void No_fresh_fetch_while_the_book_is_locked()
    {
        // The stand-in knows neither today's usage nor the first day: it must not hand out a share.
        File.WriteAllText(BookPath, "{\"v\":1,\"legacy_adopted\":true,\"accounts\":{\""
            + ScrolllerAllowance.KeyFor("account-a") + "\":{\"day\":\"2026-10-19\",\"used\":" + 100 * MB + ",\"first\":\"2026-10-01\"}}}");
        const string fresh = "https://images.scrolller.com/a.jpg";
        using (new FileStream(BookPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Rig(onDisk: true);
            Assert.False(ScrolllerAllowance.BookSettled);
            Assert.False(ScrolllerPolicyService.MayFetchFresh(fresh));
            Assert.True(ScrolllerPolicyService.MayFetchFresh("https://cclabs.app/a.jpg"));
            Assert.False(ScrolllerAllowance.Spent);   // the meter does not claim "used up" meanwhile
        }
        Assert.True(ScrolllerAllowance.BookSettled);
        Assert.True(ScrolllerPolicyService.MayFetchFresh(fresh));   // read: 100 of 300 MB, share left
    }

    [Fact]
    public void A_book_that_does_not_parse_is_kept_aside_before_any_save()
    {
        File.WriteAllText(BookPath, "{not json");
        Rig(onDisk: true);
        ScrolllerAllowance.Charge(MB);
        ScrolllerAllowance.Flush();
        Assert.Equal("{not json", File.ReadAllText(BookPath + ".bad"));
        Assert.Contains("\"used\":" + MB, File.ReadAllText(BookPath));
    }

    [Fact]
    public void Merging_keeps_the_later_day_adds_a_same_day_and_the_earliest_first_day()
    {
        var same = ScrolllerLedger.Merge(new ScrolllerLedger("2026-10-19", 5, "2026-10-10"), new ScrolllerLedger("2026-10-19", 7, "2026-10-02"));
        Assert.Equal((12L, "2026-10-02"), (same.Used, same.FirstDay));
        var later = ScrolllerLedger.Merge(new ScrolllerLedger("2026-10-18", 50, null), new ScrolllerLedger("2026-10-19", 7, "2026-10-19"));
        Assert.Equal(("2026-10-19", 7L, "2026-10-19"), (later.Day, later.Used, later.FirstDay));
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
