using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>The day this account first fetched Scrolller media on this PC; null until it does.</summary>
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

    /// <summary>Moves the ledger to <paramref name="local"/>'s day, zeroing it when the day moved
    /// FORWARD. True = it rolled over. A clock set back keeps the later day and its used bytes:
    /// winding the clock never buys a fresh share.</summary>
    public bool Roll(DateTime local)
    {
        var day = DayOf(local);
        if (string.CompareOrdinal(day, Day) <= 0) return false;
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
/// The ledger is PER ACCOUNT (owner, 2026-10-19: a second account on the same PC inherited the
/// first one's usage). One book (<c>scrolller-accounts.json</c> under the user data folder) holds a
/// ledger per account key: the first 16 hex of the SHA-256 of <see cref="App.UnifiedUserId"/> (the
/// id itself never lands on disk), or <see cref="AnonymousKey"/> while signed out. It lives on this
/// machine only and is never sent anywhere: the no-tracking promise holds. The machine-wide ledger
/// of earlier builds (<c>scrolller-today.json</c>) is adopted ONCE by whoever is signed in at the
/// first load, then deleted. The limits come from the server's switch (<c>daily_mb</c> in
/// <c>/config/scrolller</c>) so they can be tuned without a release; until it answers, the
/// defaults below apply.
///
/// A free account's FIRST day of online media on this PC gets <see cref="DefaultFreeFirstDayBytes"/>
/// instead (owner, 2026-10-19: room to fill the device cache, then the ordinary share from day two).
/// The cache size, the reuse mix and these figures are ours to set: never a user setting.
/// </summary>
internal static class ScrolllerAllowance
{
    public const long MB = 1024L * 1024;
    public const long DefaultFreeBytes = 300 * MB;
    public const long DefaultFreeFirstDayBytes = 500 * MB;
    public const long DefaultBasicBytes = 2048 * MB;
    public const long DefaultPrimeBytes = 6144 * MB;

    /// <summary>The book's key for signed-out use.</summary>
    internal const string AnonymousKey = "anon";

    private static long _free = DefaultFreeBytes, _freeFirst = DefaultFreeFirstDayBytes, _basic = DefaultBasicBytes, _prime = DefaultPrimeBytes;
    private static readonly object Gate = new();
    private static Dictionary<string, ScrolllerLedger>? _book; // every account's ledger, loaded once
    private static bool _legacyAdopted;        // the old machine-wide ledger has found its owner
    private static bool _legacyGone;           // ...and its file is deleted
    private static ScrolllerLedger? _ledger;   // the current account's entry in _book
    private static string? _ledgerKey;         // whose it is
    private static string? _keyId;             // the raw id _keyHash was made from (in memory only)
    private static string _keyHash = AnonymousKey;
    private static readonly Dictionary<string, string> SpentAnnounced = new(); // account key -> the day SpentReached fired for
    private static bool? _wasSpent;            // Spent at the last look; null = no look yet (startup is silent)
    private static string? _seenDay;           // what the last look saw, so a midnight, plan or limit
    private static ScrolllerPlan? _seenPlan;   // change raises Changed even when nothing is charged
    private static long _seenLimit = -1;

    private static readonly object SaveLock = new();  // the save timer field
    private static readonly object FileLock = new();  // one writer of the .tmp at a time
    private static Timer? _saveTimer;

    private static readonly object TickLock = new();
    private static Timer? _ticker;
    private static bool _tickerAllowed = true;
    private static EventHandler? _changed, _spentReached;

    /// <summary>How often the plan is re-read when nothing else happens.</summary>
    internal static readonly TimeSpan TickPeriod = TimeSpan.FromSeconds(60);

    /// <summary>Which plan the account is on. Replaceable by tests. Prime = tier 2 (HasLabAccess),
    /// Basic = tier 1 (HasPremiumAccess; whitelist and grace fold in there), else Free.</summary>
    internal static Func<ScrolllerPlan> PlanProvider = DefaultPlan;

    /// <summary>The local clock. Replaceable by tests.</summary>
    internal static Func<DateTime> Now = () => DateTime.Now;

    /// <summary>The signed-in account's id, null or empty when signed out. Replaceable by tests.</summary>
    internal static Func<string?> AccountProvider = () => App.UnifiedUserId;

    /// <summary>Where the per-account book is kept. Replaceable by tests; null = memory only.</summary>
    internal static Func<string?> LedgerPath = () =>
    {
        try { return Path.Combine(App.UserDataPath, "scrolller-accounts.json"); }
        catch { return null; }
    };

    /// <summary>The machine-wide ledger of earlier builds, adopted once. Replaceable by tests.</summary>
    internal static Func<string?> LegacyLedgerPath = () =>
    {
        try { return Path.Combine(App.UserDataPath, "scrolller-today.json"); }
        catch { return null; }
    };

    /// <summary>The book key for an account id: the first 16 hex of its SHA-256 (FriendsFeedFile's
    /// pattern, so the id never becomes file content), <see cref="AnonymousKey"/> when signed out.</summary>
    internal static string KeyFor(string? accountId)
    {
        if (string.IsNullOrEmpty(accountId)) return AnonymousKey;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(accountId));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>Raised (any thread) after every charge and every limit, plan or day change. The day
    /// and plan are watched by a timer (local midnight, else every <see cref="TickPeriod"/>) that
    /// starts with the first listener.</summary>
    public static event EventHandler? Changed
    {
        add { lock (TickLock) _changed += value; EnsureTicker(); }
        remove { lock (TickLock) _changed -= value; }
    }

    /// <summary>Raised (any thread) once per local day, when the share goes from left to used up
    /// for any cause: a charge, a lower limit or a plan drop. Never at startup for a day that was
    /// already spent.</summary>
    public static event EventHandler? SpentReached
    {
        add { lock (TickLock) _spentReached += value; EnsureTicker(); }
        remove { lock (TickLock) _spentReached -= value; }
    }

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

    /// <summary>Today's share for this account. A free account's FIRST day of online media gets
    /// max(free, free_first_day): the bigger first-day share, never less than the ordinary one
    /// (a server figure below the daily one is ignored). Paid plans always get their own figure.</summary>
    public static long LimitBytes
    {
        get
        {
            var plan = Plan;
            lock (Gate) { var l = Ledger(); l.Roll(Now()); return LimitFor(plan, l); }
        }
    }

    private static long LimitFor(ScrolllerPlan plan, ScrolllerLedger l)
    {
        if (plan != ScrolllerPlan.Free || !l.IsFirstDay) return DailyBytes(plan);
        return FreeFirstDayBytes;
    }

    /// <summary>A free account's first-day share: never less than the ordinary free one.</summary>
    public static long FreeFirstDayBytes => Math.Max(DailyBytes(ScrolllerPlan.Free), Volatile.Read(ref _freeFirst));

    /// <summary>True on this account's first day of online media on this PC.</summary>
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
        bool crossed;
        lock (Gate)
        {
            Look(out var before);
            Volatile.Write(ref _free, ToBytes(freeMb, DefaultFreeBytes));
            Volatile.Write(ref _freeFirst, ToBytes(freeFirstDayMb, DefaultFreeFirstDayBytes));
            Volatile.Write(ref _basic, ToBytes(basicMb, DefaultBasicBytes));
            Volatile.Write(ref _prime, ToBytes(primeMb, DefaultPrimeBytes));
            Look(out crossed);
            crossed |= before;
        }
        Raise(_changed);
        if (crossed) Raise(_spentReached);
    }

    private static long ToBytes(long? mb, long fallback)
        => mb is long v && v >= 0 && v <= long.MaxValue / MB ? v * MB : fallback;

    /// <summary>Charges <paramref name="bytes"/> fetched fresh from Scrolller. Thread-safe, never
    /// throws. Fires <see cref="SpentReached"/> on the charge that uses the share up.</summary>
    public static void Charge(long bytes)
    {
        if (bytes <= 0) return;
        bool crossed;
        lock (Gate)
        {
            Look(out var before); // the baseline (and any plan drop since the last look)
            Ledger().Charge(bytes, Now());
            Look(out crossed);
            crossed |= before;
        }
        ScheduleSave();
        Raise(_changed);
        if (crossed) Raise(_spentReached);
    }

    /// <summary>The timer's look: raises <see cref="Changed"/> when the day, plan or limit moved
    /// since the last look, and <see cref="SpentReached"/> when the share went from left to used
    /// up (a plan drop). Called every <see cref="TickPeriod"/> and at local midnight.</summary>
    internal static void Tick()
    {
        bool moved, crossed;
        lock (Gate) moved = Look(out crossed);
        if (moved) Raise(_changed);
        if (crossed) Raise(_spentReached);
    }

    /// <summary>Under <see cref="Gate"/>: rolls the day, reads the plan and limit, and updates the
    /// Spent edge. True = the day, plan or limit differs from the last look. The first look only
    /// sets the baseline, so a day already spent at startup never fires.</summary>
    private static bool Look(out bool crossed)
    {
        var l = Ledger();
        l.Roll(Now());
        var plan = Plan;
        var limit = LimitFor(plan, l);
        var moved = _seenDay != l.Day || _seenPlan != plan || _seenLimit != limit;
        if (_seenDay != null && _seenDay != l.Day) _wasSpent = false; // a new day starts with its share
        var spent = l.Used >= limit;
        var was = _wasSpent ?? spent;
        _wasSpent = spent;
        var key = _ledgerKey ?? AnonymousKey;
        crossed = spent && !was && !(SpentAnnounced.TryGetValue(key, out var announced) && announced == l.Day);
        if (crossed) SpentAnnounced[key] = l.Day;
        _seenDay = l.Day;
        _seenPlan = plan;
        _seenLimit = limit;
        return moved;
    }

    private static void EnsureTicker()
    {
        lock (TickLock)
        {
            if (_ticker != null || !_tickerAllowed) return;
            try { _ticker = new Timer(_ => OnTicker(), null, NextTickDue(), Timeout.InfiniteTimeSpan); }
            catch { }
        }
    }

    private static void OnTicker()
    {
        try { Tick(); }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAllowance: tick failed: {E}", ex.Message); }
        lock (TickLock)
        {
            try { _ticker?.Change(NextTickDue(), Timeout.InfiniteTimeSpan); }
            catch { }
        }
    }

    /// <summary>The next look: a second past local midnight when that comes first, else one period.</summary>
    private static TimeSpan NextTickDue()
    {
        try
        {
            var toMidnight = NextResetLocal - Now() + TimeSpan.FromSeconds(1);
            if (toMidnight < TimeSpan.FromSeconds(1)) toMidnight = TimeSpan.FromSeconds(1);
            return toMidnight < TickPeriod ? toMidnight : TickPeriod;
        }
        catch { return TickPeriod; }
    }

    /// <summary>Writes the ledger now (app exit). Never throws. A charge after this still saves.</summary>
    public static void Flush()
    {
        lock (SaveLock)
        {
            try { _saveTimer?.Dispose(); } catch { }
            _saveTimer = null;
        }
        Save();
    }

    /// <summary>Tests only: forget the in-memory ledger and limits; no timer ever starts.</summary>
    internal static void ResetForTests()
    {
        lock (TickLock)
        {
            _tickerAllowed = false;
            try { _ticker?.Dispose(); } catch { }
            _ticker = null;
        }
        lock (Gate)
        {
            _book = null; _legacyAdopted = false; _legacyGone = false;
            _ledger = null; _ledgerKey = null; _keyId = null; _keyHash = AnonymousKey;
            SpentAnnounced.Clear();
            _wasSpent = null; _seenDay = null; _seenPlan = null; _seenLimit = -1;
        }
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

    /// <summary>The signed-in account's id, flips to the anonymous key on any failure.</summary>
    private static string? AccountId()
    {
        try { return AccountProvider(); } catch { return null; }
    }

    /// <summary>Under <see cref="Gate"/>: the book key of whoever is signed in now. The hash is
    /// only recomputed when the id moves.</summary>
    private static string CurrentKey()
    {
        var id = AccountId();
        if (!string.Equals(id, _keyId, StringComparison.Ordinal))
        {
            _keyId = id;
            _keyHash = KeyFor(id);
        }
        return _keyHash;
    }

    /// <summary>Under <see cref="Gate"/>: the signed-in account's ledger. When the account moved
    /// since the last call, the Spent edge starts over silently (as at startup), so switching to an
    /// account whose day is already spent never fires the notice.</summary>
    private static ScrolllerLedger Ledger()
    {
        var key = CurrentKey();
        if (_ledger != null && _ledgerKey == key) return _ledger;
        var book = Book(key);
        if (!book.TryGetValue(key, out var l))
        {
            l = new ScrolllerLedger(ScrolllerLedger.DayOf(Now()), 0);
            book[key] = l;
        }
        if (_ledgerKey != null && _ledgerKey != key)
        {
            _wasSpent = null; _seenDay = null; _seenPlan = null; _seenLimit = -1;
        }
        _ledger = l;
        _ledgerKey = key;
        return l;
    }

    /// <summary>Under <see cref="Gate"/>: the book, read from disk once. The first read adopts the
    /// old machine-wide ledger for <paramref name="firstKey"/> (whoever is signed in then), once:
    /// the book records the adoption and the old file is deleted after the next save (app exit
    /// always saves; only a crash before the first save leaves it for the next launch).</summary>
    private static Dictionary<string, ScrolllerLedger> Book(string firstKey)
    {
        if (_book != null) return _book;
        var book = new Dictionary<string, ScrolllerLedger>(StringComparer.Ordinal);
        try
        {
            var path = LedgerPath();
            if (path != null && File.Exists(path))
            {
                var o = JObject.Parse(File.ReadAllText(path));
                _legacyAdopted = (bool?)o["legacy_adopted"] ?? false;
                if (o["accounts"] is JObject accounts)
                    foreach (var p in accounts.Properties())
                        if (p.Value is JObject e && Parse(e) is ScrolllerLedger l) book[p.Name] = l;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAllowance: book unreadable: {E}", ex.Message); }

        if (!_legacyAdopted)
        {
            try
            {
                var legacy = LegacyLedgerPath();
                if (legacy != null && File.Exists(legacy)
                    && Parse(JObject.Parse(File.ReadAllText(legacy))) is ScrolllerLedger old
                    && !book.ContainsKey(firstKey))
                {
                    book[firstKey] = old;
                    App.Logger?.Information("ScrolllerAllowance: adopted the machine-wide ledger for the {Who} account",
                        firstKey == AnonymousKey ? "signed-out" : "signed-in");
                }
            }
            catch (Exception ex) { App.Logger?.Debug("ScrolllerAllowance: old ledger unreadable: {E}", ex.Message); }
            // Decided once, file or not: it never moves to another account. Written with the next
            // save (a charge, an account switch, app exit), never by a mere read.
            _legacyAdopted = true;
        }
        _book = book;
        return book;
    }

    private static ScrolllerLedger? Parse(JObject o)
    {
        try
        {
            var day = (string?)o["day"];
            var used = (long?)o["used"] ?? 0;
            var first = (string?)o["first"];
            return string.IsNullOrEmpty(day) ? null : new ScrolllerLedger(day!, used, string.IsNullOrEmpty(first) ? null : first);
        }
        catch { return null; }
    }

    /// <summary>Hooks <see cref="App.UnifiedIdentityChanged"/> (sign-in, sign-out, account switch,
    /// a session the server refused). Idempotent; called once at startup. Never throws.</summary>
    internal static void WatchAccount()
    {
        if (Interlocked.Exchange(ref _watchingAccount, 1) != 0) return;
        try { App.UnifiedIdentityChanged += (_, _) => AccountChanged(); }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAllowance: account watch failed: {E}", ex.Message); }
    }

    private static int _watchingAccount;

    /// <summary>The signed-in account moved: the meter switches to the new account's ledger (or
    /// the anonymous one), the book is written off the calling thread so the old account's day is
    /// on disk, and <see cref="Changed"/> repaints the meter. Never throws.</summary>
    internal static void AccountChanged()
    {
        bool loaded;
        lock (Gate)
        {
            loaded = _book != null;
            if (loaded) Look(out _); // a fresh baseline for the new account: silent
        }
        if (loaded)
        {
            try { Task.Run(Flush); }
            catch { ScheduleSave(); }
        }
        Raise(_changed);
    }

    private static void ScheduleSave()
    {
        lock (SaveLock)
        {
            try
            {
                if (_saveTimer != null) return;
                _saveTimer = new Timer(_ => OnSaveTimer(), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            }
            catch { }
        }
    }

    private static void OnSaveTimer()
    {
        // Drop the timer BEFORE saving: a charge that lands during the write schedules a new one.
        lock (SaveLock)
        {
            try { _saveTimer?.Dispose(); } catch { }
            _saveTimer = null;
        }
        Save();
    }

    private static void Save()
    {
        try
        {
            var path = LedgerPath();
            if (path == null) return;
            lock (FileLock) // Flush and the debounce timer never share the .tmp
            {
                string json;
                bool dropLegacy;
                lock (Gate)
                {
                    if (_book == null) return;
                    var accounts = new JObject();
                    foreach (var (key, l) in _book)
                        accounts[key] = new JObject { ["day"] = l.Day, ["used"] = l.Used, ["first"] = l.FirstDay };
                    json = new JObject { ["v"] = 1, ["legacy_adopted"] = _legacyAdopted, ["accounts"] = accounts }
                        .ToString(Newtonsoft.Json.Formatting.None);
                    dropLegacy = _legacyAdopted && !_legacyGone;
                }
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, path, overwrite: true);
                if (dropLegacy)
                {
                    var legacy = LegacyLedgerPath();
                    if (legacy != null && File.Exists(legacy)) File.Delete(legacy);
                    lock (Gate) _legacyGone = true;
                }
            }
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAllowance: save failed: {E}", ex.Message); }
    }

    private static void Raise(EventHandler? handler)
    {
        try { handler?.Invoke(null, EventArgs.Empty); }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAllowance: handler failed: {E}", ex.Message); }
    }
}
