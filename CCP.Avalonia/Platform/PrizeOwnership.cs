using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room prize ownership on this head. WPF asks <c>PrizeGrants.IsGranted</c>, backed by
    /// OwnershipService snapshots from BackRoomApi. Snapshots from provider validation, profile load, sync and counter replies
    /// (<see cref="ApplySnapshot"/>, fed by <see cref="V2PurchaseHead"/>) are held in memory, as in WPF. The other source is WPF's DEBUG-only
    /// <c>CCP_PRIZE_GRANTS</c> override (Core OwnershipService.ReadDebugOverride): same variable,
    /// same patterns (exact id, <c>prefix.*</c>, <c>*</c>), and a Release build never reads it.
    /// Parsing is Core's, so an empty id never matches and a pattern holding whitespace or a
    /// misplaced <c>*</c> is dropped.
    /// </summary>
    internal static class PrizeOwnership
    {
        /// <summary>Core <see cref="PrizeGrants.FlashDriftBounce"/>.</summary>
        internal const string FlashDriftBounce = PrizeGrants.FlashDriftBounce;
        internal const string FlashPendulum = PrizeGrants.FlashPendulum;
        internal const string JackpotRemix = PrizeGrants.JackpotRemix;
        internal const string BubbleRain = PrizeGrants.BubbleRain;
        internal const string BubbleSpiralIn = PrizeGrants.BubbleSpiralIn;

        /// <summary>The DEBUG override spec, read once; always null in Release.</summary>
        internal static readonly string? OverrideSpec = OwnershipService.ReadDebugOverride();

        /// <summary>Test seam: the owned-check every caller uses.</summary>
        internal static Func<string, bool> IsGranted = id => Matches(OverrideSpec, id) || Held(id);

        // WPF GrantsChanged: listeners marshal to their UI thread.
        internal static event Action? Changed;

        internal static void Seed()
        {
            ConditioningControlPanel.Services.ProviderSubscription.PrizesSink = Apply;
            CoreAccount.UnifiedIdentityChanged -= OnIdentityChanged;
            CoreAccount.UnifiedIdentityChanged += OnIdentityChanged;
        }

        private static void OnIdentityChanged(object? sender, EventArgs e)
        {
            lock (Gate)
            {
                if (_heldAccount == null || _heldAccount == CoreAccount.UnifiedUserId) return;
                (_heldAccount, _revision, _grants) = (null, 0, new(StringComparer.Ordinal));
            }
            RaiseChanged();
        }

        internal static void Apply(string? requestedFor, string? answeredFor,
            ConditioningControlPanel.Models.PrizesBlock? prizes, string source)
        {
            if (string.IsNullOrEmpty(requestedFor) || requestedFor != answeredFor || prizes?.Grants == null) return;
            ApplySnapshot(requestedFor, prizes.Revision, prizes.Grants);
        }

        private static readonly object Gate = new();
        private static string? _heldAccount;
        private static long _revision;
        private static HashSet<string> _grants = new(StringComparer.Ordinal);

        /// <summary>WPF OwnershipService.IsGranted over the snapshots counter replies carry (row
        /// feat-backroom-counter-buy). In memory only, refreshed by the server feeds.</summary>
        internal static bool Held(string id)
        {
            var current = FriendsHead.Identity()?.UnifiedId;
            lock (Gate) return current != null && current == _heldAccount && _grants.Contains(id);
        }

        /// <summary>WPF OwnershipService.ApplySnapshot: another account's snapshot and a lower
        /// revision for the held account are ignored; blank ids skipped, ids trimmed.</summary>
        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
        {
            var current = FriendsHead.Identity()?.UnifiedId;
            if (string.IsNullOrEmpty(accountId) || grants is null || current != accountId) return;
            lock (Gate)
            {
                if (_heldAccount == accountId && revision < _revision) return;
                var next = new HashSet<string>(grants.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()), StringComparer.Ordinal);
                if (_heldAccount == accountId && _revision == revision && _grants.SetEquals(next)) return;
                (_heldAccount, _revision) = (accountId, revision);
                _grants = next;
            }
            RaiseChanged();
        }

        private static void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug("[Prizes] listener failed: {Error}", ex.Message); }
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
