using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Sparkles.Claims;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class SparklesHead
{
    internal static SparklesClaimService? Service { get; set; }

    internal static SparklesClaimService? Create(string? userDataDir, string? overrideUrl, HttpClient? http = null)
    {
        if (FriendsHead.BaseUrl(userDataDir, overrideUrl) is not { } url) return null;
        return new SparklesClaimService(http, FriendsHead.Identity, url,
            (account, sp) => Dispatcher.UIThread.Post(() => AdoptSp(account, sp)));
    }

    // Recheck the account inside the queued write. A credit can only raise the wallet.
    internal static void AdoptSp(string account, int sp)
    {
        var s = CoreSettings.Current;
        var same = string.Equals(FriendsHead.Identity()?.UnifiedId, account, StringComparison.Ordinal);
        var before = s.SkillPoints;
        if (!V2WalletAdoption.Decide(same, fromBuy: false, sp, before, out var next)) return;
        s.SkillPoints = next;
        CoreSettings.Save();
        SparklePointRewards.PublishCredit(before, next, SparklePointSource.Place);
    }

    /// <summary>WPF SparklesSlip.Deliver: await a claim, then post its slip on the UI thread, unless the
    /// window closed meanwhile. Never throws; the chip shows the higher of server and local balance
    /// (display only: the wallet itself moves only through <see cref="AdoptSp"/>).</summary>
    internal static async Task Deliver(Task<SparklesClaimOutcome?> claim, Action<JObject> post, Func<bool> closed)
    {
        try
        {
            var outcome = await claim.ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (closed()) return;
                var msg = SparklesSlip.WithWallet(SparklesSlip.Build(outcome, GentleVisuals.On, Loc.Get), CoreSettings.Current.SkillPoints);
                if (msg != null) post(msg);
            });
        }
        catch (Exception ex) { Serilog.Log.Debug("Sparkles: slip failed: {E}", ex.Message); }
    }
}
