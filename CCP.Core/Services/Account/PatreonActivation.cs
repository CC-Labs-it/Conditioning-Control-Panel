using System;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>The server's Patreon reading off <c>/v2/user/profile</c> or <c>/v2/user/me</c>.</summary>
    public sealed record PatreonActiveReading(bool IsActive, DateTime? ConfirmedAtUtc, int PatreonTier);

    /// <summary>
    /// Keeps the 14-day Patreon grace alive for a patron who never signs in with Patreon (Discord
    /// sign-in, a linked Patreon). The server stamps <c>patreon_confirmed_at</c> whenever it sees an
    /// ACTIVE Patreon reading (webhook, hourly reconcile, validate); a fresh stamp re-stamps the
    /// grace from that moment. A stale one never does: the grace then runs out on its own.
    /// </summary>
    public static class PatreonActiveRule
    {
        /// <summary>A confirmation older than this says nothing about today.</summary>
        public static readonly TimeSpan FreshFor = TimeSpan.FromHours(72);

        /// <summary>Server clock ahead of ours by more than this: the stamp is not believed.</summary>
        public static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(10);

        /// <summary>Null when the server sent neither field (older server). Never throws.</summary>
        public static PatreonActiveReading? Parse(JObject? node)
        {
            if (node == null || node["patreon_is_active"] is not JToken active || active.Type != JTokenType.Boolean)
                return null;
            var tier = EntitlementTierRule.ParseTier(node["patreon_tier"]) ?? 0;
            return new PatreonActiveReading(active.Value<bool>(), Invites.InviteRules.ParseUtc(node["patreon_confirmed_at"]), tier);
        }

        /// <summary>True when the reading proves a paying patron within <see cref="FreshFor"/>.</summary>
        public static bool IsFresh(PatreonActiveReading? r, DateTime nowUtc)
        {
            if (r is not { IsActive: true, ConfirmedAtUtc: DateTime at } || r.PatreonTier < 1) return false;
            return at <= nowUtc + FutureSkew && nowUtc - at < FreshFor;
        }

        /// <summary>
        /// Re-stamps the grace (14 days from the server's confirmation, never from now, never
        /// shortening a longer window). True when a stamp moved. Whitelist logic is untouched.
        /// </summary>
        public static bool Restamp(AppSettings settings, PatreonActiveReading? r, DateTime nowUtc)
        {
            if (settings == null || !IsFresh(r, nowUtc)) return false;
            var anchor = r!.ConfirmedAtUtc!.Value > nowUtc ? nowUtc : r.ConfirmedAtUtc.Value;
            var premium = settings.PatreonPremiumValidUntil;
            var lab = settings.PatreonLabValidUntil;
            EntitlementTierRule.ExtendGrace(settings, Math.Min(r.PatreonTier, EntitlementTierRule.MaxTier), anchor);
            return premium != settings.PatreonPremiumValidUntil || lab != settings.PatreonLabValidUntil;
        }
    }

    /// <summary>
    /// After a checkout link opens, the next times the app comes back to the front re-read the
    /// server tier: for <see cref="WatchFor"/>, at most once per <see cref="Gap"/>.
    /// </summary>
    public static class CheckoutWatch
    {
        public static readonly TimeSpan WatchFor = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan Gap = TimeSpan.FromSeconds(20);

        private static readonly object Lock = new();
        private static DateTime? _armedUtc;
        private static DateTime _lastCheckUtc = DateTime.MinValue;

        /// <summary>A Patreon checkout, or the site's own checkout. Https only.</summary>
        public static bool IsCheckoutUrl(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return false;
            var host = u.Host.ToLowerInvariant();
            var path = u.AbsolutePath.ToLowerInvariant();
            if (host is "patreon.com" or "www.patreon.com") return path.StartsWith("/checkout/") || path == "/checkout";
            if (host is "app.cclabs.app" or "cclabs.app") return path == "/subscribe" || path.StartsWith("/subscribe/");
            return false;
        }

        /// <summary>Armed within <see cref="WatchFor"/> and no check in the last <see cref="Gap"/>.</summary>
        public static bool ShouldCheck(DateTime? armedUtc, DateTime lastCheckUtc, DateTime nowUtc)
            => armedUtc is DateTime armed && nowUtc >= armed && nowUtc - armed <= WatchFor
               && (nowUtc - lastCheckUtc >= Gap || nowUtc < lastCheckUtc);

        /// <summary>Arms the watch when <paramref name="url"/> is a checkout. Never throws.</summary>
        public static void NoteOpened(string? url, DateTime? nowUtc = null)
        {
            if (!IsCheckoutUrl(url)) return;
            lock (Lock) _armedUtc = nowUtc ?? DateTime.UtcNow;
        }

        /// <summary>True (and the slot spent) when a focus now owes a check.</summary>
        public static bool TakeCheck(DateTime nowUtc)
        {
            lock (Lock)
            {
                if (!ShouldCheck(_armedUtc, _lastCheckUtc, nowUtc)) return false;
                _lastCheckUtc = nowUtc;
                return true;
            }
        }

        /// <summary>The purchase landed (or a test): stop watching.</summary>
        public static void Disarm()
        {
            lock (Lock) { _armedUtc = null; _lastCheckUtc = DateTime.MinValue; }
        }

        internal static bool IsArmed { get { lock (Lock) return _armedUtc != null; } }
    }

    /// <summary>What the "I just subscribed" button found.</summary>
    public enum JustSubscribedOutcome { PrimeOn, BasicOn, NotYet, Unreachable, SignIn }

    public static class JustSubscribedRule
    {
        public static JustSubscribedOutcome Decide(bool signedIn, bool reached, int tierBefore, int tierAfter)
        {
            if (tierAfter >= 2) return JustSubscribedOutcome.PrimeOn;
            if (tierAfter > tierBefore) return JustSubscribedOutcome.BasicOn;
            if (!signedIn) return JustSubscribedOutcome.SignIn;
            return reached ? JustSubscribedOutcome.NotYet : JustSubscribedOutcome.Unreachable;
        }

        public static string LocKey(JustSubscribedOutcome o) => o switch
        {
            JustSubscribedOutcome.PrimeOn => "plans_just_subscribed_prime",
            JustSubscribedOutcome.BasicOn => "plans_just_subscribed_basic",
            JustSubscribedOutcome.SignIn => "plans_just_subscribed_signin",
            JustSubscribedOutcome.Unreachable => "plans_just_subscribed_unreachable",
            _ => "plans_just_subscribed_not_yet",
        };
    }

    /// <summary>
    /// The small "Your Prime is on" card: owed once per RISE of the effective tier over the last
    /// tier this install knew. The first reading ever only records (never a card on app start for
    /// a tier already held). A drop is recorded only when <c>mayRecordDrop</c> (the server
    /// answered this run), so a stale cache at startup never sets up a false "it's on" later.
    /// </summary>
    public static class TierOnNotice
    {
        public readonly record struct Verdict(bool Owed, int? Record);

        public static Verdict Decide(int? lastKnown, int current, bool mayRecordDrop)
        {
            current = Math.Clamp(current, 0, EntitlementTierRule.MaxTier);
            if (lastKnown is not int last) return new Verdict(false, current);
            if (current > last) return new Verdict(current >= 1, current);
            if (current < last && mayRecordDrop) return new Verdict(false, current);
            return new Verdict(false, last);
        }

        public static string TitleKey(int tier) => tier >= 2 ? "tier_on_prime" : "tier_on_basic";
    }
}
