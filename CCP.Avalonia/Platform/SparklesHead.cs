using ConditioningControlPanel.Services.Sparkles;
using System;
using System.Net.Http;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GameRewards;
using ConditioningControlPanel.Services.Prizes;
namespace ConditioningControlPanel.Avalonia.Platform;
internal static class SparklesHead
{
    internal static Func<SparklesClaimService?> MakeService = () => Create(
        Environment.GetEnvironmentVariable("CCP_USERDATA_DIR"), Environment.GetEnvironmentVariable(FriendsHead.EnvVar));
    internal static SparklesClaimService? Service => MakeService();
    internal static SparklesClaimService? Create(string? userDataDir, string? overrideUrl, HttpClient? http = null)
    {
        if (FriendsHead.BaseUrl(userDataDir, overrideUrl) is not { } url) return null;
        return new(http, FriendsHead.Identity, url, adoptSp: (account, sp) => Dispatcher.UIThread.Post(() =>
    {
        var s = CoreSettings.Current;
        if (!V2WalletAdoption.Decide(StakeHead.Account() == account, false, sp, s.SkillPoints, out var next)) return;
        var before = s.SkillPoints;
        s.SkillPoints = next;
        CoreSettings.Save();
        SparklePointRewards.PublishCredit(before, next, SparklePointSource.Place);
    }));
    }
}
