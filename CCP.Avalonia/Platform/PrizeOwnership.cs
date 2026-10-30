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
    static PrizeOwnership() => PrizeGrants.Attach(Service);
    internal static void Seed()
    {
        ProviderSubscription.PrizesSink = (requested, answered, prizes, source) => Feed.Apply(requested, answered, prizes, source);
        BackRoomApi.DefaultApplyPrizes = ApplySnapshot;
    }
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
