using System;
using System.Collections.Generic;
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

        // Reuse Core's account/revision rules and change detection for every snapshot source.
        private static readonly OwnershipService HeldOwnership = new(
            () => FriendsHead.Identity()?.UnifiedId, overrideSpec: null, raise: action => action());

        static PrizeOwnership() => HeldOwnership.OwnershipChanged += (_, _) => Changed?.Invoke();

        /// <summary>WPF GrantsChanged: snapshot grants changed, possibly off the UI thread.</summary>
        internal static event Action? Changed;

        internal static bool Held(string id) => HeldOwnership.IsGranted(id);

        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
            => HeldOwnership.ApplySnapshot(accountId, revision, grants);

        /// <summary>Test cleanup (P02): forget every held snapshot.</summary>
        internal static void ResetHeld() => HeldOwnership.Clear();

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
