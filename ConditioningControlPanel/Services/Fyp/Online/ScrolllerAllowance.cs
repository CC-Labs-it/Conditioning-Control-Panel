using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>Which daily share of Scrolller media an account gets.</summary>
internal enum ScrolllerPlan { Free, Basic, Prime }

/// <summary>
/// The day's ledger, pure so the rules are pinned by tests: bytes used on one LOCAL calendar day.
/// A charge on a later day starts a fresh ledger (the share resets at local midnight).
/// </summary>
internal sealed class ScrolllerLedger
{
    public string Day { get; private set; }
    public long Used { get; private set; }

    /// <summary>The day this install first fetched Scrolller media; null until it does.</summary>
    public string? FirstDay { get; private set; }

    public ScrolllerLedger(string day, long used, string? firstDay = null)
    {
        Day = day;
        Used = Math.Max(0, used);
        FirstDay = firstDay;
    }

    /// <summary>True on the install's first day of online media (or before it has had one).</summary>
    public bool IsFirstDay => FirstDay == null || FirstDay == Day;

    public static string DayOf(DateTime local) => local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Moves the ledger to <paramref name="local"/>'s day, zeroing it when the day changed.
    /// True = it rolled over.</summary>
    public bool Roll(DateTime local)
    {
        var day = DayOf(local);
        if (day == Day) return false;
        Day = day;
        Used = 0;
        return true;
    }

    public void Charge(long bytes, DateTime local)
    {
        Roll(local);
        if (bytes <= 0) return;
        FirstDay ??= Day;
        Used = Used > long.MaxValue - bytes ? long.MaxValue : Used + bytes;
    }

    /// <summary>Share used, 0..1. A limit of zero or less reads as fully used.</summary>
    public static double Fraction(long used, long limit) => limit <= 0 ? 1.0 : Math.Clamp((double)used / limit, 0.0, 1.0);

    /// <summary>What the meter shows: floor of the share, and 100 ONLY when it is really spent
    /// (99.6% still says 99, so "100%" always means done).</summary>
    public static int Percent(long used, long limit)
    {
        if (used >= limit) return 100;
        return Math.Min(99, (int)Math.Floor(Fraction(used, limit) * 100));
    }
}

/// <summary>
/// The daily share of Scrolller media (owner, 2026-10-19: paying for the Scrolller licence, free
/// users get a capped daily share and are told so plainly; Basic and Prime get far more, the
/// upsell). Every byte that comes FRESH from a Scrolller host is charged here: pictures, GIFs,
/// clips and posters alike. A replay from the device cache (<see cref="ScrolllerDiskCache"/>)
/// costs nothing. The meter is shown as a percentage only, never as megabytes or a file count.
///
/// The ledger lives on this machine only (<c>scrolller-today.json</c> under the user data folder)
/// and is never sent anywhere: the no-tracking promise holds. The limits come from the server's
/// switch (<c>daily_mb</c> in <c>/config/scrolller</c>) so they can be tuned without a release;
/// until it answers, the defaults below apply.
///
/// A free install's FIRST day of online media gets <see cref="DefaultFreeFirstDayBytes"/> instead
/// (owner, 2026-10-19: room to fill the device cache, then the ordinary share from day two). The
/// cache size, the reuse mix and these figures are ours to set: never a user setting.
/// </summary>
internal static class ScrolllerAllowance
{
    public const long MB = 1024L * 1024;
    public const long DefaultFreeBytes = 300 * MB;
    public const long DefaultFreeFirstDayBytes = 500 * MB;
    public const long DefaultBasicBytes = 2048 * MB;
    public const long DefaultPrimeBytes = 6144 * MB;

    private static long _free = DefaultFreeBytes, _freeFirst = DefaultFreeFirstDayBytes, _basic = DefaultBasicBytes, _prime = DefaultPrimeBytes;
    private static readonly object Gate = new();
    private static ScrolllerLedger? _ledger;
    private static string? _spentAnnouncedDay; // the day the SpentReached edge fired for
    private static Timer? _saveTimer;

    /// <summary>Which plan the account is on. Replaceable by tests. Prime = tier 2 (HasLabAccess),
    /// Basic = tier 1 (HasPremiumAccess; whitelist and grace fold in there), else Free.</summary>
    internal static Func<ScrolllerPlan> PlanProvider = DefaultPlan;

    /// <summary>The local clock. Replaceable by tests.</summary>
    internal static Func<DateTime> Now = () => DateTime.Now;

    /// <summary>Where the ledger is kept. Replaceable by tests; null = keep it in memory only.</summary>
    internal static Func<string?> LedgerPath = () =>
    {
        try { return Path.Combine(App.UserDataPath, "scrolller-today.json"); }
        catch { return null; }
    };

    /// <summary>Raised (any thread) after every charge and every limit or day change.</summary>
    public static event EventHandler? Changed;

    /// <summary>Raised (any thread) once per day, on the charge that uses the share up.</summary>
    public static event EventHandler? SpentReached;

    public static ScrolllerPlan Plan
    {
        get { try { return PlanProvider(); } catch { return ScrolllerPlan.Free; } }
    }

    /// <summary>Today's share for <paramref name="plan"/>, in bytes.</summary>
    public static long DailyBytes(ScrolllerPlan plan) => plan switch
    {
        ScrolllerPlan.Prime => Volatile.Read(ref _prime),
        ScrolllerPlan.Basic => Volatile.Read(ref _basic),
        _ => Volatile.Read(ref _free),
    };

    /// <summary>Today's share for this account: a free install's first day gets the bigger
    /// first-day share (never less than the ordinary one).</summary>
    public static long LimitBytes
    {
        get
        {
            var plan = Plan;
            if (plan != ScrolllerPlan.Free || !IsFirstDay) return DailyBytes(plan);
            return Math.Max(DailyBytes(ScrolllerPlan.Free), Volatile.Read(ref _freeFirst));
        }
    }

    /// <summary>True on this install's first day of online media.</summary>
    public static bool IsFirstDay
    {
        get { lock (Gate) { var l = Ledger(); l.Roll(Now()); return l.IsFirstDay; } }
    }

    public static long UsedToday
    {
        get { lock (Gate) { var l = Ledger(); l.Roll(Now()); return l.Used; } }
    }

    /// <summary>0..100 for the meter (see <see cref="ScrolllerLedger.Percent"/>).</summary>
    public static int Percent => ScrolllerLedger.Percent(UsedToday, LimitBytes);

    /// <summary>True when today's share is used up: no FRESH Scrolller fetch until midnight.
    /// Cached copies keep playing.</summary>
    public static bool Spent => UsedToday >= LimitBytes;

    /// <summary>Next local midnight, when the share resets.</summary>
    public static DateTime NextResetLocal => Now().Date.AddDays(1);

    /// <summary>The server's limits in MB; a missing or negative value keeps the default. Never
    /// throws.</summary>
    public static void ApplyLimits(long? freeMb, long? basicMb, long? primeMb, long? freeFirstDayMb = null)
    {
        Volatile.Write(ref _free, ToBytes(freeMb, DefaultFreeBytes));
        Volatile.Write(ref _freeFirst, ToBytes(freeFirstDayMb, DefaultFreeFirstDayBytes));
        Volatile.Write(ref _basic, ToBytes(basicMb, DefaultBasicBytes));
        Volatile.Write(ref _prime, ToBytes(primeMb, DefaultPrimeBytes));
        Raise(Changed);
    }

    private static long ToBytes(long? mb, long fallback)
        => mb is long v && v >= 0 && v <= long.MaxValue / MB ? v * MB : fallback;

    /// <summary>Charges <paramref name="bytes"/> fetched fresh from Scrolller. Thread-safe, never
    /// throws. Fires <see cref="SpentReached"/> on the charge that crosses the limit.</summary>
    public static void Charge(long bytes)
    {
        if (bytes <= 0) return;
        bool crossed;
        lock (Gate)
        {
            var l = Ledger();
            var limit = LimitBytes;
            var now = Now();
            l.Roll(now);
            var before = l.Used;
            l.Charge(bytes, now);
            crossed = before < limit && l.Used >= limit && _spentAnnouncedDay != l.Day;
            if (crossed) _spentAnnouncedDay = l.Day;
        }
        ScheduleSave();
        Raise(Changed);
        if (crossed) Raise(SpentReached);
    }

    /// <summary>Writes the ledger now (app exit). Never throws.</summary>
    public static void Flush()
    {
        try { _saveTimer?.Dispose(); } catch { }
        _saveTimer = null;
        Save();
    }

    /// <summary>Tests only: forget the in-memory ledger and limits.</summary>
    internal static void ResetForTests()
    {
        lock (Gate) { _ledger = null; _spentAnnouncedDay = null; }
        Volatile.Write(ref _free, DefaultFreeBytes);
        Volatile.Write(ref _freeFirst, DefaultFreeFirstDayBytes);
        Volatile.Write(ref _basic, DefaultBasicBytes);
        Volatile.Write(ref _prime, DefaultPrimeBytes);
    }

    private static ScrolllerPlan DefaultPlan()
    {
        var p = App.Patreon;
        if (p == null) return ScrolllerPlan.Free;
        if (p.HasLabAccess) return ScrolllerPlan.Prime;
        if (p.HasPremiumAccess) return ScrolllerPlan.Basic;
        return ScrolllerPlan.Free;
    }

    private static ScrolllerLedger Ledger()
    {
        if (_ledger != null) return _ledger;
        _ledger = Load() ?? new ScrolllerLedger(ScrolllerLedger.DayOf(Now()), 0);
        return _ledger;
    }

    private static ScrolllerLedger? Load()
    {
        try
        {
            var path = LedgerPath();
            if (path == null || !File.Exists(path)) return null;
            var o = JObject.Parse(File.ReadAllText(path));
            var day = (string?)o["day"];
            var used = (long?)o["used"] ?? 0;
            var first = (string?)o["first"];
            return string.IsNullOrEmpty(day) ? null : new ScrolllerLedger(day!, used, string.IsNullOrEmpty(first) ? null : first);
        }
        catch { return null; }
    }

    private static void ScheduleSave()
    {
        try
        {
            if (_saveTimer != null) return;
            _saveTimer = new Timer(_ => { _saveTimer?.Dispose(); _saveTimer = null; Save(); }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }
        catch { }
    }

    private static void Save()
    {
        try
        {
            var path = LedgerPath();
            if (path == null) return;
            string json;
            lock (Gate)
            {
                if (_ledger == null) return;
                json = new JObject { ["day"] = _ledger.Day, ["used"] = _ledger.Used, ["first"] = _ledger.FirstDay }.ToString(Newtonsoft.Json.Formatting.None);
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAllowance: save failed: {E}", ex.Message); }
    }

    private static void Raise(EventHandler? handler)
    {
        try { handler?.Invoke(null, EventArgs.Empty); }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAllowance: handler failed: {E}", ex.Message); }
    }
}
