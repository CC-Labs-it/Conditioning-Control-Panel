using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.PrizeCounter;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF PrizeCounterHostService.RefreshWalletInBackground.</summary>
internal static class SparkleWalletBootRead
{
    private static readonly object Gate = new();
    private static string? _account;
    private static DateTime _at;
    private static long _generation;

    internal static bool WalletReadDue(string? account, string? last, DateTime at, DateTime now) =>
        !string.IsNullOrEmpty(account) && (account != last || now - at >= TimeSpan.FromMinutes(10));

    internal static void Refresh()
    {
        try
        {
            var identity = FriendsHead.Identity();
            var url = GameWindow.CounterServerBase();
            if (identity == null || CoreSettings.Current.OfflineMode || url == null) return;
            var account = identity.Value.UnifiedId;
            long generation;
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                if (!WalletReadDue(account, _account, _at, now)) return;
                _account = account; _at = now; generation = ++_generation;
            }
            _ = Task.Run(async () =>
            {
                try
                {
                    var relay = new PrizeCounterRelay(GameWindow.CounterHttp(),
                        () => FriendsHead.Identity() is { } id && id.UnifiedId == account ? id : null,
                        () => CoreSettings.Current.OfflineMode,
                        (acc, _, sp) => Dispatcher.UIThread.Post(() => V2PurchaseHead.AdoptSp(acc, false, sp)),
                        PrizeOwnership.ApplySnapshot, url);
                    var reply = await relay.RelayAsync(PrizeCounterRelay.OpState, null, null).ConfigureAwait(false);
                    if (reply.Value<bool?>("ok") == true) return;
                }
                catch { }
                lock (Gate) { if (_generation == generation) _account = null; }
            });
        }
        catch { }
    }
}
