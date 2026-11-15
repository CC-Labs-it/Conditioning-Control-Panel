// WPF Services/Possession/Effects/FallEffect.cs:18-150.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class FallEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] Card = { PossessionRole.Card };
    private PossessionLease? _lease;
    private Rectangle? _chalk;
    private Canvas? _layer;
    private int _generation;
    public override string Id => "fall";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override bool IsBig => true;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(45);
    public override IReadOnlyList<PossessionRole> Roles => Card;

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Host is IAvaloniaPossessionHost { GhostLayer: not null } && target?.Visual is Control card && card is not Window &&
        !card.GetVisualDescendants().OfType<Control>().Any(c => PossessionOffLimits.IsReservedName(c.Name) || PossessionTree.GetExclude(c));

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer ||
            target?.Visual is not Control card || host.BoundsOnGhostLayer(card) is not { } bounds) return;
        _lease = PossessionLease.Take(card);
        if (_lease == null) return;
        var lease = _lease;
        lease.SetOrigin(0, 1);
        _layer = layer;
        _chalk = new Rectangle { Width = bounds.Width, Height = bounds.Height, RadiusX = 6, RadiusY = 6,
            Stroke = new SolidColorBrush(Color.Parse("#FFFF7733")), StrokeThickness = 1.2,
            StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 4, 4 }, Opacity = .55, IsHitTestVisible = false };
        Canvas.SetLeft(_chalk, bounds.X); Canvas.SetTop(_chalk, bounds.Y); layer.Children.Add(_chalk);
        var amp = ctx.Photosafe ? .5 : 1;
        await Animate(400, ct, t => lease.Rotate.Angle = 8 * amp * t);
        Override(card, InputElement.IsHitTestVisibleProperty, false);
        var scaleY = bounds.Height / card.Bounds.Height;
        var drop = Math.Max(120, (layer.Bounds.Height - bounds.Bottom + 40) / (scaleY > 0 ? scaleY : 1));
        await Animate(900, ct, t => { lease.Rotate.Angle = (8 + 6 * t * t) * amp; lease.Translate.Y = drop * t * t; });
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        int generation = ++_generation;
        var lease = _lease;
        if (lease != null && duration > TimeSpan.Zero)
        {
            double angle = lease.Rotate.Angle, y = lease.Translate.Y;
            await Animate(Math.Clamp(duration.TotalMilliseconds, 400, 900), CancellationToken.None, t =>
            {
                if (generation != _generation) return;
                var k = Math.Pow(1 - t, 3);
                lease.Rotate.Angle = angle * k; lease.Translate.Y = y * k;
                if (_chalk != null) _chalk.Opacity = .55 * k;
            });
        }
        if (generation != _generation) return;
        if (_chalk != null) _layer?.Children.Remove(_chalk);
        _chalk = null; _layer = null;
        lease?.ReleaseImmediate(); _lease = null;
    }

    private static async Task Animate(double ms, CancellationToken ct, Action<double> frame)
    {
        long start = Environment.TickCount64;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            double t = Math.Clamp((Environment.TickCount64 - start) / ms, 0, 1);
            frame(t);
            if (t >= 1) return;
            await Task.Delay(28, ct);
        }
    }
}
