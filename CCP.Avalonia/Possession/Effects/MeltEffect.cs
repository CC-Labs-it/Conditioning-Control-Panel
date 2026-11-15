// WPF Services/Possession/Effects/MeltEffect.cs:17-155.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class MeltEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] TargetRoles = { PossessionRole.Card, PossessionRole.Button };
    private Control? _element;
    private IFrameSource? _frames;
    private PossessionLease? _lease;
    private BlurEffect? _blur;
    private IDisposable? _effectValue;
    private CancellationToken _token;
    private Task? _charge;
    private TimeSpan? _start;
    private double _amount, _from, _to, _duration, _amplitude;
    private bool _hover;
    private int _generation;

    public override string Id => "melt";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override bool IsBig => true;
    public override double Weight => 4;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(25);
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;
    protected override bool ChargeOnApply => false;
    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => ctx.Host is IAvaloniaPossessionHost;

    protected override Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || target?.Visual is not Control element) return Task.CompletedTask;
        _element = element; _frames = host.PossessionFrames; _token = ct;
        _amplitude = ctx.Photosafe ? 0.5 : 1;
        element.PointerEntered += Enter;
        element.PointerExited += Leave;
        if (element.IsPointerOver) Enter(element, null!);
        return Task.CompletedTask;
    }

    private async void Enter(object? sender, PointerEventArgs e)
    {
        var element = _element;
        if (element == null) return;
        _hover = true;
        int generation = ++_generation;
        try
        {
            _charge ??= ChargeAndPossessAsync(element, _token);
            await _charge.ConfigureAwait(true);
            if (_token.IsCancellationRequested || generation != _generation || !_hover || _element != element) return;
            if (_lease == null)
            {
                _lease = PossessionLease.Take(element);
                _lease?.SetOrigin(0.5, 1);
                if (element.Effect == null)
                {
                    _blur = new BlurEffect();
                    _effectValue = element.SetValue(Visual.EffectProperty, _blur, BindingPriority.Animation);
                }
                _frames?.Subscribe(Frame);
            }
            AnimateTo(1, 900);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning("Possession melt failed: {Error}", ex.Message); }
    }

    private void Leave(object? sender, PointerEventArgs e)
    {
        _hover = false;
        ++_generation;
        if (_lease != null) AnimateTo(0, 600);
    }

    private void AnimateTo(double target, double duration)
    {
        _from = _amount; _to = target; _duration = duration; _start = null;
    }

    private void Frame(TimeSpan now)
    {
        if (_lease == null) return;
        _start ??= now;
        double t = Math.Clamp((now - _start.Value).TotalMilliseconds / _duration, 0, 1);
        double eased = _to > _from ? HeadEffect.CubicOut(t) : HeadEffect.CubicInOut(t);
        _amount = _from + (_to - _from) * eased;
        double amount = _amount * _amplitude;
        _lease.Skew.AngleY = 6 * amount;
        _lease.Scale.ScaleY = 1 - 0.08 * amount;
        _lease.Translate.Y = 6 * amount;
        if (_blur != null) _blur.Radius = 2 * amount;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        ++_generation;
        if (_element != null) { _element.PointerEntered -= Enter; _element.PointerExited -= Leave; }
        _frames?.Unsubscribe(Frame);
        _effectValue?.Dispose(); _effectValue = null; _blur = null;
        _lease?.ReleaseImmediate(); _lease = null;
        _element = null; _frames = null; _charge = null; _hover = false; _amount = 0; _start = null;
        return Task.CompletedTask;
    }
}
