using System.Threading.Tasks;
using ConditioningControlPanel.Services.Sparkles.Claims;

namespace ConditioningControlPanel.Avalonia.Views.Games;

/// <summary>SPARKLES BY PLACE (WPF B1): every game host here claims through the one Core claim service
/// (<see cref="Platform.SparklesHead.Service"/>) and lands the results slip on its own page.</summary>
internal sealed partial class GameWindow
{
    /// <summary>The last claim's slip delivery (tests await it).</summary>
    internal Task SparklesPending { get; private set; } = Task.CompletedTask;

    private void DeliverSparkles(Task<SparklesClaimOutcome?> claim) =>
        SparklesPending = Platform.SparklesHead.Deliver(claim, msg => Post(msg), () => _closing);
}
