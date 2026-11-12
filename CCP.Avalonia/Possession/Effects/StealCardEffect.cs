// WPF Services/Possession/Effects/StealCardEffect.cs:31-218: the no-tube theft and return.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class StealCardEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] Card = { PossessionRole.Card };
    private readonly HashSet<string> _stolen = new(StringComparer.Ordinal);
    private LockdownService? _hooked;
    private PossessionGhost? _ghost;
    private ScaleTransform? _scale;

    public override string Id => "stealcard";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(120);
    public override IReadOnlyList<PossessionRole> Roles => Card;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var lockdown = LockdownService.Current;
        if (!ReferenceEquals(_hooked, lockdown))
        {
            if (_hooked != null) _hooked.LockdownActivated -= Reset;
            _hooked = lockdown;
            if (_hooked != null) _hooked.LockdownActivated += Reset;
            _stolen.Clear();
        }
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer ||
            target?.Visual is not Control card || card is Window || _stolen.Contains(target.Key) ||
            CoreEngine.Video?.IsPlaying == true || PossessionTree.IsOffLimits(card)) return false;
        if (host.BoundsOnGhostLayer(card) is not { } bounds || bounds.Width < 40 || bounds.Height < 24 ||
            (layer.Bounds.Width > 0 && bounds.Width > layer.Bounds.Width * .8) ||
            (layer.Bounds.Height > 0 && bounds.Height > layer.Bounds.Height * .7)) return false;
        return card.GetVisualDescendants().OfType<Control>().Any(c =>
            c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.Bounds.Width > 0 && c.Bounds.Height > 0 &&
            c is Button or ToggleButton or Slider or ComboBox or TextBox or ListBoxItem);
    }

    private void Reset() => _stolen.Clear();

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (target?.Visual is not Control card || ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer) return;
        var ghost = PossessionGhost.Capture(card, layer);
        if (ghost == null) return;
        _ghost = ghost;
        _stolen.Add(target.Key);
        ghost.Hide(true);
        _scale = new ScaleTransform(1, 1);
        ghost.Image.RenderTransformOrigin = RelativePoint.Center;
        ghost.Image.RenderTransform = _scale;
        await AnimateAsync(ghost, _scale, 1, .06, 700, ct);
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        var ghost = _ghost;
        var scale = _scale;
        try
        {
            if (ghost != null && scale != null && duration > TimeSpan.Zero)
                await AnimateAsync(ghost, scale, scale.ScaleX, 1, Math.Clamp(duration.TotalMilliseconds, 260, 650), CancellationToken.None);
        }
        finally
        {
            ghost?.Dispose();
            _ghost = null;
            _scale = null;
        }
    }

    private static async Task AnimateAsync(PossessionGhost ghost, ScaleTransform scale, double from, double to, double ms, CancellationToken ct)
    {
        var start = Environment.TickCount64;
        var opacityFrom = ghost.Image.Opacity;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var t = Math.Clamp((Environment.TickCount64 - start) / ms, 0, 1);
            var eased = to < from ? t * t * t : 1 - Math.Pow(1 - t, 3);
            scale.ScaleX = scale.ScaleY = from + (to - from) * eased;
            ghost.Image.Opacity = opacityFrom + ((to < from ? 0 : 1) - opacityFrom) * eased;
            if (t >= 1) return;
            await Task.Delay(28, ct);
        }
    }
}
