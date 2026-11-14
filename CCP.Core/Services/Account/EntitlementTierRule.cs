using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>What a server <c>effective_tier</c> reading means for this install.</summary>
    public enum EntitlementVerdict
    {
        /// <summary>No usable reading (older server, garbage, out of range). Touch nothing.</summary>
        NoSignal,

        /// <summary>The server agrees with what this install already holds.</summary>
        Same,

        /// <summary>The server says more than we hold: unlock now.</summary>
        Rise,

        /// <summary>
        /// The server says less than the stored tier. Remembered for the lapse rows; the same reading
        /// again ends the grace it no longer backs (<see cref="EntitlementTierRule.ApplyReading"/>).
        /// </summary>
        Lower
    }

    /// <summary>
    /// Instant unlock for a subscription bought outside the app (PayPal / Stripe on the site).
    ///
    /// <para>The server folds every provider into one <c>effective_tier</c> (0..2, whitelist is 2)
    /// on the heartbeat and on <c>/v2/user/profile</c>. This class is the pure half: parse it,
    /// compare it with the tier this install last stored, and stamp the same 14-day grace windows
    /// the sign-in path writes. The live half is <see cref="EntitlementTierSync"/>.</para>
    ///
    /// <para>Compared against the STORED <see cref="AppSettings.PatreonTier"/> and never against
    /// live access, so a rise means someone really gained a tier. A signed-in launch keeps that
    /// value (owner, 2026-11-01): zeroing it made every launch read as a purchase and left the
    /// server's "no" nothing to compare against. While the server says a tier, the grace is renewed;
    /// with PATREON_ALLOW_LOWER on, that tier falls when the patron goes.</para>
    /// </summary>
    public static class EntitlementTierRule
    {
        public const int MaxTier = 2;
        public const int GraceDays = 14;

        /// <summary>An integer 0..2, or null for anything else (absent, string, float, out of range).</summary>
        public static int? ParseTier(JToken? token)
        {
            if (token is null || token.Type != JTokenType.Integer) return null;
            long v;
            try { v = token.Value<long>(); } catch { return null; }
            return v >= 0 && v <= MaxTier ? (int)v : null;
        }

        /// <summary>Top-level <c>effective_tier</c> off a heartbeat body, or null. Never throws.</summary>
        public static int? ParseHeartbeatTier(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try { return JObject.Parse(body) is JObject root ? ParseTier(root["effective_tier"]) : null; }
            catch { return null; }
        }

        public static EntitlementVerdict Decide(int storedTier, int? serverTier)
        {
            if (serverTier is null || serverTier < 0 || serverTier > MaxTier) return EntitlementVerdict.NoSignal;
            var local = Math.Clamp(storedTier, 0, MaxTier);
            if (serverTier.Value > local) return EntitlementVerdict.Rise;
            return serverTier.Value < local ? EntitlementVerdict.Lower : EntitlementVerdict.Same;
        }

        /// <summary>
        /// What <see cref="AppSettings.ServerTierSeen"/> becomes after a reading: the server tier on
        /// a Lower verdict, null once the server agrees again or rose, unchanged on no signal. The
        /// lapse rows read it; access never does.
        /// </summary>
        public static int? ServerTierSeenAfter(EntitlementVerdict verdict, int? serverTier, int? current) => verdict switch
        {
            EntitlementVerdict.Lower => serverTier,
            EntitlementVerdict.Same or EntitlementVerdict.Rise => null,
            _ => current,
        };

        /// <summary>
        /// The 14-day grace stamps for a server-confirmed tier. Premium at tier 1+, the Lab stamp
        /// only at tier 2. Never shortens an existing longer window. The ONE copy of this rule:
        /// <see cref="ProfileAdopt.ApplyUserData"/> calls it too.
        /// </summary>
        public static void ExtendGrace(AppSettings settings, int tier, DateTime nowUtc)
        {
            if (settings == null || tier < 1) return;
            var until = nowUtc.AddDays(GraceDays);
            if (settings.PatreonPremiumValidUntil == null || settings.PatreonPremiumValidUntil < until)
                settings.PatreonPremiumValidUntil = until;
            if (tier >= 2 && (settings.PatreonLabValidUntil == null || settings.PatreonLabValidUntil < until))
                settings.PatreonLabValidUntil = until;
        }

        /// <summary>The settings half of a live rise (EntitlementTierSync): store the tier, stamp grace. True on a rise.</summary>
        public static bool ApplyRise(AppSettings settings, int? serverTier, DateTime nowUtc)
        {
            if (Decide(settings.PatreonTier, serverTier) != EntitlementVerdict.Rise) return false;
            settings.PatreonTier = serverTier!.Value;
            ExtendGrace(settings, serverTier.Value, nowUtc);
            return true;
        }

        /// <summary>How long a closed plan stays the news. Here because Core cannot see the head's
        /// ConversionRules, which reads this same value.</summary>
        public static readonly TimeSpan ClosedRowWindow = TimeSpan.FromDays(30);

        /// <summary>The shortest time between the two agreeing readings that end a grace window.</summary>
        public static readonly TimeSpan GraceConfirmGap = TimeSpan.FromSeconds(90);

        /// <summary>
        /// One server reading against the grace (owner, 2026-11-01). A Lower verdict needs the same
        /// reading again at least <see cref="GraceConfirmGap"/> later, because a reading carries no
        /// account or order, and waits while <paramref name="held"/> (a session or a game owns the
        /// screen). Then the stamps close, the stored tier falls to the server's, and
        /// <see cref="AppSettings.GraceClosedTier"/> keeps the closed row owed. Paying again is then an
        /// ordinary rise. A sign-in that lowers the stored tier gives a Same verdict and never closes
        /// anything: it can be a blank twin record that has no tier yet.
        /// </summary>
        public static GraceOutcome ApplyReading(AppSettings settings, EntitlementVerdict verdict, int? serverTier,
            GracePending? pending, Func<bool> held, DateTime nowUtc)
        {
            if (verdict == EntitlementVerdict.NoSignal || serverTier is not int tier) return new GraceOutcome(false, false, pending);

            var dirty = false;
            var closed = false;
            GracePending? next = null;
            if (verdict == EntitlementVerdict.Lower)
            {
                next = pending?.Tier == tier ? pending : new GracePending(tier, nowUtc);
                if (nowUtc - next.SinceUtc >= GraceConfirmGap && !held())
                {
                    settings.GraceClosedTier = Math.Clamp(settings.PatreonTier, 0, MaxTier);
                    settings.GraceClosedAtUtc = nowUtc;
                    CloseGrace(settings, tier, nowUtc);
                    settings.PatreonTier = tier;
                    closed = dirty = true;
                    next = null;
                }
            }
            if (!closed && verdict != EntitlementVerdict.Rise && RenewGrace(settings, tier, nowUtc)) dirty = true;
            if (settings.GraceClosedTier is int lost && (tier >= lost || ClosedTierNow(settings, nowUtc) == null))
            {
                settings.GraceClosedTier = null;
                settings.GraceClosedAtUtc = null;
                dirty = true;
            }
            return new GraceOutcome(closed, dirty, next);
        }

        /// <summary>
        /// While the server says a tier, its grace stays full. This is what used to happen by accident
        /// on every launch, when the zeroed stored tier made the reading a rise; it keeps PayPal and
        /// Stripe payers open. Moves at most about once a day, so a heartbeat does not save each time.
        /// </summary>
        public static bool RenewGrace(AppSettings settings, int tier, DateTime nowUtc)
        {
            if (settings == null || tier < 1) return false;
            var due = nowUtc.AddDays(GraceDays - 1);
            var stale = !(settings.PatreonPremiumValidUntil >= due) || (tier >= 2 && !(settings.PatreonLabValidUntil >= due));
            if (stale) ExtendGrace(settings, tier, nowUtc);
            return stale;
        }

        /// <summary>
        /// A believed reading ends the stamps it no longer backs (owner, 2026-11-01). The stamp is
        /// what a Discord sign-in patron's access rides on, so it stays: an offline PC has no reading
        /// and keeps its window. Set to now rather than cleared, so the lapse row can name the day.
        /// </summary>
        public static void CloseGrace(AppSettings settings, int tier, DateTime nowUtc)
        {
            if (tier < 1 && settings.PatreonPremiumValidUntil > nowUtc) settings.PatreonPremiumValidUntil = nowUtc;
            if (tier < 2 && settings.PatreonLabValidUntil > nowUtc) settings.PatreonLabValidUntil = nowUtc;
        }

        /// <summary>
        /// The tier a confirmed reading closed, while that is still the news: for the closed row's
        /// window, then null. It says "subscribe" rather than "reconnect", and a link that broke months
        /// later needs the Reconnect answer back.
        /// </summary>
        public static int? ClosedTierNow(AppSettings? settings, DateTime nowUtc)
            => settings?.GraceClosedTier is int tier && settings.GraceClosedAtUtc is DateTime at
               && nowUtc - at <= ClosedRowWindow
                ? tier
                : null;

        /// <summary>
        /// The whitelist's premium refresh: at least this far ahead, never shorter than a reading's
        /// stamp. Writing it flat made every sync shorten what the next reading renewed.
        /// </summary>
        public static void ExtendPremiumTo(AppSettings settings, DateTime untilUtc)
        {
            if (!(settings.PatreonPremiumValidUntil >= untilUtc)) settings.PatreonPremiumValidUntil = untilUtc;
        }
    }

    /// <summary>A lower reading waiting for the same reading again.</summary>
    public sealed record GracePending(int Tier, DateTime SinceUtc);

    /// <summary>What one reading did: <see cref="EntitlementTierRule.ApplyReading"/>.</summary>
    public sealed record GraceOutcome(bool Closed, bool Dirty, GracePending? Pending);

    /// <summary>
    /// Which "premium unlocked" card an install still owes. One seen-flag per tier, so a
    /// Basic -> Prime upgrade celebrates again. The pre-tier key (<see cref="LegacyKey"/>) still
    /// counts on the launch re-check, so an existing install is not re-celebrated for the tier it
    /// already had; a LIVE rise ignores it, because a rise is a new purchase.
    /// </summary>
    public static class TierCelebration
    {
        public const string LegacyKey = "premium-celebration";
        public const string KeyT1 = "premium-celebration-t1";
        public const string KeyT2 = "premium-celebration-t2";

        public static string? KeyFor(int tier) => tier >= 2 ? KeyT2 : tier == 1 ? KeyT1 : null;

        public static bool IsOwed(ICollection<string>? seen, int tier, bool onRise)
        {
            var key = KeyFor(tier);
            if (key == null) return false;
            if (seen == null) return true;
            if (seen.Contains(key)) return false;
            return onRise || !seen.Contains(LegacyKey);
        }
    }
}
