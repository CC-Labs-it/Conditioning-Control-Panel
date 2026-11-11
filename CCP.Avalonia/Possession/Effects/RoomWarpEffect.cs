// WPF: Services/Possession/Effects/RoomWarpEffect.cs:29-115, 154-223.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

/// <summary>The reversible room lean and initial wash. The window toll is a later slice.</summary>
internal sealed class RoomWarpEffect : PossessionEffectBase
{
    private PossessionLease? _lease;
    private Canvas? _layer;
    private Border? _wash;
    private IFrameSource? _frames;
    private TimeSpan? _start;
    private double _angle, _sag, _opacity;
    public override string Id => "roomwarp";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.Zero;
    protected override bool ChargeOnApply => false;

    private static Control? CanvasOf(IAvaloniaPossessionHost host)
    {
        var window = host.PossessionWindow;
        return window?.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "DesignCanvas")
            ?? window?.GetVisualDescendants().OfType<Viewbox>().FirstOrDefault()?.Child as Control;
    }

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Host is IAvaloniaPossessionHost host && host.GhostLayer != null
        && CanvasOf(host) is { IsEffectivelyVisible: true } canvas && !PossessionLease.IsLeased(canvas);

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || CanvasOf(host) is not { } canvas || host.GhostLayer is not { } layer) return;
        await ChargeAndPossessAsync(canvas, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        _lease = PossessionLease.Take(canvas);
        if (_lease == null) return;
        _lease.SetOrigin(0.5, 0.5);
        double amp = ctx.Photosafe ? 0.5 : 1;
        _angle = (0.5 + ctx.Rng.NextDouble() * 0.5) * (ctx.Rng.Next(2) == 0 ? -1 : 1) * amp;
        _sag = 4 * amp;
        _opacity = (ctx.Rung >= PossessionRung.ItKnows ? 0.08 : 0.04) * amp;
        _layer = layer;
        _wash = new Border { Width = layer.Bounds.Width, Height = layer.Bounds.Height,
            Background = new SolidColorBrush(Color.FromRgb(0xdc, 0x14, 0x3c)), Opacity = 0, IsHitTestVisible = false };
        layer.Children.Add(_wash);
        _frames = host.PossessionFrames;
        _frames.Subscribe(OnFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        _start ??= now;
        double t = Math.Clamp((now - _start.Value).TotalMilliseconds / 900, 0, 1);
        double ease = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        if (_lease != null) { _lease.Rotate.Angle = _angle * ease; _lease.Translate.Y = _sag * ease; }
        if (_wash != null) _wash.Opacity = _opacity * ease;
        if (t >= 1) { _frames?.Unsubscribe(OnFrame); _frames = null; }
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        _frames?.Unsubscribe(OnFrame);
        _frames = null;
        _start = null;
        if (_wash != null) _layer?.Children.Remove(_wash);
        _wash = null;
        _layer = null;
        _lease?.ReleaseImmediate();
        _lease = null;
        return Task.CompletedTask;
    }
}
