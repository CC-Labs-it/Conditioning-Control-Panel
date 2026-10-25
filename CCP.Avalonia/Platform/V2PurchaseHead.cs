using System;
using System.Net.Http;
using Avalonia.Threading;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>The Core <see cref="V2PurchaseService"/> on this head (WPF App.xaml.cs V2Purchase). Sandbox
/// rule as <see cref="FriendsHead"/>: with CCP_USERDATA_DIR set only a loopback <see cref="EnvVar"/> is
/// called; with none the service is not built and the Get it row reads "not available".</summary>
internal static class V2PurchaseHead
{
    /// <summary>WPF BackRoomApi.BaseUrl's DEBUG override, same variable.</summary>
    internal const string EnvVar = "CCP_BACKROOM_BASE_URL";

    /// <summary>WPF App.V2Purchase. Null until startup built it.</summary>
    internal static V2PurchaseService? Service { get; set; }

    internal static V2PurchaseService? Create(string? userDataDir, string? overrideUrl, HttpClient? http = null)
    {
        if (FriendsHead.BaseUrl(userDataDir, overrideUrl) is not { } url) return null;
        // adoptSp is null on the relay for WPF's reason: a state read must never lower the wallet.
        // ponytail: no Contract D hook on this head (WPF MergedAccountRecovery is head-only), as FriendsHead.
        return new V2PurchaseService(
            new BackRoomApi(http, FriendsHead.Identity, adoptSp: null, PrizeOwnership.ApplySnapshot, url),
            () => FriendsHead.Identity()?.UnifiedId,
            id => V2PurchaseRule.OwnsPrize(id, PrizeOwnership.IsGranted),
            () => CoreSettings.Current.SkillPoints,
            (account, fromBuy, sp) => Dispatcher.UIThread.Post(() => AdoptSp(account, fromBuy, sp)));
    }

    /// <summary>WPF's marshalled write: the account is checked AGAIN, the player may have changed.</summary>
    internal static void AdoptSp(string account, bool fromBuy, int sp)
    {
        var s = CoreSettings.Current;
        var same = string.Equals(FriendsHead.Identity()?.UnifiedId, account, StringComparison.Ordinal);
        if (!V2WalletAdoption.Decide(same, fromBuy, sp, s.SkillPoints, out var next)) return;
        s.SkillPoints = next;
        CoreSettings.Save();
    }
}
