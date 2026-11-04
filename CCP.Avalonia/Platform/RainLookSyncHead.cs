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
    private static string? _pulledFor, _deferredFor;
    private static readonly object Gate = new();
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
            Volatile.Write(ref _deferredFor, s.UnifiedId);
            var needsGrants = false;
            RainLookCodec.Normalize(look, (slot, id) =>
            {
                needsGrants |= RainItemManifest.Shipped.Find(slot, id)?.Access == RainItemAccessKind.Shop;
                return false;
            });
            if (needsGrants && !PrizeOwnership.HasSnapshot(s.UnifiedId))
            {
                lock (Gate) _pulledFor = null;
                if (PrizeOwnership.HasSnapshot(s.UnifiedId)) AccountReady(s.UnifiedId, deferred: true);
                return;
            }
            Volatile.Write(ref _deferredFor, null);
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
        public IReadOnlyCollection<string> UnlockedAchievements => App.Achievements?.Progress?.UnlockedAchievements?.ToArray() ?? Array.Empty<string>();
    }

    // Queue after ownership snapshots, as WPF PostPrizeWork does.
    internal static void AccountReady(string? account, bool deferred = false)
    {
        var token = Volatile.Read(ref _run).Token;
        if (!deferred || (account != null && Volatile.Read(ref _deferredFor) == account)) Queue(account, token);
    }
    private static void Queue(string? account, CancellationToken token) => Dispatcher.UIThread.Post(() => _ = PullAsync(account, token));

    internal static Task PullAsync(string? account, CancellationToken token)
    {
        lock (Gate)
        {
            if (token.IsCancellationRequested || (token.CanBeCanceled && token != _run.Token) || string.IsNullOrEmpty(account)
                || account == _pulledFor || BackRoomApi.AppIdentity()?.UnifiedId != account) return Task.CompletedTask;
            _pulledFor = account;
            return Client.SyncAsync(token);
        }
    }

    internal static void ApplySyncPrizes(string account, PrizesBlock? prizes) => ApplyPrizes(account, account, prizes, "V2 sync", queueReady: false);

    internal static void ApplyPrizes(string? requested, string? answered, PrizesBlock? prizes, string source, bool queueReady = true)
    {
        if (string.IsNullOrEmpty(requested) || requested != answered) return;
        var session = CoreAccount.SessionVersion;
        var token = Volatile.Read(ref _run).Token;
        Dispatcher.UIThread.Post(() => CoreAccount.ForSession(session, () =>
        {
            if (token.IsCancellationRequested) return;
            if (prizes?.Grants is { } grants) PrizeOwnership.ApplySnapshot(requested, prizes.Revision, grants);
            if (queueReady) Queue(requested, token);
        }));
    }

    internal static void Stop()
    {
        CancellationTokenSource previous;
        lock (Gate)
        {
            Volatile.Write(ref _deferredFor, null);
            Client = Client.Fork();
            previous = Interlocked.Exchange(ref _run, new CancellationTokenSource());
            _pulledFor = null;
        }
        previous.Cancel();
        // A background reader may still be between reading the old source and its Token.
        // Do not dispose it; the old run releases its registrations as it finishes.
    }
}
