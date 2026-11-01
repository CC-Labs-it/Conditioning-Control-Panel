using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room prize ownership on this head. WPF asks <c>PrizeGrants.IsGranted</c>, backed by
    /// OwnershipService snapshots from BackRoomApi. Here only the snapshots a counter reply carries
    /// (<see cref="ApplySnapshot"/>, fed by <see cref="V2PurchaseHead"/>) are held, for this run;
    /// the profile-sync feed is parity row svc-prize-ownership. The other source is WPF's DEBUG-only
    /// <c>CCP_PRIZE_GRANTS</c> override (Core OwnershipService.ReadDebugOverride): same variable,
    /// same patterns (exact id, <c>prefix.*</c>, <c>*</c>), and a Release build never reads it.
    /// Parsing is Core's, so an empty id never matches and a pattern holding whitespace or a
    /// misplaced <c>*</c> is dropped.
    /// </summary>
    internal static class PrizeOwnership
    {
        /// <summary>Core prize ids, shared with the WPF ownership gate.</summary>
        internal const string FlashDriftBounce = PrizeGrants.FlashDriftBounce;
        internal const string FlashPendulum = PrizeGrants.FlashPendulum;
        internal const string JackpotRemix = PrizeGrants.JackpotRemix;
        internal const string BubbleRain = PrizeGrants.BubbleRain;
        internal const string BubbleSpiralIn = PrizeGrants.BubbleSpiralIn;
        internal static bool OwnsFlashV2() => IsGranted(FlashDriftBounce) || IsGranted(FlashPendulum);

        /// <summary>The DEBUG override spec, read once; always null in Release.</summary>
        internal static readonly string? OverrideSpec = OwnershipService.ReadDebugOverride();

        /// <summary>Test seam: the owned-check every caller uses.</summary>
        internal static Func<string, bool> IsGranted = id => Matches(OverrideSpec, id) || Held(id);

        // WPF PrizeGrants.GrantsChanged; snapshots can arrive on a worker thread.
        internal static event Action? Changed;

        private static readonly object Gate = new();
        private static string? _heldAccount;
        private static long _revision;
        private static HashSet<string> _grants = new(StringComparer.Ordinal);

        /// <summary>WPF OwnershipService.IsGranted over the snapshots counter replies carry (row
        /// feat-backroom-counter-buy). Per run only: no profile-sync feed yet (svc-prize-ownership).</summary>
        internal static bool Held(string id)
        {
            // WPF OwnershipService.cs:60,77-82,155-157: cached grants follow the signed-in
            // account, not the availability of a network identity while Offline mode is on.
            lock (Gate) return _heldAccount != null
                && string.Equals(CoreAccount.UnifiedUserId, _heldAccount, StringComparison.Ordinal)
                && _grants.Contains(id);
        }

        /// <summary>WPF OwnershipService.ApplySnapshot: another account's snapshot and a lower
        /// revision for the held account are ignored; blank ids skipped, ids trimmed.</summary>
        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
        {
            if (string.IsNullOrEmpty(accountId) || grants is null) return;
            bool changed;
            lock (Gate)
            {
                // A response already in flight can arrive offline; reject it only if the account changed.
                if (!string.Equals(CoreAccount.UnifiedUserId, accountId, StringComparison.Ordinal)) return;
                if (_heldAccount == accountId && revision < _revision) return;
                var next = new HashSet<string>(grants.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()), StringComparer.Ordinal);
                changed = _heldAccount != accountId || !_grants.SetEquals(next);
                (_heldAccount, _revision, _grants) = (accountId, revision, next);
            }
            if (changed)
            {
                try { Changed?.Invoke(); }
                catch (Exception ex) { Serilog.Log.Debug("Prize ownership repaint: {E}", ex.Message); }
            }
        }

        /// <summary>Test cleanup (P02): forget every held snapshot.</summary>
        internal static void ResetHeld() { lock (Gate) (_heldAccount, _revision, _grants) = (null, 0, new(StringComparer.Ordinal)); }

        /// <summary>WPF V2Badges.FlashOwned: any flashes v2 prize (both motions or Jackpot Remix).</summary>
        internal static bool FlashV2Owned() =>
            IsGranted(FlashDriftBounce) || IsGranted(FlashPendulum) || IsGranted(JackpotRemix);

        /// <summary>WPF V2Badges.BubbleOwned: either ambient bubble motion.</summary>
        internal static bool BubbleV2Owned() => IsGranted(BubbleRain) || IsGranted(BubbleSpiralIn);

        /// <summary>Core OwnershipService's override rules over one comma list.</summary>
        internal static bool Matches(string? spec, string id) =>
            !string.IsNullOrEmpty(id) && OwnershipService.MatchesOverride(OwnershipService.ParseOverride(spec), id);
    }
}
