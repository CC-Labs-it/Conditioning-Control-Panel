using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GameRewards;
using ConditioningControlPanel.Services.Prizes;
namespace ConditioningControlPanel.Avalonia.Platform;
internal static class SparklesHead
{
    internal static SparklesClaimService Service { get; } = new(adoptSp: (account, sp) => Dispatcher.UIThread.Post(() =>
    {
        var s = CoreSettings.Current;
        if (!V2WalletAdoption.Decide(StakeHead.Account() == account, false, sp, s.SkillPoints, out var next)) return;
        var before = s.SkillPoints;
        s.SkillPoints = next;
        CoreSettings.Save();
        SparklePointRewards.PublishCredit(before, next, SparklePointSource.Place);
    }));
}
