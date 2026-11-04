using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room ownership from counter, profile sync and provider validation replies.
    /// The DEBUG-only override uses Core OwnershipService's matching rules.
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

        private static readonly object Gate = new();
        private static string? _heldAccount;
        private static long _revision;
        private static HashSet<string> _grants = new(StringComparer.Ordinal);

        /// <summary>WPF OwnershipService.IsGranted: snapshots are held for the current account only.</summary>
        internal static bool Held(string id)
        {
            var s = CoreSettings.Current;
            lock (Gate) return s.UnifiedId != null && s.UnifiedId == CoreAccount.UnifiedUserId
                && s.UnifiedId == _heldAccount && _grants.Contains(id);
        }

        /// <summary>WPF OwnershipService.ApplySnapshot: another account's snapshot and a lower
        /// revision for the held account are ignored; blank ids skipped, ids trimmed.</summary>
        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
        {
            if (string.IsNullOrEmpty(accountId) || grants is null) return;
            bool changed;
            lock (Gate)
            {
                var s = CoreSettings.Current;
                if (s.UnifiedId != accountId || CoreAccount.UnifiedUserId != accountId) return;
                if (_heldAccount == accountId && revision < _revision) return;
                var next = new HashSet<string>(grants.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()), StringComparer.Ordinal);
                changed = _heldAccount == accountId ? !_grants.SetEquals(next) : next.Count != 0;
                (_heldAccount, _revision, _grants) = (accountId, revision, next);
            }
            if (changed) NotifyChanged();
        }

        // WPF ProfileSyncService.cs:670-687: missing and mismatched blocks do not change ownership.
        internal static void ApplyProfilePrizes(string? requested, string? answered, Models.PrizesBlock? prizes, string source)
        {
            if (!string.IsNullOrEmpty(requested) && requested == answered && requested == CoreAccount.UnifiedUserId
                && prizes?.Grants != null)
                ApplySnapshot(requested, prizes.Revision, prizes.Grants);
        }

        // WPF ProfileSyncService.cs:565-570,693-698: clear even when Home is detached.
        internal static void OnIdentityChanged(object? sender, EventArgs e)
        {
            bool changed;
            lock (Gate)
            {
                changed = _grants.Count != 0;
                (_heldAccount, _revision, _grants) = (null, 0, new(StringComparer.Ordinal));
            }
            if (changed) NotifyChanged();
        }

        private static void NotifyChanged()
        {
            if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) PrizeGrants.RaiseGrantsChanged();
            else global::Avalonia.Threading.Dispatcher.UIThread.Post(PrizeGrants.RaiseGrantsChanged);
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
