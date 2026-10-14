using System;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room prize ownership on this head. WPF asks <c>PrizeGrants.IsGranted</c>, backed by
    /// OwnershipService snapshots from WPF's BackRoomApi. OwnershipService and the relay contract are
    /// in Core; BackRoomApi is still WPF-only, so nothing feeds snapshots here yet (parity row
    /// svc-prize-ownership) and every account is unowned - exactly what WPF shows an account that
    /// bought nothing. The one exception is WPF's DEBUG-only <c>CCP_PRIZE_GRANTS</c> override
    /// (Core OwnershipService.ReadDebugOverride): same variable, same patterns (exact id,
    /// <c>prefix.*</c>, <c>*</c>), and a Release build never reads it. Parsing is Core's, so an
    /// empty id never matches and a pattern holding whitespace or a misplaced <c>*</c> is dropped.
    /// </summary>
    internal static class PrizeOwnership
    {
        /// <summary>Core <see cref="PrizeGrants.FlashDriftBounce"/>.</summary>
        internal const string FlashDriftBounce = PrizeGrants.FlashDriftBounce;

        /// <summary>The DEBUG override spec, read once; always null in Release.</summary>
        internal static readonly string? OverrideSpec = OwnershipService.ReadDebugOverride();

        /// <summary>Test seam: the owned-check every caller uses.</summary>
        internal static Func<string, bool> IsGranted = id => Matches(OverrideSpec, id);

        /// <summary>Core OwnershipService's override rules over one comma list.</summary>
        internal static bool Matches(string? spec, string id) =>
            !string.IsNullOrEmpty(id) && OwnershipService.MatchesOverride(OwnershipService.ParseOverride(spec), id);
    }
}
