using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Services.Stakes;

/// <summary>The WPF head's half of the stakes (the rules, wire and settlement live in CCP.Core):
/// the Chaster gate, the loss-row switch and the settlement's books, seeded at App startup.</summary>
internal static class StakeAppHooks
{
    public static void Seed()
    {
        StakeBridge.TimeOkHook = AppTimeOk;
        StakeBridge.EnableLossRowHook = AppEnableLossRow;
        StakeSettlement.CreateShared = CreateSettlement;
    }

    /// <summary>Time is on offer while the Chaster tab could book a loss right now.</summary>
    public static bool AppTimeOk()
    {
        var chaster = App.Chaster;
        var s = App.Settings?.Current;
        if (chaster == null || s == null) return false;
        return StakeRules.TimeAllowed(chaster.IsLinked, s.ChasterTabEnabled, s.ChasterPaused, chaster.SafetyHoldRemaining);
    }

    /// <summary>Switch <see cref="StakeRules.LossRowId"/> on, on the UI thread, the way the tab
    /// page does: a new list every time, never one being edited.</summary>
    public static void AppEnableLossRow()
    {
        void Apply()
        {
            if (App.Settings?.Current is not { } s) return;
            var now = s.ChasterPrices ?? new List<string>();
            if (now.Contains(StakeRules.LossRowId)) return;
            s.ChasterPrices = new List<string>(now) { StakeRules.LossRowId };
            App.Settings.Save();
        }
        var d = App.Current?.Dispatcher;
        if (d == null || d.CheckAccess()) Apply(); else d.Invoke(Apply);
    }

    private static StakeSettlement CreateSettlement() => new(
        // The row id is literal on purpose: the price-row wiring test reads it.
        (_, seconds) => App.Chaster?.NoteSeconds("pvp_loss", seconds).AppliedSeconds ?? 0,
        // The account is checked AGAIN inside the marshalled write (the canAdopt shape
        // BackRoomBridge.AdoptSp uses): the player may be somebody else by then.
        (account, receipt, sp) => App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            if (App.Settings?.Current is not { } s) return;
            var same = string.Equals(BackRoom.BackRoomApi.AppIdentity()?.UnifiedId, account, StringComparison.Ordinal);
            if (!V2WalletAdoption.Decide(same, receipt, sp, s.SkillPoints, out var next)) return;
            s.SkillPoints = next;
            App.Settings.Save();
        })),
        () => BackRoom.BackRoomApi.AppIdentity()?.UnifiedId);
}
