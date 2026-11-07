using System;
using System.Net.Http;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
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
}
