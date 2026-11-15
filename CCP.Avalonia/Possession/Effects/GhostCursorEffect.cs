// WPF Services/Possession/Effects/GhostCursorEffect.cs:32-315.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class GhostCursorEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] TargetRoles = { PossessionRole.Button, PossessionRole.Toggle };
    private Canvas? _layer;
    private Path? _arrow;
    private Ellipse? _ripple;
    private IFrameSource? _frames;
    private PossessionLease? _lease;
    private TimeSpan? _start;
    private Point _from, _landing, _exit;
    private double _slow, _glide, _leaveMs;
    private bool _press, _leaving;
    private TaskCompletionSource? _completion;

    public override string Id => "ghostcursor";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => (Context?.Rung ?? PossessionRung.Settle) >= PossessionRung.Collapse;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(6);
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;
    protected override string? NameOverride => ConditioningControlPanel.Localization.Loc.Get("possession_target_ghost_cursor");

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer == null || target?.Visual is not Control c ||
            host.BoundsOnGhostLayer(c) is not { Width: >= 8, Height: >= 8 } b) return false;
        var p = host.PointerPosition;
        return (p.X <= 0 && p.Y <= 0) || Math.Sqrt(Math.Pow(b.X + b.Width / 2 - p.X, 2) + Math.Pow(b.Y + b.Height / 2 - p.Y, 2)) >= 150;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || host.GhostLayer is not { } layer ||
            target?.Visual is not Control c || host.BoundsOnGhostLayer(c) is not { } b) return;
        double w = layer.Bounds.Width, h = layer.Bounds.Height;
        if (w <= 0 || h <= 0) return;
        _layer = layer;
        _landing = new Point(b.X + b.Width * (.35 + ctx.Rng.NextDouble() * .3), b.Y + b.Height * (.35 + ctx.Rng.NextDouble() * .3));
        bool left = _landing.X > w / 2;
        _from = new Point(left ? -48 : w + 48, Math.Clamp(_landing.Y + (ctx.Rng.NextDouble() * 280 - 140), 24, Math.Max(24, h - 24)));
        _exit = new Point(left ? w + 60 : -60, Math.Clamp(_landing.Y + (ctx.Rng.NextDouble() * 260 - 100), 12, Math.Max(12, h - 12)));
        _press = ctx.Rung >= PossessionRung.Melt;
        _slow = ctx.Photosafe ? 1.35 : 1;
        _glide = (_press ? 1500 : 1200) * _slow;
        _arrow = new Path { Data = Geometry.Parse("M 0,0 L 0,17.5 L 4.4,13.2 L 7.3,19.2 L 10.1,17.9 L 7.2,12.1 L 12.4,12.1 Z"),
            Fill = new SolidColorBrush(EmberAttribution.Ember), Stroke = new SolidColorBrush(EmberAttribution.Ember), StrokeThickness = .8,
            Opacity = 0, IsHitTestVisible = false, RenderTransform = new ScaleTransform(1.45, 1.45),
            Effect = new DropShadowEffect { Color = EmberAttribution.Ember, BlurRadius = 16, OffsetX = 0, OffsetY = 0, Opacity = .9 } };
        layer.Children.Add(_arrow);
        _frames = host.PossessionFrames;
        _completion = new TaskCompletionSource();
        _frames.Subscribe(Frame);
        await Task.WhenAny(_completion.Task, Task.Delay(TimeSpan.FromMilliseconds(_glide + 1100 * _slow), ct)).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
    }

    private static Point Curve(Point a, Point b, double t)
    {
        var v = new Vector(b.X - a.X, b.Y - a.Y);
        var length = v.Length;
        var n = length > .001 ? new Vector(-v.Y / length, v.X / length) : default;
        var c1 = a + v * .32 + n * 75;
        var c2 = a + v * .68 - n * 41.25;
        double u = 1 - t;
        return new Point(u*u*u*a.X + 3*u*u*t*c1.X + 3*u*t*t*c2.X + t*t*t*b.X,
            u*u*u*a.Y + 3*u*u*t*c1.Y + 3*u*t*t*c2.Y + t*t*t*b.Y);
    }

    private void Frame(TimeSpan now)
    {
        if (_arrow == null) return;
        _start ??= now;
        double ms = Math.Max(0, (now - _start.Value).TotalMilliseconds);
        var p = _leaving ? Curve(_landing, _exit, Math.Clamp(ms / _leaveMs, 0, 1)) : Curve(_from, _landing, Math.Clamp(ms / _glide, 0, 1));
        Canvas.SetLeft(_arrow, p.X); Canvas.SetTop(_arrow, p.Y);
        _arrow.Opacity = _leaving ? .7 * (1 - Math.Clamp(ms / _leaveMs, 0, 1)) : .7 * Math.Clamp(ms / (300 * _slow), 0, 1);
        if (_leaving) { if (ms >= _leaveMs) _completion?.TrySetResult(); return; }
        double pressAt = _glide + 400 * _slow;
        if (_press && ms >= pressAt && _ripple == null && _layer != null)
        {
            _ripple = new Ellipse { Width = 52, Height = 52, Stroke = new SolidColorBrush(EmberAttribution.Ember), StrokeThickness = 2,
                IsHitTestVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = new ScaleTransform(.2, .2) };
            Canvas.SetLeft(_ripple, _landing.X - 26); Canvas.SetTop(_ripple, _landing.Y - 26); _layer.Children.Add(_ripple);
            _lease = PossessionLease.Take(Target?.Visual as Control); _lease?.SetOrigin(.5, .5);
        }
        if (_ripple != null)
        {
            double elapsed = (ms - pressAt) / _slow;
            double t = Math.Clamp(elapsed / 520, 0, 1);
            var scale = (ScaleTransform)_ripple.RenderTransform!;
            scale.ScaleX = scale.ScaleY = .2 + .8 * (1 - Math.Pow(1 - t, 3));
            _ripple.Opacity = (Context?.Photosafe == true ? .5 : .8) * (t < .25 ? t / .25 : (1 - t) / .75);
            if (_lease != null)
            {
                double squeeze = elapsed < 120
                    ? 1 - .04 * HeadEffect.CubicOut(Math.Clamp(elapsed / 90, 0, 1))
                    : .96 + .04 * HeadEffect.CubicOut(Math.Clamp((elapsed - 120) / 190, 0, 1));
                _lease.Scale.ScaleX = _lease.Scale.ScaleY = squeeze;
            }
        }
        if (ms >= pressAt + (_press ? 540 * _slow : 0)) _completion?.TrySetResult();
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        _lease?.ReleaseImmediate(); _lease = null;
        if (duration > TimeSpan.Zero && _arrow != null && _frames != null)
        {
            var leavingArrow = _arrow;
            _leaving = true; _start = null; _leaveMs = Math.Clamp(duration.TotalMilliseconds, 400, 900);
            _completion = new TaskCompletionSource();
            await Task.WhenAny(_completion.Task, Task.Delay((int)_leaveMs + 40)).ConfigureAwait(true);
            if (!ReferenceEquals(_arrow, leavingArrow)) return;
        }
        _frames?.Unsubscribe(Frame); _frames = null;
        if (_layer != null) { if (_arrow != null) _layer.Children.Remove(_arrow); if (_ripple != null) _layer.Children.Remove(_ripple); }
        _arrow = null; _ripple = null; _layer = null; _start = null; _leaving = false; _completion = null;
    }
}
