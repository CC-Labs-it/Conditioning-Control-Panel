// Adapter over the existing four frame-driven effects; Core owns holds, cooldowns and reassembly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class PossessionDirectorEffect(HeadEffect effect) : IPossessionEffect
{
    public string Id => effect.Id;
    public PossessionRung MinRung => effect.MinRung;
    public PossessionIntensity MinIntensity => effect.MinIntensity;
    public bool IsBig => false;
    public bool UsesFlicker => effect.UsesFlicker;
    public double Weight => effect.Weight;
    public TimeSpan HoldFor => effect.HoldFor;
    public IReadOnlyList<PossessionRole> Roles => effect.Roles;
    public bool IsLive { get; private set; }
    private PossessionLease? _lease;
    private PossessionHost? _host;
    private IDisposable? _outline;
    private CancellationTokenSource? _cancel;
    private TimeSpan? _start, _returnAt;
    private double _returnMs, _x, _y, _angle, _sx, _sy;
    private TaskCompletionSource? _returned;
    public bool CanApply(PossessionContext ctx, PossessionTarget? target) => !IsLive && target is { IsLive: false, Visual: Control c }
        && Roles.Contains(target.Role) && c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0
        && !PossessionTree.IsOffLimits(c) && !PossessionLease.IsLeased(c);
    public async Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (IsLive || target?.Visual is not Control c || PossessionTree.IsOffLimits(c)) return;
        IsLive = true; _host = (PossessionHost)ctx.Host;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cancel.Token;
        try
        {
            await ctx.Attribution.ChargeAsync(c, token);
            token.ThrowIfCancellationRequested();
            if (!c.IsEffectivelyVisible || PossessionTree.IsOffLimits(c)) { Release(); return; }
            _outline = ctx.Attribution.Possess(c);
            _lease = PossessionLease.Take(c)!; effect.Begin(_lease, ctx.Rng, ctx.Photosafe);
            _start = _returnAt = null; _host.Frames.Subscribe(OnFrame);
        }
        catch { Release(); throw; }
    }
    private void OnFrame(TimeSpan now)
    {
        if (_lease is not { } l) return;
        _start ??= now;
        if (_returned == null) { effect.Frame((now - _start.Value).TotalMilliseconds); return; }
        _returnAt ??= now;
        double f = Math.Clamp((now - _returnAt.Value).TotalMilliseconds / _returnMs, 0, 1);
        if (f >= 1) { Release(); return; }
        double k = 1 - HeadEffect.CubicInOut(f);
        l.Translate.X = _x * k; l.Translate.Y = _y * k; l.Rotate.Angle = _angle * k;
        l.Scale.ScaleX = 1 + (_sx - 1) * k; l.Scale.ScaleY = 1 + (_sy - 1) * k;
    }
    public Task UndoAsync(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || _lease == null) { Release(); return Task.CompletedTask; }
        if (_returned != null) return _returned.Task;
        _returnMs = effect is NudgeHeadEffect ? Math.Clamp(duration.TotalMilliseconds, 220, 600)
            : Math.Clamp(duration.TotalMilliseconds, 250, 700);
        _x = _lease.Translate.X; _y = _lease.Translate.Y; _angle = _lease.Rotate.Angle;
        _sx = _lease.Scale.ScaleX; _sy = _lease.Scale.ScaleY;
        _returned = new TaskCompletionSource(); return _returned.Task;
    }
    private void Release()
    {
        _host?.Frames.Unsubscribe(OnFrame); _host = null;
        _cancel?.Cancel(); _cancel?.Dispose(); _cancel = null;
        _lease?.ReleaseImmediate(); _lease = null; _outline?.Dispose(); _outline = null;
        IsLive = false; var done = _returned; _returned = null; done?.TrySetResult();
    }
}
