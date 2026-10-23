using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF App.Ownership and ProfileSyncService.PrizeFeed over the shared engine. Grants
/// live only in memory, bound to CoreAccount, and are fetched again by validate/sync each run.</summary>
internal static class PrizeOwnership
{
    internal const string FlashDriftBounce = PrizeGrants.FlashDriftBounce;
    internal static readonly string? OverrideSpec = OwnershipService.ReadDebugOverride();
    private static readonly OwnershipService Service = new(() => CoreAccount.UnifiedUserId, OverrideSpec, Raise);
    internal static readonly PrizesFeed Feed = new(() => Service, () => CoreAccount.UnifiedUserId, Post);

    // Keep the existing owned-check test seam; production uses the same facade as WPF.
    internal static Func<string, bool> IsGranted = PrizeGrants.IsGranted;

    static PrizeOwnership() => PrizeGrants.Attach(Service);

    /// <summary>WPF App's provider-validate and counter sinks, installed before provider initialization.</summary>
    internal static void Seed()
    {
        ProviderSubscription.PrizesSink = (requested, answered, prizes, source) => Feed.Apply(requested, answered, prizes, source);
        BackRoomApi.DefaultApplyPrizes = ApplySnapshot;
    }

    // WPF OwnershipChanged runs inline on the UI thread, queued from other threads. Feed work
    // always queues, keeping a pool-thread snapshot and a UI-thread logout in FIFO order.
    private static volatile bool _shutdown;
    private static void Raise(Action work)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess()) { if (!_shutdown) work(); }
        else Post(work);
    }

    private static void Post(Action work)
    {
        void Safe()
        {
            if (_shutdown) return;
            try { work(); }
            catch (Exception ex) { Log.Debug("[Prizes] ownership update failed: {Error}", ex.Message); }
        }
        if (Application.Current == null) Safe();
        else Dispatcher.UIThread.Post(Safe);
    }

    internal static void WatchShutdown() => Dispatcher.UIThread.ShutdownStarted += (_, _) => _shutdown = true;
    internal static bool Held(string id) => Service.Grants.Contains(id);
    internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants) => Service.ApplySnapshot(accountId, revision, grants);
    internal static void ResetHeld() => Service.Clear();
    internal static bool Matches(string? spec, string id) =>
        !string.IsNullOrEmpty(id) && OwnershipService.MatchesOverride(OwnershipService.ParseOverride(spec), id);
}
