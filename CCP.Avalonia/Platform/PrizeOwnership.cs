using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room prize ownership on this head. WPF asks <c>PrizeGrants.IsGranted</c>, backed by
    /// OwnershipService snapshots from sync, provider validation and BackRoomApi, held for this run.
    /// The other source is WPF's DEBUG-only
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

        private static readonly object Gate = new();
        private static Tuple<string?>? _heldIdentity;
        private static long _revision;
        private static HashSet<string> _grants = new(StringComparer.Ordinal);

        /// <summary>WPF OwnershipService.IsGranted over the current account's in-memory snapshot.</summary>
        internal static bool Held(string id)
        {
            lock (Gate) return ReferenceEquals(_heldIdentity, CoreAccount.UnifiedIdentity) && _grants.Contains(id);
        }

        /// <summary>WPF OwnershipService.ApplySnapshot: another account's snapshot and a lower
        /// revision for the held account are ignored; blank ids skipped, ids trimmed.</summary>
        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
        {
            if (string.IsNullOrEmpty(accountId) || grants is null) return;
            bool changed;
            lock (Gate)
            {
                var identity = CoreAccount.UnifiedIdentity;
                if (identity.Item1 != accountId) return;
                bool same = ReferenceEquals(_heldIdentity, identity);
                if (same && revision < _revision) return;
                var next = new HashSet<string>(grants.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()), StringComparer.Ordinal);
                changed = same ? !_grants.SetEquals(next) : _grants.Count != 0 || next.Count != 0;
                (_heldIdentity, _revision, _grants) = (identity, revision, next);
            }
            // WPF OwnershipService.cs:129-136: only a changed set raises the repaint event.
            if (changed) PrizeGrants.RaiseGrantsChanged();
        }

        // WPF ProfileSyncService.cs:570,670: clear on identity change; accept only matching replies.
        internal static void Seed()
        {
            CoreAccount.UnifiedIdentityChanged -= ClearOnIdentity;
            CoreAccount.UnifiedIdentityChanged += ClearOnIdentity;
            ProviderSubscription.PrizesSink = ApplyValidated;
        }
        private static void ClearOnIdentity(object? sender, EventArgs e) => ResetHeld(keepCurrent: true);
        private static void ApplyValidated(string? requested, string? answered, Models.PrizesBlock? prizes, string source)
        {
            if (requested != null && requested == answered && prizes?.Grants is { } grants)
                ApplySnapshot(requested, prizes.Revision, grants);
        }
        internal static void ResetHeld(bool keepCurrent = false)
        {
            bool changed;
            lock (Gate)
            {
                if (keepCurrent && ReferenceEquals(_heldIdentity, CoreAccount.UnifiedIdentity)) return;
                changed = _grants.Count != 0;
                (_heldIdentity, _revision, _grants) = (null, 0, new(StringComparer.Ordinal));
            }
            if (changed) PrizeGrants.RaiseGrantsChanged();
        }

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
