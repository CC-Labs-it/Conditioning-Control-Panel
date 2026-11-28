using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.PrizeCounter;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF PrizeCounterHostService:239-284. A state read can raise SP but cannot spend it.
internal sealed class PrizeWalletRead
{
    private readonly object _gate = new();
    private string? _account;
    private DateTime _at;
    private int _generation;

    internal static bool WalletReadDue(string? account, string? lastAccount, DateTime lastAt, DateTime now) =>
        !string.IsNullOrEmpty(account) && (!string.Equals(account, lastAccount, StringComparison.Ordinal)
            || now - lastAt >= TimeSpan.FromMinutes(10));

    internal async Task Refresh()
    {
        string? account = null;
        int generation = 0;
        try
        {
            var id = FriendsHead.Identity();
            var url = GameWindow.CounterServerBase();
            if (id == null || CoreSettings.Current.OfflineMode || url == null) return;
            account = id.Value.UnifiedId;
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if (!WalletReadDue(account, _account, _at, now)) return;
                _account = account; _at = now; generation = ++_generation;
            }
            var relay = new PrizeCounterRelay(GameWindow.CounterHttp(),
                () => FriendsHead.Identity() is { } current && current.UnifiedId == account ? current : null,
                () => CoreSettings.Current.OfflineMode,
                (acc, fromBuy, sp) => Dispatcher.UIThread.Post(() => V2PurchaseHead.AdoptSp(acc, fromBuy, sp)),
                PrizeOwnership.ApplySnapshot, url);
            var reply = await relay.RelayAsync(PrizeCounterRelay.OpState, null, null).ConfigureAwait(false);
            if (reply["ok"]?.Type != JTokenType.Boolean || !(bool)reply["ok"]!) Reset(generation);
        }
        catch (Exception ex)
        {
            if (generation != 0) Reset(generation);
            Log.Debug("PrizeCounter wallet read failed: {E}", ex.Message);
        }
    }

    private void Reset(int generation)
    {
        lock (_gate) { if (_generation == generation) _account = null; }
    }
}
