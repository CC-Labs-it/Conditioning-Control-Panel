using System;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// Every Scrolller copy this machine holds, swept in one place (licence 3.4 + owner, 2026-10-21):
/// no copy outlives 24 h from its download, and the off switch (GET /config/scrolller,
/// enabled=false) DELETES them rather than hiding them. The device cache's timer
/// (<see cref="ScrolllerDiskCache.StartSweeper"/>) calls <see cref="SweepAll"/> after the launch
/// load and then hourly; <see cref="ScrolllerPolicyService.Accept"/> calls <see cref="PurgeAll"/>
/// on every disabled answer. Each step is guarded on its own: one failing never skips the rest.
/// </summary>
internal static class ScrolllerCopySweeper
{
    /// <summary>The hourly pass. Never throws.</summary>
    public static void SweepAll()
    {
        Step(() => ScrolllerDiskCache.SweepExpired());
        Step(() => RemoteMediaCache.DropExpiredMemory());
    }

    /// <summary>The off switch: every copy goes now. Idempotent, never throws.</summary>
    public static void PurgeAll(string reason)
    {
        Step(() => ScrolllerDiskCache.PurgeAll(reason));
        Step(RemoteMediaCache.ClearMemory);
    }

    private static void Step(Action step)
    {
        try { step(); }
        catch (Exception ex) { App.Logger?.Debug("[scrolller sweep] step failed: {E}", ex.Message); }
    }
}
