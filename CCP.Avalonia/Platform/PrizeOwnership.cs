using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform
{
    // WPF ProfileSyncService.ApplyValidatePrizes and V2 sync feed, plus counter snapshots.
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

        internal static void ApplyAccountPrizes(string? requested, string? answered,
            ConditioningControlPanel.Models.PrizesBlock? prizes, string source)
        {
            if (!string.IsNullOrEmpty(requested) && requested == answered && prizes?.Grants != null)
                ApplySnapshot(requested, prizes.Revision, prizes.Grants);
        }
        internal static event Action? DeskGrantsChanged;
        internal static IEnumerable<string>? KnownDeskGrants()
        {
            lock (Gate)
                return FriendsHead.Identity()?.UnifiedId == _heldAccount && _revision > 0
                    ? ConditioningControlPanel.Services.EmiDesk.DeskEquipment.Items.Where(i => IsGranted(i.Grant)).Select(i => i.Grant).ToArray() : null;
        }
        private static readonly object Gate = new();
        private static string? _heldAccount;
        private static long _revision;
        private static HashSet<string> _grants = new(StringComparer.Ordinal);

        // WPF OwnershipService.IsGranted: held grants are bound to the signed-in account.
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
                (_heldAccount, _revision) = (accountId, revision);
                _grants = new HashSet<string>(grants.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()), StringComparer.Ordinal);
            }
            DeskGrantsChanged?.Invoke();
        }

        /// <summary>Forget held grants on logout or test cleanup.</summary>
        internal static void ResetHeld() { lock (Gate) (_heldAccount, _revision, _grants) = (null, 0, new(StringComparer.Ordinal)); DeskGrantsChanged?.Invoke(); }

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
