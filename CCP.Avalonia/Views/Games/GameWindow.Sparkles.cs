using ConditioningControlPanel.Services.Sparkles;
using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GameRewards;
using ConditioningControlPanel.Services.Safety;
namespace ConditioningControlPanel.Avalonia.Views.Games;
internal sealed partial class GameWindow
{
    internal Task SparklesPending { get; private set; } = Task.CompletedTask;
    private async Task DeliverSparkles(Task<SparklesClaimOutcome?> claim)
    {
        try
        {
            var outcome = await claim.ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_closing || SparklesSlip.Build(outcome, GentleVisuals.On, Loc.Get) is not { } msg) return;
                if (msg["sp"]?.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                    msg["sp"] = Math.Max((long)msg["sp"]!, CoreSettings.Current.SkillPoints);
                Post(msg);
            });
        }
        catch (Exception ex) { Serilog.Log.Debug("Sparkles: slip failed: {E}", ex.Message); }
    }
}
