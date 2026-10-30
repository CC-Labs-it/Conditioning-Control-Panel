using System.Threading.Tasks;
using ConditioningControlPanel.Services.Sparkles;
namespace ConditioningControlPanel.Avalonia.Views.Games;
internal sealed partial class GameWindow
{
    internal Task SparklesPending { get; private set; } = Task.CompletedTask;
    private Task DeliverSparkles(Task<SparklesClaimOutcome?> claim) =>
        Platform.SparklesHead.Deliver(claim, msg => Post(msg), () => _closing);
}
