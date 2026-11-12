// WPF Services/Possession/Effects/DodgeEffect.cs:47-394.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class DodgeEffect : IPossessionEffect
{
    private static readonly PossessionRole[] TargetRoles = { PossessionRole.Button, PossessionRole.Toggle, PossessionRole.TabHeader };
    public string Id => "dodge";
    public PossessionRung MinRung => PossessionRung.Drift;
    public PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public bool IsBig => true;
    public bool UsesFlicker => false;
    public double Weight => 3;
    public TimeSpan HoldFor => TimeSpan.FromSeconds(20);
    public IReadOnlyList<PossessionRole> Roles => TargetRoles;
    public bool IsLive { get; private set; }

    private IAvaloniaPossessionHost? _host;
    private PossessionContext? _context;
    private PossessionTarget? _target;
    private Control? _element;
    private PossessionLease? _lease;
    private IDisposable? _outline;
    private CancellationTokenSource? _cancel;
    private IFrameSource? _frames;
    private Rect _home;
    private double _sx, _sy;
    private Vector _offset, _from, _to;
    private TimeSpan? _start;
    private bool _busy, _returning;
    private int _count;
    private double _returnMs;
    private TaskCompletionSource? _returned;

    public bool CanApply(PossessionContext ctx, PossessionTarget? target) => CanApplyCore(ctx, target, false);

    private bool CanApplyCore(PossessionContext ctx, PossessionTarget? target, bool booked)
    {
        try
        {
            if (IsLive || ctx.Host is not IAvaloniaPossessionHost host || target?.Visual is not Control c || (!booked && target.IsLive) ||
                Array.IndexOf(TargetRoles, target.Role) < 0 || !c.IsEffectivelyVisible || !c.IsEffectivelyEnabled ||
                PossessionTree.IsOffLimits(c) || PossessionLease.IsLeased(c) || host.GhostLayer == null || host.PossessionWindow == null ||
                host.BoundsOnGhostLayer(c) is not { } home || !TryScale(c, host.GhostLayer, out _, out _)) return false;
            var region = AllowedRegion(host, c, home);
            return DodgeRegion.TryOffsetRange(home, region, out var minX, out var maxX, out var minY, out var maxY) &&
                   (maxX - minX >= 6 || maxY - minY >= 6);
        }
        catch { return false; }
    }

    public Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (!CanApplyCore(ctx, target, true) || ct.IsCancellationRequested) return Task.CompletedTask;
        var host = (IAvaloniaPossessionHost)ctx.Host;
        var el = (Control)target!.Visual;
        if (host.BoundsOnGhostLayer(el) is not { } home || !TryScale(el, host.GhostLayer!, out _sx, out _sy)) return Task.CompletedTask;
        _host = host; _context = ctx; _target = target; _element = el; _home = home;
        _cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _frames = host.PossessionFrames;
        IsLive = target.IsLive = true;
        host.PossessionPointer.Moved += OnMove;
        return Task.CompletedTask;
    }

    private async void OnMove(Point windowPoint, Vector velocity)
    {
        if (!IsLive || _busy || _returning || _count >= 3 || _host?.GhostLayer is not { } layer ||
            _host.PossessionWindow is not { } window || _element is not { } el || _context is not { } ctx || _cancel is not { } cancel) return;
        bool scheduled = false;
        try
        {
            var p = window.TranslatePoint(windowPoint, layer);
            if (p == null) return;
            var region = AllowedRegion(_host, el, _home);
            double amplitude = ctx.Photosafe ? 0.5 : 1;
            double side = (40 + ctx.Rng.NextDouble() * 20) * amplitude;
            double away = (40 + ctx.Rng.NextDouble() * 80) * amplitude;
            if (!DodgeRegion.TryStep(_home, region, _offset, p.Value, velocity, side, away, out _)) return;
            _busy = true;
            if (_lease == null)
            {
                await ctx.Attribution.ChargeAsync(el, cancel.Token).ConfigureAwait(true);
                cancel.Token.ThrowIfCancellationRequested();
                if (!IsLive || _returning) return;
                ctx.Name(Id, _target?.DisplayName);
                _outline = ctx.Attribution.Possess(el);
                _lease = PossessionLease.Take(el);
                if (_lease == null) return;
            }
            region = AllowedRegion(_host, el, _home);
            if (!DodgeRegion.TryStep(_home, region, _offset, p.Value, velocity, side, away, out var step)) return;
            _from = _offset; _to = step; _offset = step; _count++;
            _start = null;
            _frames!.Subscribe(OnFrame);
            scheduled = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Possession dodge failed"); }
        finally { if (!scheduled && ReferenceEquals(_cancel, cancel)) _busy = false; }
    }

    private void OnFrame(TimeSpan time)
    {
        _start ??= time;
        double elapsed = Math.Max(0, (time - _start.Value).TotalMilliseconds);
        double duration = _returning ? _returnMs : 260;
        double f = Math.Min(1, elapsed / duration);
        double k = _returning ? HeadEffect.CubicInOut(f) : HeadEffect.CubicOut(f);
        var value = _from + (_to - _from) * k;
        if (_lease != null) { _lease.Translate.X = value.X / _sx; _lease.Translate.Y = value.Y / _sy; }
        if (_returning && f >= 1) { Release(); return; }
        if (!_returning && elapsed >= 380) { _frames?.Unsubscribe(OnFrame); _busy = false; }
    }

    public Task UndoAsync(TimeSpan duration)
    {
        if (!IsLive) return Task.CompletedTask;
        try { _cancel?.Cancel(); } catch { }
        if (_host != null) _host.PossessionPointer.Moved -= OnMove;
        if (duration <= TimeSpan.Zero || _lease == null)
        {
            Release();
            return Task.CompletedTask;
        }
        if (_returning) return _returned?.Task ?? Task.CompletedTask;
        _returning = true;
        _from = new Vector(_lease.Translate.X * _sx, _lease.Translate.Y * _sy);
        _to = default;
        _returnMs = Math.Clamp(duration.TotalMilliseconds, 300, 800);
        _start = null;
        _returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _frames!.Subscribe(OnFrame);
        return _returned.Task;
    }

    private void Release()
    {
        _frames?.Unsubscribe(OnFrame);
        if (_host != null) _host.PossessionPointer.Moved -= OnMove;
        try { _lease?.ReleaseImmediate(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Possession dodge lease release failed"); }
        _lease = null;
        try { _outline?.Dispose(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Possession dodge outline release failed"); }
        _outline = null;
        if (_target != null) _target.IsLive = false;
        _cancel?.Dispose(); _cancel = null;
        IsLive = _busy = _returning = false;
        _host = null; _context = null; _target = null; _element = null; _frames = null;
        _count = 0; _offset = _from = _to = default; _start = null;
        var done = _returned; _returned = null; done?.TrySetResult();
    }

    private static bool TryScale(Control el, Visual layer, out double sx, out double sy)
    {
        sx = sy = 0;
        if (el.GetVisualParent()?.TransformToVisual(layer) is not { } m || Math.Abs(m.M12) > 0.001 || Math.Abs(m.M21) > 0.001 || m.M11 <= 0 || m.M22 <= 0) return false;
        sx = m.M11; sy = m.M22;
        return true;
    }

    // Conservative chrome bounds also stay inside the button's parent, so Avalonia can hit-test it.
    internal static Rect AllowedRegion(IAvaloniaPossessionHost host, Control el, Rect home)
    {
        var layer = host.GhostLayer;
        if (layer == null || layer.Bounds.Width <= 24 || layer.Bounds.Height <= 24) return default;
        double w = layer.Bounds.Width, h = layer.Bounds.Height;
        var region = new Rect(12, 12, w - 24, h - 24);
        var name = el.Name ?? "";
        bool chrome = name.Contains("close", StringComparison.OrdinalIgnoreCase) || name.Contains("minimi", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("maximi", StringComparison.OrdinalIgnoreCase) || name.Contains("titlebar", StringComparison.OrdinalIgnoreCase);
        Rect? title = null, parent = null;
        for (var node = el.GetVisualParent(); node != null; node = node.GetVisualParent())
        {
            if (node is not Control c || c.TransformToVisual(layer) is not { } m) continue;
            var bounds = new Rect(c.Bounds.Size).TransformToAABB(m);
            parent ??= bounds;
            if (c.Name?.Contains("titlebar", StringComparison.OrdinalIgnoreCase) == true) title = bounds;
            if (c.ClipToBounds)
            {
                if (DodgeRegion.IsStrip(bounds, w, h)) return default;
                region = region.Intersect(bounds);
            }
            if (c.Clip is { } clip)
            {
                var r = clip.Bounds.TransformToAABB(m);
                if (DodgeRegion.IsStrip(r, w, h)) return default;
                region = region.Intersect(r);
            }
            if (region.Width <= 0 || region.Height <= 0) return default;
        }
        if (chrome)
        {
            // Later siblings paint and hit-test above this button. Move only toward earlier siblings.
            double left = region.Left, right = Math.Min(region.Right, home.Right);
            if (title is { } t) { left = Math.Max(left, t.Left); right = Math.Min(right, t.Right); }
            if (parent is { } p) { left = Math.Max(left, p.Left); right = Math.Min(right, p.Right); }
            return right - left >= home.Width ? new Rect(left, home.Y, right - left, home.Height) : default;
        }
        return region;
    }
}
