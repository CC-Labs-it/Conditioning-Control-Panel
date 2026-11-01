using System;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;
namespace ConditioningControlPanel.Services.Sparkles;
internal static class SparklesApp
{
    public static SparklesClaimService Create() => new(adoptSp: (account, sp) =>
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
