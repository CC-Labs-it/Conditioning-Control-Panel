// WPF Services/Possession/Effects/XpDrainEffect.cs:27-151.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class XpDrainEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] TargetRoles = { PossessionRole.Progress, PossessionRole.Label };
    private static readonly List<XpDrainEffect> Live = new();
    private Canvas? _layer;
    private Border? _ghost;
    private TextBlock? _chip;
    private string? _original, _fake;
    private IDisposable? _barOpacity;
    private IFrameSource? _frames;
    private TimeSpan? _start;
    private double _width;

    public override string Id => "xpdrain";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(6);
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;
    protected override bool ChargeOnApply => false;

    private static Border? Bar(IAvaloniaPossessionHost host, PossessionTarget? target) =>
        target?.Role == PossessionRole.Progress && target.Visual is Border bar ? bar :
        host.PossessionWindow?.GetVisualDescendants().OfType<Border>().FirstOrDefault(x => x.Name == "XPBar");

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (ctx.Host is not IAvaloniaPossessionHost { GhostLayer: not null } host ||
            target?.Visual is not Control selected ||
            (target.Role == PossessionRole.Label && selected.Name != "TxtLevelLabel")) return false;
        var bar = Bar(host, target);
        return bar != null && bar.IsEffectivelyVisible && !PossessionTree.IsOffLimits(bar) &&
            !PossessionLease.IsLeased(bar) && host.BoundsOnGhostLayer(bar) is { Width: >= 6, Height: >= 2 };
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer || Bar(host, target) is not { } bar) return;
        Live.Add(this);
        await ChargeAndPossessAsync(bar, ct, "the XP bar").ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        if (!CanApplyCore(ctx, target) || host.BoundsOnGhostLayer(bar) is not { } bounds) return;
        _layer = layer;
        _width = bounds.Width;
        _ghost = new Border { Width = bounds.Width, Height = bounds.Height, Background = bar.Background,
            CornerRadius = bar.CornerRadius, IsHitTestVisible = false };
        Canvas.SetLeft(_ghost, bounds.X); Canvas.SetTop(_ghost, bounds.Y);
        layer.Children.Add(_ghost);
        _barOpacity = bar.SetValue(Visual.OpacityProperty, 0d, BindingPriority.Animation);
        _chip = host.PossessionWindow?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(x => x.Name == "TxtLevelLabel");
        if (_chip != null && !PossessionTree.IsOffLimits(_chip) && TypoHeadEffect.IsRewritable(_chip, 2))
        {
            _original = _chip.Text;
            _fake = Loc.Get("possession_level_zero");
            if (string.IsNullOrWhiteSpace(_fake) || _fake == "possession_level_zero") _fake = "LVL 0";
            _chip.Text = _fake;
        }
        _frames = host.PossessionFrames;
        _frames.Subscribe(Frame);
    }

    private void Frame(TimeSpan now)
    {
        if (_ghost == null) return;
        _start ??= now;
        double t = Math.Clamp((now - _start.Value).TotalMilliseconds / 1500, 0, 1);
        _ghost.Width = _width * (1 - t * t * (3 - 2 * t));
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        _frames?.Unsubscribe(Frame); _frames = null; _start = null;
        _barOpacity?.Dispose(); _barOpacity = null;
        if (_ghost != null) _layer?.Children.Remove(_ghost);
        _ghost = null; _layer = null;
        if (_chip != null && _original != null && _chip.Text == _fake) _chip.Text = _original;
        _chip = null; _original = null; _fake = null;
        Live.Remove(this);
        return Task.CompletedTask;
    }

    internal static void UndoAllLive()
    {
        foreach (var effect in Live.ToArray()) _ = effect.UndoAsync(TimeSpan.Zero);
    }
}
