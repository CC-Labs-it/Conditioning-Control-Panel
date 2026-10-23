using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

/// <summary>Adapts the existing frame-driven effects to Core's lifecycle/deck; no second scheduler.</summary>
internal sealed class DirectorHeadEffect(HeadEffect effect, PossessionRuntime runtime) : IPossessionEffect
{
    private IDisposable? _attribution;
    public string Id => effect.Id;
    public PossessionRung MinRung => effect.MinRung;
    public PossessionIntensity MinIntensity => effect.MinIntensity;
    public bool IsBig => false; // all four existing effects are WPF micro-tics
    public bool UsesFlicker => effect.UsesFlicker;
    public double Weight => effect.Weight;
    public TimeSpan HoldFor => effect.HoldFor;
    public IReadOnlyList<PossessionRole> Roles => effect.Roles;
    public bool IsLive => runtime.IsLive(effect);
    public bool CanApply(PossessionContext ctx, PossessionTarget? target) => !IsLive
        && target?.Visual is Control { IsEffectivelyVisible: true } c && c.Bounds.Width > 0 && c.Bounds.Height > 0
        && Roles.Contains(target.Role) && !PossessionLease.IsLeased(c)
        && (!PossessionTree.IsOffLimits(c) || effect is WobbleHeadEffect && PossessionTree.IsTaggedTimer(c));
    public async Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (target?.Visual is not Control c) return;
        await ctx.Attribution.ChargeAsync(c, ct);
        ct.ThrowIfCancellationRequested();
        runtime.Photosafe = ctx.Photosafe;
        if (runtime.TryApply(effect, new PossessionHeadTarget(c, target.Role, target.Key)))
            _attribution = ctx.Attribution.Possess(c);
    }
    public async Task UndoAsync(TimeSpan duration)
    {
        await runtime.Return(effect, duration);
        _attribution?.Dispose(); _attribution = null;
    }
}
