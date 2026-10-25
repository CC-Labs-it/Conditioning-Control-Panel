// PORTED from ConditioningControlPanel/Services/Stakes/StakeAppHooks.cs: the head's half of the stakes
// (rules, wire and settlement are Core Services/Stakes), seeded at App startup.

using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Stakes;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF StakeAppHooks: the Chaster gate, the loss-row switch and the settlement's books.</summary>
internal static class StakeHead
{
    internal static void Seed()
    {
        StakeBridge.TimeOkHook = TimeOk;
        StakeBridge.EnableLossRowHook = EnableLossRow;
        StakeSettlement.CreateShared = CreateSettlement;
    }

    /// <summary>WPF AppTimeOk: time is on offer while the Chaster tab could book a loss right now.</summary>
    internal static bool TimeOk()
    {
        var chaster = ChasterHead.Service;
        if (chaster == null) return false;
        var s = CoreSettings.Current;
        return StakeRules.TimeAllowed(chaster.IsLinked, s.ChasterTabEnabled, s.ChasterPaused, chaster.SafetyHoldRemaining);
    }

    /// <summary>WPF AppEnableLossRow: switch <see cref="StakeRules.LossRowId"/> on, on the UI thread, a new list
    /// every time (ChasterTabView.Keys does the same).</summary>
    internal static void EnableLossRow()
    {
        static void Apply()
        {
            var s = CoreSettings.Current;
            var now = s.ChasterPrices ?? new List<string>();
            if (now.Contains(StakeRules.LossRowId)) return;
            s.ChasterPrices = new List<string>(now) { StakeRules.LossRowId };
            CoreSettings.Save();
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply(); else Dispatcher.UIThread.Invoke(Apply);
    }

    /// <summary>WPF: books on the Chaster tab with the literal row id.</summary>
    internal static int BookLoss(string row, int seconds) =>
        ChasterHead.Service?.NoteSeconds("pvp_loss", seconds).AppliedSeconds ?? 0;

    /// <summary>WPF: the account is checked AGAIN inside the marshalled write; the player may be somebody else by then.</summary>
    internal static void AdoptSp(string account, bool receipt, int sp) => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            var s = CoreSettings.Current;
            var same = string.Equals(Account(), account, StringComparison.Ordinal);
            if (!V2WalletAdoption.Decide(same, receipt, sp, s.SkillPoints, out var next)) return;
            s.SkillPoints = next;
            CoreSettings.Save();
        }
        catch (Exception ex) { Log.Debug("Stakes: wallet adopt failed: {E}", ex.Message); }
    });

    /// <summary>WPF BackRoomApi.AppIdentity()?.UnifiedId.</summary>
    internal static string? Account() => FriendsHead.Identity()?.UnifiedId;

    private static StakeSettlement CreateSettlement() => new(BookLoss, AdoptSp, Account);
}
