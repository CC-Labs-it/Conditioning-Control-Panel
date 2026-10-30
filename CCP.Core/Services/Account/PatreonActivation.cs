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
        /// <summary>Apply the typed profile without depending on a head's account service.</summary>
        public static bool RestampProfile(AppSettings settings, V2AuthService.V2User user, DateTime nowUtc) =>
            Restamp(settings, new PatreonActiveReading(user.PatreonIsActive,
                Invites.InviteRules.ParseUtc(user.PatreonConfirmedAtRaw), user.PatreonTier), nowUtc);

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

}
