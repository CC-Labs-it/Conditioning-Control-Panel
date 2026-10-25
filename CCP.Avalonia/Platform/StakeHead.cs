using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Stakes;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF StakeAppHooks: live Chaster gate, opt-in loss row and account-checked wallet.</summary>
internal static class StakeHead
{
    internal static void Seed()
    {
        StakeBridge.TimeOkHook = TimeOk;
        StakeBridge.EnableLossRowHook = EnableLossRow;
        StakeSettlement.CreateShared = CreateSettlement;
    }

    internal static bool TimeOk()
    {
        var c = ChasterHead.Service;
        var s = CoreSettings.Current;
        return c != null && StakeRules.TimeAllowed(c.IsLinked, s.ChasterTabEnabled,
            s.ChasterPaused, c.SafetyHoldRemaining);
    }

    internal static void EnableLossRow()
    {
        void Apply()
        {
            var s = CoreSettings.Current;
            var now = s.ChasterPrices ?? new List<string>();
            if (now.Contains(StakeRules.LossRowId)) return;
            s.ChasterPrices = new List<string>(now) { StakeRules.LossRowId };
            CoreSettings.Save();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Invoke(Apply);
    }

    internal static StakeSettlement CreateSettlement() => new(
        (_, seconds) => ChasterHead.Service?.NoteSeconds("pvp_loss", seconds).AppliedSeconds ?? 0,
        // Same marshalled account recheck / snapshot-versus-receipt rule as the shop.
        (account, receipt, sp) => Dispatcher.UIThread.Post(() => V2PurchaseHead.AdoptSp(account, receipt, sp)),
        () => FriendsHead.Identity()?.UnifiedId);
}
