using System;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Services.Sparkles;

// Dispatcher half retained from WPF SparklesClaimService.CreateForApp.
internal static class SparklesApp
{
    /// <summary>The app's service: adopts on the UI thread and fires the wallet juice.</summary>
    public static SparklesClaimService CreateForApp() => new(adoptSp: (account, sp) =>
        App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            if (App.Settings?.Current is not { } s) return;
            // The account is checked AGAIN inside the marshalled write: the player may be
            // somebody else by the time the reply lands.
            var same = string.Equals(BackRoomApi.AppIdentity()?.UnifiedId, account, StringComparison.Ordinal);
            var before = s.SkillPoints;
            if (!V2WalletAdoption.Decide(same, fromBuy: false, sp, before, out var next)) return;
            s.SkillPoints = next;
            App.Settings.Save();
            SparklePointRewards.PublishCredit(before, next, SparklePointSource.Place);
        })));
}
