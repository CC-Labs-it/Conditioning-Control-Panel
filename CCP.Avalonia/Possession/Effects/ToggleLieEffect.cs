// WPF Services/Possession/Effects/ToggleLieEffect.cs:32-110, 207-272.
// The named-knob path only. The ghost fallback and return blink remain part B.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class ToggleLieEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] TargetRoles = { PossessionRole.Toggle };
    private static readonly string[] Names = { "thumb", "knob", "indicator", "dot", "pill", "handle" };
    private PossessionLease? _lease;
    private IFrameSource? _frames;
    private TimeSpan? _start;
    private double _dx, _slideMs;

    public override string Id => "togglelie";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => (Context?.Rung ?? PossessionRung.Settle) >= PossessionRung.Collapse;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(1500);
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;

    private static (Control knob, double dx)? FindKnob(Control toggle)
    {
        foreach (var knob in toggle.GetVisualDescendants().OfType<Control>())
        {
            if (knob.Name == null || !Names.Any(n => knob.Name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
            if (PossessionLease.IsLeased(knob) || knob.Bounds.Width <= 3 ||
                knob.TranslatePoint(default, toggle) is not { } point) continue;
            double slot = toggle.Bounds.Width - knob.Bounds.Width;
            double dx = slot - 2 * point.X;
            if (slot > 8 && Math.Abs(dx) >= 6) return (knob, dx);
        }
        return null;
    }

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Host is IAvaloniaPossessionHost { GhostLayer: not null } && target?.Visual is ToggleButton toggle &&
        toggle.Bounds.Width > 10 && toggle.Bounds.Height > 6 && FindKnob(toggle) != null;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || target?.Visual is not ToggleButton toggle ||
            FindKnob(toggle) is not { } found) return Task.CompletedTask;
        _lease = PossessionLease.Take(found.knob);
        if (_lease == null) return Task.CompletedTask;
        _dx = found.dx;
        _slideMs = ctx.Photosafe ? 336 : 240;
        _start = null;
        _frames = host.PossessionFrames;
        _frames.Subscribe(Frame);
        return Task.CompletedTask;
    }

    private void Frame(TimeSpan now)
    {
        if (_lease == null) return;
        _start ??= now;
        double f = Math.Clamp((now - _start.Value).TotalMilliseconds / _slideMs, 0, 1);
        _lease.Translate.X = _dx * HeadEffect.CubicOut(f);
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        _frames?.Unsubscribe(Frame);
        _frames = null;
        _lease?.ReleaseImmediate();
        _lease = null;
        _start = null;
        return Task.CompletedTask;
    }
}
