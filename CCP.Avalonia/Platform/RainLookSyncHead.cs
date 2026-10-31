using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF RainLookSync's app hooks. No wardrobe or V2 tube exists on this base, so no bake or toast.
internal static class RainLookSyncHead
{
    private static string? _pulledFor;
    private static CancellationTokenSource _run = new();
    internal static RainLookSync Client = new(null, new RainLookSync.Hooks
    {
        Identity = BackRoomApi.AppIdentity,
        Local = () => (CoreSettings.Current.RainLook, CoreSettings.Current.RainLookPushOwed,
            CoreSettings.Current.RainLookAgreedUtc),
        Agreed = (look, at) =>
        {
            var s = CoreSettings.Current;
            if (RainLookCodec.Normalize(s.RainLook) == RainLookCodec.Normalize(look)) s.RainLookPushOwed = false;
            s.RainLookAgreedUtc = at;
            CoreSettings.Save();
        },
        Adopt = (look, at) =>
        {
            var s = CoreSettings.Current;
            s.RainLook = RainLookClamp.Clamp(look, new RainItemAccess(RainItemManifest.Shipped, new Grants(), new Progress()));
            // No local V2 pack player on this head. Keep the embedded clips.
            s.RainPackHash = "";
            s.RainLookPushOwed = false;
            s.RainLookAgreedUtc = at;
            CoreSettings.Save();
        },
        Log = line => Serilog.Log.Information("RainLookSync: {Line}", line),
    });

    private sealed class Grants : IRainGrantSource
    {
        public bool IsGranted(string grant) => PrizeOwnership.IsGranted(grant);
    }
    private sealed class Progress : IRainProgressSource
    {
        public int Level => Math.Max(CoreSettings.Current.PlayerLevel, CoreSettings.Current.HighestLevelEver);
        public IReadOnlyCollection<string> UnlockedAchievements =>
            App.Achievements?.Progress?.UnlockedAchievements?.ToArray() ?? Array.Empty<string>();
    }

    // Queue after ownership snapshots, as WPF PostPrizeWork does.
    internal static void AccountReady(string? account)
    {
        var token = _run.Token;
        Dispatcher.UIThread.Post(() => _ = PullAsync(account, token));
    }

    internal static async Task PullAsync(string? account, CancellationToken token)
    {
        if (token.IsCancellationRequested || string.IsNullOrEmpty(account) || account == _pulledFor
            || BackRoomApi.AppIdentity()?.UnifiedId != account) return;
        _pulledFor = account;
        await Client.SyncAsync(token);
    }

    internal static void ApplyPrizes(string? requested, string? answered, PrizesBlock? prizes, string source)
    {
        if (prizes?.Grants == null || string.IsNullOrEmpty(requested) || requested != answered) return;
        var token = _run.Token;
        Dispatcher.UIThread.Post(() =>
        {
            if (token.IsCancellationRequested) return;
            if (prizes?.Grants is { } grants) PrizeOwnership.ApplySnapshot(requested, prizes.Revision, grants);
            AccountReady(requested);
        });
    }

    internal static void Stop()
    {
        _run.Cancel();
        _run.Dispose();
        _run = new CancellationTokenSource();
        _pulledFor = null;
    }
}
