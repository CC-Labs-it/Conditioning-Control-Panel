// WPF Services/Possession/Effects/SliderCreepEffect.cs:27-233.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class SliderCreepEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] SliderRole = { PossessionRole.Slider };
    private Canvas? _layer;
    private Border? _ghost;
    private IFrameSource? _frames;
    private TimeSpan? _start;
    private TaskCompletionSource? _completion;
    private bool _vertical;
    private double _distance, _slow;

    public override string Id => "slidercreep";
    public override PossessionRung MinRung => PossessionRung.Settle;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(3);
    public override IReadOnlyList<PossessionRole> Roles => SliderRole;

    private static Slider? SliderOf(object? visual) => visual is Slider slider ? slider :
        (visual as Visual)?.GetVisualDescendants().OfType<Slider>().FirstOrDefault();

    private static Slider? RecentSlider(PossessionContext ctx, IAvaloniaPossessionHost host)
    {
        var pointer = host.PossessionPointer;
        if (DateTime.Now - pointer.LastClickAt > TimeSpan.FromSeconds(10)) return null;
        var clicked = pointer.LastClicked;
        var slider = SliderOf(clicked) ?? clicked?.GetVisualAncestors().OfType<Slider>().FirstOrDefault();
        if (slider == null || !slider.IsEffectivelyVisible) return null;
        return ctx.Host.Targets.Any(t => t.Role == PossessionRole.Slider && !t.IsLive &&
            ReferenceEquals(SliderOf(t.Visual), slider)) ? slider : null;
    }

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer == null) return false;
        var slider = SliderOf(target?.Visual);
        if (slider == null || slider.Maximum <= slider.Minimum || host.BoundsOnGhostLayer(slider) is not { } bounds ||
            bounds.Width <= 0 || bounds.Height <= 0) return false;
        var recent = RecentSlider(ctx, host);
        return recent == null || ReferenceEquals(recent, slider);
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer ||
            SliderOf(target?.Visual) is not { } slider || host.BoundsOnGhostLayer(slider) is not { } bounds ||
            slider.Maximum <= slider.Minimum) return;

        _vertical = slider.Orientation == Orientation.Vertical;
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
        var thumbBounds = thumb == null ? null : host.BoundsOnGhostLayer(thumb);
        var rect = thumbBounds is { Width: > 1, Height: > 1 } actual ? actual : EstimateThumb(slider, bounds, _vertical);
        double span = slider.Maximum - slider.Minimum;
        double track = _vertical ? Math.Max(1, bounds.Height - rect.Height) : Math.Max(1, bounds.Width - rect.Width);
        double notch = slider.SmallChange > 0 ? slider.SmallChange : span / 20;
        _distance = Math.Clamp(notch / span * track, 12, 70);
        _slow = ctx.Photosafe ? 1.3 : 1;
        _ghost = new Border
        {
            Width = Math.Max(6, rect.Width), Height = Math.Max(6, rect.Height),
            CornerRadius = new CornerRadius(Math.Min(6, Math.Min(rect.Width, rect.Height) / 2)),
            Background = new SolidColorBrush(Color.Parse("#FF8A5C")), Opacity = 0,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 14, Color = Color.Parse("#D9FF8A5C") }),
            IsHitTestVisible = false, RenderTransform = new TranslateTransform()
        };
        Canvas.SetLeft(_ghost, rect.X);
        Canvas.SetTop(_ghost, rect.Y);
        _layer = layer;
        layer.Children.Add(_ghost);
        _frames = host.PossessionFrames;
        _start = null;
        _completion = new TaskCompletionSource();
        _frames.Subscribe(Frame);
        await Task.WhenAny(_completion.Task, Task.Delay(TimeSpan.FromMilliseconds(1950 * _slow + 80), ct)).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        RemoveGhost();
        _completion = null;
    }

    private static Rect EstimateThumb(Slider slider, Rect bounds, bool vertical)
    {
        double f = Math.Clamp((slider.Value - slider.Minimum) / (slider.Maximum - slider.Minimum), 0, 1);
        if (vertical)
        {
            double h = Math.Min(18, Math.Max(8, bounds.Height * 0.06));
            return new Rect(bounds.X + bounds.Width / 2 - 7, bounds.Y + (1 - f) * Math.Max(0, bounds.Height - h), 14, h);
        }
        double w = Math.Min(18, Math.Max(8, bounds.Width * 0.06));
        return new Rect(bounds.X + f * Math.Max(0, bounds.Width - w), bounds.Y + bounds.Height / 2 - 9,
            w, Math.Min(22, Math.Max(10, bounds.Height)));
    }

    private void Frame(TimeSpan now)
    {
        if (_ghost == null) return;
        _start ??= now;
        double ms = Math.Max(0, (now - _start.Value).TotalMilliseconds / _slow);
        double t = Math.Clamp(ms / 1500, 0, 1);
        double ease = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        var translate = (TranslateTransform)_ghost.RenderTransform!;
        if (_vertical) translate.Y = _distance * ease; else translate.X = -_distance * ease;
        _ghost.Opacity = 0.6 * Math.Min(1, ms / 220) * (1 - Math.Clamp((ms - 1540) / 450, 0, 1));
        if (ms >= 1950 + 80 / _slow) { RemoveGhost(); _completion?.TrySetResult(); }
    }

    private void RemoveGhost()
    {
        _frames?.Unsubscribe(Frame);
        _frames = null;
        if (_layer != null && _ghost != null) _layer.Children.Remove(_ghost);
        _ghost = null;
        _layer = null;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        RemoveGhost();
        _completion = null;
        return Task.CompletedTask;
    }
}
