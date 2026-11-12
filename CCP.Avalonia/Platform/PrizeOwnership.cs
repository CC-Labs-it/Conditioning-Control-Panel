using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;
using Serilog;
namespace ConditioningControlPanel.Avalonia.Platform;
internal static class PrizeOwnership
{
    internal const string FlashDriftBounce = PrizeGrants.FlashDriftBounce;
    internal const string FlashPendulum = PrizeGrants.FlashPendulum;
    internal const string JackpotRemix = PrizeGrants.JackpotRemix;
    internal const string BubbleRain = PrizeGrants.BubbleRain;
    internal const string BubbleSpiralIn = PrizeGrants.BubbleSpiralIn;
    internal static bool OwnsFlashV2() => IsGranted(FlashDriftBounce) || IsGranted(FlashPendulum);
    internal static bool FlashV2Owned() => IsGranted(FlashDriftBounce) || IsGranted(FlashPendulum) || IsGranted(JackpotRemix);
    internal static bool BubbleV2Owned() => IsGranted(BubbleRain) || IsGranted(BubbleSpiralIn);
    internal static readonly string? OverrideSpec = OwnershipService.ReadDebugOverride();
    private static readonly OwnershipService Service = new(() => CoreAccount.UnifiedUserId, OverrideSpec, Raise);
    internal static readonly PrizesFeed Feed = new(() => Service, () => CoreAccount.UnifiedUserId, Post);
    internal static Func<string, bool> IsGranted = PrizeGrants.IsGranted;
    /// <summary>WPF PrizeGrants.GrantsChanged under this head's older name.</summary>
    internal static event Action? Changed { add => PrizeGrants.GrantsChanged += value; remove => PrizeGrants.GrantsChanged -= value; }
    static PrizeOwnership()
    {
        PrizeGrants.Attach(Service);
        PrizeGrants.GrantsChanged += () => GrantsChanged?.Invoke();
    }
    internal static void Seed()
    {
        ProviderSubscription.PrizesSink = SinkFor(Feed);
        BackRoomApi.DefaultApplyPrizes = ApplySnapshot;
    }
    // Run after Feed.Apply: an absent snapshot must not clamp a shop look against unknown ownership.
    internal static void SyncReceipt(string account) => SyncReceipt(account, Service);
    internal static void SyncReceipt(string account, OwnershipService ownership) => Post(() =>
    {
        if (string.Equals(CoreAccount.UnifiedUserId, account, StringComparison.Ordinal)
            && ownership.Revision > 0 && ownership.HoldsCurrentAccount)   // KnownGrants() over this service
            RainLookSyncHead.OnAccountReady(account);
    });

    /// <summary>The validate sink over <paramref name="feed"/>. Tests pass their own feed so shared grants stay untouched.</summary>
    internal static Action<string?, string?, PrizesBlock?, string> SinkFor(PrizesFeed feed) =>
        (requested, answered, prizes, source) =>
        {
            feed.Apply(requested, answered, prizes, source);
            // WPF ProfileSyncService.ApplyValidatePrizes:585-587: queue after the snapshot, even when absent.
            if (!string.IsNullOrEmpty(requested) && string.Equals(requested, answered, StringComparison.Ordinal))
                Post(() => RainLookSyncHead.OnAccountReady(requested));
        };

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
    /// <summary>The held grant ids, for the Racing Thoughts garage's kart rows (WPF GlassroadHostService init.grants).</summary>
    internal static string[] Grants => Service.Grants.ToArray();
    internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
    {
        Service.ApplySnapshot(accountId, revision, grants);
        GrantsChanged?.Invoke();
    }
    internal static void ResetHeld() => Service.Clear();
    internal static bool Matches(string? spec, string id) =>
        !string.IsNullOrEmpty(id) && OwnershipService.MatchesOverride(OwnershipService.ParseOverride(spec), id);

    /// <summary>A direct snapshot (counter, chess wardrobe) or any change of the held set: the chess page and
    /// the desk re-read their grants. An unchanged direct snapshot still raises it, so a first empty answer
    /// turns "unknown" into "known none".</summary>
    internal static event Action? GrantsChanged;
    /// <summary>The desk's name for <see cref="GrantsChanged"/>.</summary>
    internal static event Action? DeskGrantsChanged { add => GrantsChanged += value; remove => GrantsChanged -= value; }
    /// <summary>Null until the signed-in account has a revisioned snapshot: unknown grants keep stored picks.</summary>
    internal static ISet<string>? KnownGrants() => Service.Revision > 0 && Service.HoldsCurrentAccount
        ? new HashSet<string>(Service.Grants, StringComparer.Ordinal) : null;
    /// <summary>Desk items the signed-in account holds, or null before any snapshot for it.</summary>
    internal static IEnumerable<string>? KnownDeskGrants() => KnownGrants() is null ? null
        : ConditioningControlPanel.Services.EmiDesk.DeskEquipment.Items.Where(i => IsGranted(i.Grant)).Select(i => i.Grant).ToArray();
}
