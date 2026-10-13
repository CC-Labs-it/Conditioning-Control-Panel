using System;
using System.Threading;
using System.Threading.Tasks;

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
