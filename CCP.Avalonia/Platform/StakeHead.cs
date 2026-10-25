using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Stakes;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF StakeAppHooks: the tab gate, consent switch and shared settlement.</summary>
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
        var chaster = ChasterHead.Service;
        var s = CoreSettings.Current;
        return chaster != null && StakeRules.TimeAllowed(chaster.IsLinked,
            s.ChasterTabEnabled, s.ChasterPaused, chaster.SafetyHoldRemaining);
    }

    internal static void EnableLossRow()
    {
        void Apply()
        {
            var s = CoreSettings.Current;
            var rows = s.ChasterPrices ?? new List<string>();
            if (rows.Contains(StakeRules.LossRowId)) return;
            s.ChasterPrices = new List<string>(rows) { StakeRules.LossRowId };
            CoreSettings.Save();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Invoke(Apply);
    }

    internal static StakeSettlement CreateSettlement() => new(
        (_, seconds) => ChasterHead.Service?.NoteSeconds("pvp_loss", seconds).AppliedSeconds ?? 0,
        (account, receipt, sp) => Dispatcher.UIThread.Post(() => V2PurchaseHead.AdoptSp(account, receipt, sp)),
        () => FriendsHead.Identity()?.UnifiedId);
}
