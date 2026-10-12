using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Possession.Effects;
using Serilog;

namespace ConditioningControlPanel.Services.Possession.Scenes;

/// <summary>One choreography. Instances are shared (the director keeps one of each), so all mutable
/// state belongs to the run, not the object.</summary>
public interface IPossessionScene
{
    string Id { get; }

    /// <summary>Scenes are a Melt-and-up device by design; below that the room is still pretending
    /// nothing is wrong, and a three-beat sequence gives that away.</summary>
    PossessionRung MinRung { get; }

    /// <summary>Run the choreography. <paramref name="pick"/> hands out victims of a role, already
    /// filtered for cooldown / already-possessed and BOOKED by the director - a scene never has to do
    /// the bookkeeping, and never gets the same control twice. It returns null when nothing suitable
    /// is free, which every scene must survive (a beat is skipped, not faked).</summary>
    Task RunAsync(PossessionContext ctx, IPossessionHost host, Func<PossessionRole, PossessionTarget?> pick, CancellationToken ct);

    /// <summary>Undo everything this run touched, restoring EXACTLY. Safe before RunAsync, twice, and
    /// after the window died. <paramref name="duration"/> Zero is the synchronous crash path.</summary>
    Task UndoAsync(TimeSpan duration);

    /// <summary>Roughly how many live ghosts this scene is worth while it runs. The director charges it
    /// against the concurrency cap so a scene plus a full deck cannot stack into a seizure.</summary>
    int Beats { get; }

    /// <summary>True while a run is in flight; the director never starts a second run of it.</summary>
    bool IsRunning { get; }
}

/// <summary>
/// A scene dressed as an <see cref="IPossessionEffect"/>.
///
/// <para>The director already owns everything a running scene needs: a cancellation token per haunt,
/// a live-ghost ledger, per-target cooldowns, the reassembly exit and a crash-safe synchronous UndoAll.
/// Teaching it a second lifecycle for scenes would have meant duplicating all of that (and forgetting
/// one of them). So a scene is handed to the same machinery through this adapter: Apply runs the
/// choreography, Undo unwinds it, and UndoAll cancels it mid-sentence exactly as it cancels an effect.</para>
///
/// <para>It declares no Roles - the scene picks its own victims through the director's booking
/// callback, because a scene needs several controls of several roles and the deck's one-effect-one-
/// victim pick cannot express that.</para>
/// </summary>
public sealed class PossessionSceneEffect : IPossessionEffect
{
    private readonly IPossessionScene _scene;
    private readonly Func<PossessionRole, PossessionTarget?> _pick;
    private bool _live;

    public PossessionSceneEffect(IPossessionScene scene, Func<PossessionRole, PossessionTarget?> pick)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _pick = pick ?? (_ => null);
    }

    public IPossessionScene Scene => _scene;

    public string Id => _scene.Id;
    public PossessionRung MinRung => _scene.MinRung;
    public PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    /// <summary>Always named: a scene is the loudest thing on the ladder short of a Doki dialog, and an
    /// unattributed choreography is exactly the "was that a bug?" the whole grammar exists to prevent.</summary>
    public bool IsBig => true;
    public bool UsesFlicker => false;
    public double Weight => 0;                 // never dealt from the deck; the director elects it
    /// <summary>The scene cleans up after itself; this is the short tail before the director takes the
    /// slot back, so the last beat is readable before the room reassembles.</summary>
    public TimeSpan HoldFor => TimeSpan.FromSeconds(1.5);
    public IReadOnlyList<PossessionRole> Roles => Array.Empty<PossessionRole>();
    public bool IsLive => _live;

    public bool CanApply(PossessionContext ctx, PossessionTarget? target)
        => !_live && ctx != null && ctx.Rung >= _scene.MinRung;

    public async Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (_live) return;
        _live = true;
        try { await _scene.RunAsync(ctx, ctx.Host, _pick, ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Warning("Possession scene {Id} threw: {Error}", Id, ex.Message); }
    }

    public async Task UndoAsync(TimeSpan duration)
    {
        try { await _scene.UndoAsync(duration).ConfigureAwait(true); }
        catch (Exception ex) { Log.Warning("Possession scene {Id} undo failed: {Error}", Id, ex.Message); }
        finally { _live = false; }
    }
}
