using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;
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
    internal static async Task Deliver(Task<SparklesClaimOutcome?> claim, Action<Newtonsoft.Json.Linq.JObject> post, Func<bool> closed)
    {
        try
        {
            var outcome = await claim.ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (closed() || SparklesSlip.Build(outcome, GentleVisuals.On, Loc.Get) is not { } msg) return;
                if (msg["sp"]?.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                    msg["sp"] = Math.Max((long)msg["sp"]!, CoreSettings.Current.SkillPoints);
                post(msg);
            });
        }
        catch (Exception ex) { Serilog.Log.Debug("Sparkles: slip failed: {E}", ex.Message); }
    }
}
