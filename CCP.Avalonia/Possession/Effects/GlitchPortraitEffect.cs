// WPF: ConditioningControlPanel/Services/Possession/Effects/GlitchPortraitEffect.cs:28-78.
using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class GlitchPortraitEffect : PossessionEffectBase
{
    private readonly Func<AvatarTubeWindow?> _getTube;
    private AvatarTubeWindow? _activeTube;

    internal GlitchPortraitEffect(Func<AvatarTubeWindow?>? getTube = null) =>
        _getTube = getTube ?? (() => AvatarTubeWindow.Live);

    public override string Id => "glitchportrait";
    public override PossessionRung MinRung => PossessionRung.ItKnows;
    public override PossessionIntensity MinIntensity => PossessionIntensity.FullDoki;
    public override bool UsesFlicker => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(250);
    protected override bool ChargeOnApply => false;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var tube = _getTube();
        return !ctx.Photosafe && ctx.Intensity >= PossessionIntensity.FullDoki &&
            tube is { IsVisible: true } && tube.Bounds.Width > 8 && tube.Bounds.Height > 8;
    }

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (!CanApplyCore(ctx, target)) return Task.CompletedTask;
        _activeTube = _getTube();
        _activeTube?.GlitchPortrait(200);
        return Task.CompletedTask;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        _activeTube?.ClearGlitchPortrait();
        _activeTube = null;
        return Task.CompletedTask;
    }
}
