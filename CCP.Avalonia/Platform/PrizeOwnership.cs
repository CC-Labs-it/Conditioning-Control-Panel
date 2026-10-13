using System;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room prize ownership on this head. WPF asks <c>PrizeGrants.IsGranted</c>, backed by
    /// OwnershipService snapshots from BackRoomApi; both are in Core now but nothing feeds them here yet (parity row
    /// svc-prize-ownership), so every account is unowned - exactly what WPF shows an account that
    /// bought nothing. The one exception is WPF's DEBUG-only <c>CCP_PRIZE_GRANTS</c> override
    /// (Core OwnershipService.ReadDebugOverride): same variable, same patterns (exact id,
    /// <c>prefix.*</c>, <c>*</c>), and a Release build never reads it.
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
