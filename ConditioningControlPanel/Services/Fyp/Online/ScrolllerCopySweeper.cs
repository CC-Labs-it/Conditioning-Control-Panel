using System;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// Every Scrolller copy this machine holds, swept in one place (licence 3.4 + owner, 2026-10-21):
/// no copy outlives 24 h from its download, and the off switch (GET /config/scrolller,
/// enabled=false) DELETES them rather than hiding them. The device cache's timer
/// (<see cref="ScrolllerDiskCache.StartSweeper"/>) calls <see cref="SweepAll"/> after the launch
/// load and then hourly; <see cref="ScrolllerPolicyService.Accept"/> calls <see cref="PurgeAll"/>
/// on every disabled answer. Each step is guarded on its own: one failing never skips the rest.
///
/// The copies: the device cache, the in-memory bytes, the <c>ccp_temp_remote_</c> files (players,
/// wallpaper, games; born at their download time), DtRH's local copies, and a remote wallpaper
/// Windows keeps its own copy of.
/// </summary>
internal static class ScrolllerCopySweeper
{
    /// <summary>The hourly pass. Never throws.</summary>
    public static void SweepAll()
    {
        Step(() => ScrolllerDiskCache.SweepExpired());
        Step(() => RemoteMediaCache.DropExpiredMemory());
        Step(() => OnUi(() => App.Wallpaper?.EnforceRemoteMaxAge()));
        Step(() => Chaos.DtrhAssetManifest.SweepLocalCopies());
        Step(() => RemoteMediaCache.SweepTempFilesOlderThan(ScrolllerDiskCache.MaxAge - ScrolllerDiskCache.SweepPeriod));
    }

    /// <summary>The off switch: every copy goes now. Idempotent, never throws.</summary>
    public static void PurgeAll(string reason)
    {
        Step(() => ScrolllerDiskCache.PurgeAll(reason));
        Step(RemoteMediaCache.ClearMemory);
        Step(() => OnUi(() => App.Wallpaper?.EnforceRemoteMaxAge(all: true)));
        Step(() => Chaos.DtrhAssetManifest.SweepLocalCopies(all: true));
        Step(() => RemoteMediaCache.SweepTempFilesOlderThan(TimeSpan.Zero));
    }

    /// <summary>Runs on the UI thread (queued, never waited on); nothing when there is none.</summary>
    private static void OnUi(Action work)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d == null || d.HasShutdownStarted) return;
        d.BeginInvoke(new Action(() => Step(work)));
    }

    private static void Step(Action step)
    {
        try { step(); }
        catch (Exception ex) { App.Logger?.Debug("[scrolller sweep] step failed: {E}", ex.Message); }
    }
}
