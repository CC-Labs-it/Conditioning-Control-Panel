using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The small "Your Prime is on" card: owed once per RISE of the effective tier over the last
    /// tier this install knew. The first reading ever only records (never a card on app start for
    /// a tier already held). A drop is recorded only when <c>mayRecordDrop</c> (the server
    /// answered this run), so a stale cache at startup never sets up a false "it's on" later.
    /// </summary>
    public static class TierOnNoticeRule
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
