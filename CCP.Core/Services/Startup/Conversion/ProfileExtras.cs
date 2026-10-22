using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Startup.Conversion
{
    /// <summary>The additive <c>/v2/user/profile</c> fields this lane reads (brief C2, C6). A null
    /// member means "the server did not say" (older server): keep what is stored.</summary>
    public sealed record ProfileExtrasReading(bool? PatreonMember, bool PaidUntilSent, DateTime? PaidUntilUtc,
        Dictionary<string, string>? PostGrants);

    /// <summary>
    /// Pure reading of the profile's conversion fields, and the two questions the Daily Gift asks of
    /// them: is a post grant still open, and which key is a free member's second gift today.
    /// </summary>
    public static class ProfileExtras
    {
        /// <summary>Daily Gift keys a post code may open (brief C6). Anything else is ignored.</summary>
        public static readonly string[] GrantKeys = { "takeover", "awareness", "fyp", "remote", "haptics", "voice", "dtrh" };

        public static ProfileExtrasReading? Parse(JObject? user)
        {
            if (user == null) return null;
            bool? member = user["patreon_member"]?.Type == JTokenType.Boolean ? user.Value<bool>("patreon_member") : null;

            var paidSent = user.ContainsKey("paid_until");
            var paid = paidSent ? ParseUtc(user["paid_until"]) : null;

            Dictionary<string, string>? grants = null;
            if (user["post_grants"] is JObject g)
            {
                grants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in g.Properties())
                {
                    var key = p.Name.Trim().ToLowerInvariant();
                    if (!GrantKeys.Contains(key) || ParseUtc(p.Value) is not DateTime until) continue;
                    grants[key] = until.ToString("o", CultureInfo.InvariantCulture);
                }
            }
            return new ProfileExtrasReading(member, paidSent, paid, grants);
        }

        /// <summary>Writes a reading onto settings. Only what the server actually sent moves.
        /// True when anything changed.</summary>
        public static bool Apply(AppSettings settings, ProfileExtrasReading? reading)
        {
            if (settings == null || reading == null) return false;
            var changed = false;
            if (reading.PatreonMember is bool member && member != settings.PatreonMember)
            {
                settings.PatreonMember = member;
                changed = true;
            }
            if (reading.PaidUntilSent && reading.PaidUntilUtc != settings.ServerPaidUntil)
            {
                settings.ServerPaidUntil = reading.PaidUntilUtc;
                changed = true;
            }
            if (reading.PostGrants != null && !SameGrants(settings.PostGrants, reading.PostGrants))
            {
                settings.PostGrants = new Dictionary<string, string>(reading.PostGrants, StringComparer.OrdinalIgnoreCase);
                changed = true;
            }
            return changed;
        }

        /// <summary>True while a post grant for <paramref name="key"/> has not ended.</summary>
        public static bool GrantOpen(IReadOnlyDictionary<string, string>? grants, string? key, DateTime nowUtc)
        {
            if (grants == null || string.IsNullOrEmpty(key)) return false;
            foreach (var kv in grants)
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return ParseUtc(kv.Value) is DateTime until && until > nowUtc;
            return false;
        }

        /// <summary>
        /// A free member's second Daily Gift (proposal 6b): the server's <c>member_key</c> when it
        /// names one that differs from today's, else tomorrow's seeded pick, else the first pool key
        /// that is not today's. Never today's key, never null for a non-empty pool.
        /// </summary>
        public static string? MemberSecondKey(string todayKey, string? serverMemberKey, string? tomorrowSeeded, IReadOnlyList<string> pool)
        {
            bool Other(string? k) => !string.IsNullOrEmpty(k) && !string.Equals(k, todayKey, StringComparison.OrdinalIgnoreCase);
            if (Other(serverMemberKey)) return serverMemberKey;
            if (Other(tomorrowSeeded)) return tomorrowSeeded;
            return pool.FirstOrDefault(k => Other(k));
        }

        private static bool SameGrants(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
            => a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

        private static DateTime? ParseUtc(JToken? t)
        {
            var s = t?.Type == JTokenType.String ? (string?)t : t?.Type == JTokenType.Date ? t.Value<DateTime>().ToString("o") : null;
            return ParseUtc(s);
        }

        internal static DateTime? ParseUtc(string? s)
            => !string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, CultureInfo.InvariantCulture,
                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var v)
                ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
    }
}
