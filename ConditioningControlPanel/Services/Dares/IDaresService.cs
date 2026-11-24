using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// THE SEAM between the Dares UI and everything else (CONTRACT.md). <c>App.Dares</c> holds the one
/// live instance: the real service, or the local preview under DEBUG <c>CCP_DARES_PREVIEW=1</c>.
/// Nothing here throws; a fault reads as TryLater. <see cref="Changed"/> fires on the UI thread.
/// </summary>
public interface IDaresService
{
    event Action? Changed;

    DaresSnapshot Snapshot { get; }

    /// <summary>True for the local preview backend (no network; a desk "settle now" exists).</summary>
    bool IsPreview { get; }

    Task RefreshAsync(CancellationToken ct = default);

    Task<DareCreateOutcome> CreateAsync(string friendId, DareSlip slip, CancellationToken ct = default);

    /// <summary>One press on a bet card. StartLockdown is called only after the UI's consent prompts.</summary>
    Task<DareActResult> ActAsync(string betId, DareOp op, CancellationToken ct = default);

    /// <summary>Preview only: end a running bet now (deadline reached). No-op on the real service.</summary>
    Task SettleNowAsync(string betId, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>The camera check right now (a camera bet counting): watching, held off by the player's
    /// own stop, or unavailable (no consent or no device). Idle otherwise.</summary>
    DareCameraState Camera => DareCameraState.Idle;

    /// <summary>The player's Sparkles, for greying a stake they cannot cover. Null = unknown: the UI
    /// then offers everything and the server answers <c>short</c>.</summary>
    int? Sparkles => null;
}
