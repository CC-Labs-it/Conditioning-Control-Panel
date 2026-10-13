using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Services.Possession;

// The WPF half of PossessionContracts (CCP.Core/Services/Possession/PossessionContracts.cs holds the
// rungs, intensity, roles and bark triggers): host, target, attribution, context, effect and warden
// are declared against FrameworkElement / Window / Canvas, so they stay with this head.

/// <summary>A possessable control the host registered (via the poss:Possession.Role attached property,
/// or explicit registration). Key is stable across the lockdown (x:Name or a role+index fallback) and
/// is what per-target cooldowns hang off.</summary>
public sealed class PossessionTarget
{
    public required FrameworkElement Element { get; init; }
    public required PossessionRole Role { get; init; }
    public required string Key { get; init; }
    /// <summary>Friendly name the warden uses when it names a big effect ("the Stop button").</summary>
    public string DisplayName { get; init; } = "";
    public DateTime CooldownUntil { get; set; } = DateTime.MinValue;
    public bool IsLive { get; set; }   // currently possessed by some effect - never double-book
}

/// <summary>What the director needs from a window it haunts. MainWindow implements it in
/// MainWindow.Possession.cs (Phase 1); other rooms (dashboard wall, Settings palette, Lock Card) follow.</summary>
public interface IPossessionHost
{
    Window Window { get; }
    /// <summary>Transparent, non-hit-testable Canvas stretched over the whole window (last child of
    /// RootGrid, above everything). Ghosts, ember overlays and the cursor ring live here.</summary>
    Canvas GhostLayer { get; }
    /// <summary>Canvas strip along the bottom of the window where fallen pieces come to rest.</summary>
    Canvas RubbleFloor { get; }
    /// <summary>Live registry of possessable controls; refreshed by the director before each pick
    /// (controls in collapsed tabs are skipped - IsVisible false).</summary>
    IReadOnlyList<PossessionTarget> Targets { get; }
    /// <summary>Top-left of the element in GhostLayer coordinates.</summary>
    Point PointOf(FrameworkElement element);
    /// <summary>False while the window is minimized, not loaded, or covered by a playback takeover -
    /// the director never STARTS an effect then (live ones keep running).</summary>
    bool IsUsable { get; }
}

/// <summary>The attribution grammar ("clarity in front"). Every effect speaks it; nothing moves
/// without ChargeAsync first, nothing stays moved without Possess, and the cursor ring is on while
/// anything is possessed. Ember #FF8A5C is reserved for Possession - never reuse it for theme.</summary>
public interface IPossessionAttribution
{
    /// <summary>The charge: ~400ms ember ripple over the target's bounds BEFORE it moves. Awaits the
    /// ripple. Photosafe/reduced-motion: a 600ms static ember tint instead of a ripple.</summary>
    Task ChargeAsync(FrameworkElement target, CancellationToken ct, int durationMs = 400);
    /// <summary>The possessed outline: thin ember outline + faint tint while the ghost is live, plus the
    /// cursor ember ring (refcounted). Dispose releases both.</summary>
    IDisposable Possess(FrameworkElement target);
    /// <summary>Window-edge ember pulse used by tripwires and rung changes. Never a hard white/crimson
    /// blink when photosafe; <paramref name="strength"/> 0..1.</summary>
    void EdgePulse(double strength);
    /// <summary>True while at least one Possess handle is alive.</summary>
    bool AnyPossessed { get; }
}

/// <summary>Everything an effect may read. Built fresh by the director for each Apply.</summary>
public sealed class PossessionContext
{
    public required IPossessionHost Host { get; init; }
    public required IPossessionAttribution Attribution { get; init; }
    public required PossessionRung Rung { get; init; }
    public required PossessionIntensity Intensity { get; init; }
    public required bool Photosafe { get; init; }
    public required Random Rng { get; init; }
    public required double ElapsedFraction { get; init; }
    public required TimeSpan Remaining { get; init; }
    /// <summary>Ask the warden to name what just happened: (effectId, target DisplayName). The director
    /// routes it to barks (PossessionEffect trigger) - effects never talk to BarkService directly.</summary>
    public required Action<string, string?> Name { get; init; }
}

/// <summary>One haunt. Stateless until Apply; Undo MUST be safe to call when Apply never ran, was
/// cancelled half-way, or already undid itself. Undo restores the real control EXACTLY (transform,
/// opacity, hit-testing, text) - the reassembly exit is just UndoAsync over every live effect.</summary>
public interface IPossessionEffect
{
    string Id { get; }
    PossessionRung MinRung { get; }
    /// <summary>Gentle-intensity effects say Gentle; themed Doki dialogs say FullDoki.</summary>
    PossessionIntensity MinIntensity { get; }
    /// <summary>Big effects (moves, falls, dissolves, retitles, dialogs) are named by the warden;
    /// R0/R1 micro-tics stay silent but still carry charge + tint.</summary>
    bool IsBig { get; }
    /// <summary>Skipped entirely when Photosafe (hard blinks / strobing flicker).</summary>
    bool UsesFlicker { get; }
    /// <summary>Deck weight at the effect's MinRung; the director scales it up per rung above.</summary>
    double Weight { get; }
    /// <summary>How long the haunt stays before the director auto-undoes it. Zero = until exit.</summary>
    TimeSpan HoldFor { get; }
    /// <summary>Roles this effect can take a victim from (empty = no target needed, e.g. title effects).</summary>
    IReadOnlyList<PossessionRole> Roles { get; }
    bool IsLive { get; }
    bool CanApply(PossessionContext ctx, PossessionTarget? target);
    Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct);
    Task UndoAsync(TimeSpan duration);
}

/// <summary>The companion as warden. Implemented by Services/Possession/Warden.cs over the avatar
/// tube's glide API; the director calls it (knock at R3, stare on tripwire repeat 3+ / R4, leave at
/// R4, return on reassembly). Every method must be safe when no avatar window exists (no-op).</summary>
public interface IPossessionWarden
{
    /// <summary>False when the tube is absent, busy, hidden, or on cooldown - the director then skips the verb.</summary>
    bool IsAvailable { get; }
    /// <summary>Glide beside the target, a beat, then the caller drops the target (returns when the knock landed).</summary>
    Task KnockAsync(PossessionTarget target, CancellationToken ct);
    /// <summary>Glide to the window, say one line (reason = tripwire kind or "r4"), linger, go home.</summary>
    Task StareAsync(string reason, CancellationToken ct);
    /// <summary>R4: the tube empties / the companion leaves the frame.</summary>
    Task LeaveAsync(CancellationToken ct);
    /// <summary>Reassembly: the companion comes back to where it was. Must be safe to call twice.</summary>
    Task ReturnAsync(CancellationToken ct);
}


/// <summary>The one deck helper that needs a real effect, so it stays with the effect contract
/// (PossessionDeck itself moved to CCP.Core).</summary>
public static class PossessionDeckMeta
{
    /// <summary>Snapshot an effect into the deck's plain-data view.</summary>
    public static PossessionEffectMeta MetaOf(IPossessionEffect e) => new(
        e.Id, e.MinRung, e.MinIntensity, e.UsesFlicker, e.Weight,
        e.Roles ?? Array.Empty<PossessionRole>());
}
