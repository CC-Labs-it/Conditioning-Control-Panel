using System;
using System.Collections.Generic;
using System.Globalization;

namespace ConditioningControlPanel.Services.Startup.Conversion
{
    /// <summary>Which lapse row an install owes right now.</summary>
    public enum LapseRow { None, Warn7, Warn3, Warn1, Closed }

    /// <summary>A lapse reading: the row, the tier that is closing (1 Basic, 2 Prime), its end, and
    /// whole days left (0 once closed).</summary>
    public readonly record struct LapseReading(LapseRow Row, int Tier, DateTime UntilUtc, int DaysLeft)
    {
        public static readonly LapseReading Nothing = new(LapseRow.None, 0, default, 0);

        /// <summary>The Inbox row key (also the <c>InboxStamps</c> key).</summary>
        public string Key => Row switch
        {
            LapseRow.Warn7 => ConversionRules.KeyLapseWarn7,
            LapseRow.Warn3 => ConversionRules.KeyLapseWarn3,
            LapseRow.Warn1 => ConversionRules.KeyLapseWarn1,
            LapseRow.Closed => ConversionRules.KeyLapseClosed,
            _ => "",
        };

        /// <summary>The stamp value: one post per grace end, so a later lapse warns again.</summary>
        public string Stamp => UntilUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The pure half of the conversion Inbox rows (brief contract C9). Every row goes through the
    /// presenter (<c>App.StartupLadder.PresentOrInbox</c>, routed by <c>IsQuiet</c>), so none of
    /// them can land inside a session, a game or a conversation; this class only decides WHETHER a
    /// row is owed and keeps each one to once a day (or once per lapse) through
    /// <see cref="Models.AppSettings.InboxStamps"/>.
    /// </summary>
    public static class ConversionRules
    {
        public const string KeyLapseWarn7 = "lapse_warn_7";
        public const string KeyLapseWarn3 = "lapse_warn_3";
        public const string KeyLapseWarn1 = "lapse_warn_1";
        public const string KeyLapseClosed = "lapse_closed";
        public const string KeyLapseReconnect = "lapse_reconnect";
        public const string KeyLapseGreeting = "lapse_greeting";

        /// <summary>How long after the close the closed row may still be posted (proposal 4d).</summary>
        public static readonly TimeSpan ClosedRowWindow = TimeSpan.FromDays(30);

        // Compiled fallback of GET /config/plans (brief C1). TODO(D1): read Services/Vault/PlanCatalog
        // once it lands on main; these stay as its fallback values.
        public const string BasicCheckoutUrl = "https://www.patreon.com/checkout/CodeBambi?rid=27604786";
        public const string PrimeCheckoutUrl = "https://www.patreon.com/checkout/CodeBambi?rid=27681505";

        /// <summary>The Patreon checkout that preselects the tier. Never the bare profile url.</summary>
        public static string PlanUrl(int tier) => tier >= 2 ? PrimeCheckoutUrl : BasicCheckoutUrl;

        /// <summary>The funnel's plan word for a tier.</summary>
        public static string PlanWord(int tier) => tier >= 2 ? "prime" : "basic";

        /// <summary>
        /// Whether a lapse row is owed. Only when the server has REPORTED a lower tier
        /// (<paramref name="serverTierSeen"/>, stored by EntitlementTierSync) and nothing live still
        /// covers the tier: a guess from a stale grace stamp alone never warns anyone.
        /// Stored 2 with the server below 2 closes Prime (the Lab stamp); stored 1 with the server
        /// at 0 closes Basic (the premium stamp).
        /// </summary>
        public static LapseReading DecideLapse(int storedTier, int? serverTierSeen, DateTime? premiumUntilUtc,
            DateTime? labUntilUtc, DateTime nowUtc, bool liveCoversIt)
        {
            if (serverTierSeen is not int server || liveCoversIt) return LapseReading.Nothing;
            var local = Math.Clamp(storedTier, 0, 2);
            if (server >= local) return LapseReading.Nothing;

            var tier = local >= 2 ? 2 : 1;
            var until = tier >= 2 ? (labUntilUtc ?? premiumUntilUtc) : premiumUntilUtc;
            if (until is not DateTime end) return LapseReading.Nothing;

            var remaining = end - nowUtc;
            if (remaining <= TimeSpan.Zero)
                return -remaining <= ClosedRowWindow ? new LapseReading(LapseRow.Closed, tier, end, 0) : LapseReading.Nothing;

            var days = (int)Math.Ceiling(remaining.TotalDays);
            var row = days <= 1 ? LapseRow.Warn1 : days <= 3 ? LapseRow.Warn3 : days <= 7 ? LapseRow.Warn7 : LapseRow.None;
            return row == LapseRow.None ? LapseReading.Nothing : new LapseReading(row, tier, end, days);
        }

        // ------------------------------------------------------------------ outside the moment (proposal 7)

        public const string KeyCompanionCap = "companion_cap";
        public const string KeyGiftRelock = "daily_gift_relock";
        public const string KeyPatronGallery = "patron_gallery";
        public const string KeyFreeMemberHello = "free_member_hello";
        public const string KeyPostCodeLive = "post_code_live";

        /// <summary>The InboxStamps slot that remembers the last Daily Gift seen in use:
        /// <c>yyyy-MM-dd|key</c> (a local date).</summary>
        public const string KeyGiftUsed = "gift_used";

        /// <summary>The companion's daily message caps per tier (brief owner decisions).</summary>
        public const int CompanionCapFree = 100, CompanionCapBasic = 1000, CompanionCapPrime = 2000;

        /// <summary>The tier the companion cap row offers after <paramref name="tierNow"/> (0 free,
        /// 1 Basic, 2 Prime), or null when there is nothing above it: Prime gets no row.</summary>
        public static int? CapRowOffers(int tierNow) => tierNow >= 2 ? null : tierNow == 1 ? 2 : 1;

        /// <summary>A "this Daily Gift was in use on this day" stamp value.</summary>
        public static string GiftUse(DateTime localDay, string key) => DayStamp(localDay) + "|" + key;

        /// <summary>The Daily Gift key a <see cref="GiftUse"/> stamp names when it was in use on the
        /// day before <paramref name="today"/>, or null.</summary>
        public static string? UsedYesterday(string? stamp, DateTime today)
        {
            if (string.IsNullOrEmpty(stamp)) return null;
            var bar = stamp.IndexOf('|');
            if (bar <= 0 || bar == stamp.Length - 1) return null;
            return stamp.Substring(0, bar) == DayStamp(today.Date.AddDays(-1)) ? stamp.Substring(bar + 1) : null;
        }

        /// <summary>
        /// "Yesterday's door is shut" is owed when yesterday's gift was in use, today it is shut
        /// again (not today's gift, no tier holds it), and it has not been posted today.
        /// </summary>
        public static bool RelockOwed(string? usedStamp, DateTime today, bool premium, Func<string, bool> openToday, out string key)
        {
            key = UsedYesterday(usedStamp, today) ?? "";
            if (key.Length == 0 || premium) return false;
            try { return !openToday(key); }
            catch { return false; }
        }

        /// <summary>The session recap's one taste line: only at the live end of a completed session
        /// (not a recap reopened from history) for an account with no tier.</summary>
        public static bool SessionTasteLine(bool completed, bool liveEnd, bool premium) => SessionRecapConversion.SessionTasteLine(completed, liveEnd, premium);

        // ------------------------------------------------------------------ stamps

        /// <summary>A local date as a stamp value.</summary>
        public static string DayStamp(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>True when <paramref name="key"/> has not been posted for <paramref name="value"/>.</summary>
        public static bool Owed(IReadOnlyDictionary<string, string>? stamps, string key, string value)
            => !string.IsNullOrEmpty(key)
               && (stamps == null || !stamps.TryGetValue(key, out var last) || !string.Equals(last, value, StringComparison.Ordinal));

        /// <summary>Records <paramref name="value"/> for <paramref name="key"/>. True when it changed.</summary>
        public static bool Stamp(IDictionary<string, string>? stamps, string key, string value)
        {
            if (stamps == null || string.IsNullOrEmpty(key)) return false;
            if (stamps.TryGetValue(key, out var last) && string.Equals(last, value, StringComparison.Ordinal)) return false;
            stamps[key] = value;
            return true;
        }
    }
}
