// WPF: ConditioningControlPanel/Services/Possession/Effects/CrackEffect.cs:13-36.
using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class CrackEffect : PossessionEffectBase
{
    private readonly Action<double, int> _shake;
    internal CrackEffect(Action<double, int>? shake = null) =>
        _shake = shake ?? Views.Deeper.ScreenShake.Shake;
    public override string Id => "crack";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(1);
    protected override bool ChargeOnApply => false;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        ctx.Attribution.EdgePulse(0.4);
        if (!ctx.Photosafe) _shake(0.25, 180);
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration) => Task.CompletedTask;
}
