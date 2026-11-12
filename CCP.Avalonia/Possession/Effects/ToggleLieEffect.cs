// WPF Services/Possession/Effects/ToggleLieEffect.cs:32-272.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class ToggleLieEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] TargetRoles = { PossessionRole.Toggle };
    private static readonly string[] Names = { "thumb", "knob", "indicator", "dot", "switch", "pill", "handle" };
    private PossessionLease? _lease;
    private PossessionGhost? _ghost;
    private Canvas? _layer;
    private Border? _blink;
    private IFrameSource? _frames;
    private CancellationTokenSource? _returnCts;
    private TimeSpan? _start;
    private double _dx, _slideMs, _returnMs, _returnFrom;
    private bool _returning, _photosafe;

    public override string Id => "togglelie";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => (Context?.Rung ?? PossessionRung.Settle) >= PossessionRung.Collapse;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(1500);
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;

    private static Control? FindKnob(Control toggle)
    {
        var children = toggle.GetVisualDescendants().OfType<Control>().ToArray();
        return children.FirstOrDefault(c => c.Name != null && Names.Any(n => c.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            ?? children.OfType<Thumb>().FirstOrDefault()
            ?? (Control?)children.OfType<Ellipse>().FirstOrDefault(c => c.Bounds.Width > 3)
            ?? children.OfType<Border>().FirstOrDefault(c => c.Bounds.Width > 4 &&
                c.Bounds.Width < toggle.Bounds.Width * 0.6 && c.Bounds.Height > toggle.Bounds.Height * 0.35);
    }

    private static bool TryMirror(Control toggle, Control knob, out double dx)
    {
        dx = 0;
        if (knob.Bounds.Width <= 0 || knob.TranslatePoint(default, toggle) is not { } at) return false;
        double slot = toggle.Bounds.Width - knob.Bounds.Width;
        dx = slot - 2 * at.X;
        return slot > 8 && Math.Abs(dx) >= 6;
    }

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Host is IAvaloniaPossessionHost { GhostLayer: not null } && target?.Visual is ToggleButton toggle &&
        toggle.Bounds.Width > 10 && toggle.Bounds.Height > 6 && toggle.TransformToVisual(((IAvaloniaPossessionHost)ctx.Host).GhostLayer!) != null;

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer ||
            target?.Visual is not ToggleButton toggle) return;
        _layer = layer;
        _photosafe = ctx.Photosafe;
        var knob = FindKnob(toggle);
        if (knob != null && !PossessionLease.IsLeased(knob) && TryMirror(toggle, knob, out _dx))
        {
            _lease = PossessionLease.Take(knob);
            if (_lease != null)
            {
                _slideMs = ctx.Photosafe ? 336 : 240;
                _frames = host.PossessionFrames;
                _frames.Subscribe(Frame);
                await Task.Delay(270, ct).ConfigureAwait(true);
                return;
            }
        }

        _ghost = PossessionGhost.Capture(toggle, layer);
        if (_ghost == null) return;
        _ghost.Hide(); // Only opacity: the real control still takes input.
        var image = _ghost.Image;
        double h = Math.Max(8, image.Height * 0.72);
        bool left = knob?.TranslatePoint(default, toggle) is not { } at ||
                    at.X < (toggle.Bounds.Width - knob.Bounds.Width) / 2;
        var pill = new Ellipse { Width = h, Height = h, Fill = new SolidColorBrush(EmberAttribution.Ember),
            Opacity = 0.85, IsHitTestVisible = false };
        double inset = Math.Max(2, image.Height * 0.14);
        _ghost.AddExtra(pill, Canvas.GetLeft(image) + (left ? image.Width - h - inset : inset),
            Canvas.GetTop(image) + (image.Height - h) / 2);
        await Task.Delay(120, ct).ConfigureAwait(true);
    }

    private void Frame(TimeSpan now)
    {
        _start ??= now;
        double ms = Math.Max(0, (now - _start.Value).TotalMilliseconds);
        if (_lease != null)
        {
            double f = Math.Clamp(ms / (_returning ? _returnMs : _slideMs), 0, 1);
            _lease.Translate.X = _returning ? _returnFrom * (1 - HeadEffect.CubicOut(f)) : _dx * HeadEffect.CubicOut(f);
        }
        if (_blink != null)
        {
            double rise = _photosafe ? 260 : 90, fall = _photosafe ? 460 : 220, peak = _photosafe ? 0.22 : 0.45;
            _blink.Opacity = Math.Clamp(HeadEffectPulse(ms, peak, rise, fall), 0, 1);
        }
    }

    private static double HeadEffectPulse(double ms, double peak, double rise, double fall) =>
        ms < rise ? peak * HeadEffect.CubicOut(ms / rise) :
        ms < rise + fall ? peak * (1 - HeadEffect.CubicIn((ms - rise) / fall)) : 0;

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            _returnCts?.Cancel();
            Cleanup();
            return;
        }
        _returnCts = new CancellationTokenSource();
        var source = _returnCts;
        try
        {
            _returnMs = Math.Clamp(duration.TotalMilliseconds, 150, 320);
            _returnFrom = _lease?.Translate.X ?? 0;
            _returning = true;
            _start = null;
            if (_frames == null && Context?.Host is IAvaloniaPossessionHost host)
            {
                _frames = host.PossessionFrames;
                _frames.Subscribe(Frame);
            }
            if (_layer != null && Target?.Visual is Control toggle && toggle.TransformToVisual(_layer) is { } transform)
            {
                var bounds = new Rect(toggle.Bounds.Size).TransformToAABB(transform);
                _blink = new Border { Width = bounds.Width, Height = bounds.Height,
                    CornerRadius = new CornerRadius(Math.Min(12, bounds.Height / 2)),
                    Background = new SolidColorBrush(EmberAttribution.Ember), Opacity = 0, IsHitTestVisible = false };
                Canvas.SetLeft(_blink, bounds.X); Canvas.SetTop(_blink, bounds.Y);
                _layer.Children.Add(_blink);
            }
            if (_lease != null) await Task.Delay((int)_returnMs + 20, source.Token).ConfigureAwait(true);
            _ghost?.Dispose();
            _ghost = null;
            await Task.Delay(_photosafe ? 420 : 240, source.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_returnCts, source)) { Cleanup(); _returnCts = null; }
            source.Dispose();
        }
    }

    private void Cleanup()
    {
        _frames?.Unsubscribe(Frame);
        _frames = null;
        _lease?.ReleaseImmediate();
        _lease = null;
        _ghost?.Dispose();
        _ghost = null;
        if (_blink != null) _layer?.Children.Remove(_blink);
        _blink = null;
        _layer = null;
        _returning = false;
        _start = null;
    }
}
