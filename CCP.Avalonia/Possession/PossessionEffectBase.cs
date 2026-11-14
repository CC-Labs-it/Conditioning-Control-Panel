// WPF: Services/Possession/Effects/PossessionEffectBase.cs:27-225.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

/// <summary>Shared lifetime for non-transform effects. Property overrides are released on every exit.</summary>
internal abstract class PossessionEffectBase : IPossessionEffect
{
    public abstract string Id { get; }
    public abstract PossessionRung MinRung { get; }
    public virtual PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public virtual bool IsBig => false;
    public virtual bool UsesFlicker => false;
    public abstract double Weight { get; }
    public abstract TimeSpan HoldFor { get; }
    public virtual IReadOnlyList<PossessionRole> Roles => Array.Empty<PossessionRole>();
    public bool IsLive { get; private set; }
    protected PossessionContext? Context { get; private set; }
    protected PossessionTarget? Target { get; private set; }
    protected virtual bool ChargeOnApply => true;
    private CancellationTokenSource? _cts;
    private readonly List<IDisposable> _overrides = new();
    private IDisposable? _outline;
    private bool _charged, _named, _undoing;

    public bool CanApply(PossessionContext ctx, PossessionTarget? target)
    {
        try
        {
            if (IsLive || ctx == null || (ctx.Photosafe && UsesFlicker)) return false;
            if (Roles.Count == 0) return CanApplyCore(ctx, null);
            if (target == null || target.IsLive || !Roles.Contains(target.Role) || target.Visual is not Control c)
                return false;
            return c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0
                && !PossessionTree.IsOffLimits(c) && !PossessionLease.IsLeased(c)
                && CanApplyCore(ctx, target);
        }
        catch { return false; }
    }

    protected virtual bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => true;

    public async Task ApplyAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (IsLive) return;
        IsLive = true;
        Context = ctx;
        Target = Roles.Count == 0 ? null : target;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var source = _cts;
        var token = source.Token;
        try
        {
            if (Target != null) Target.IsLive = true;
            if (ChargeOnApply)
            {
                var element = Target?.Visual as Control ?? (ctx.Host as IAvaloniaPossessionHost)?.PossessionWindow?.Content as Control;
                if (element != null) await ChargeAndPossessAsync(element, token).ConfigureAwait(true);
                else NameOnce();
            }
            token.ThrowIfCancellationRequested();
            await ApplyCoreAsync(ctx, Target, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { if (!_undoing && ReferenceEquals(_cts, source)) await UndoAsync(TimeSpan.Zero).ConfigureAwait(true); }
        catch (Exception ex)
        {
            Serilog.Log.Warning("Possession {Id} failed to apply: {Error}", Id, ex.Message);
            if (ReferenceEquals(_cts, source)) await UndoAsync(TimeSpan.Zero).ConfigureAwait(true);
        }
    }

    protected async Task ChargeAndPossessAsync(Control element, CancellationToken ct)
    {
        if (_charged || Context == null) return;
        _charged = true;
        try { await Context.Attribution.ChargeAsync(element, ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Serilog.Log.Warning("Possession {Id} charge failed: {Error}", Id, ex.Message); }
        ct.ThrowIfCancellationRequested();
        NameOnce();
        try { _outline = Context?.Attribution.Possess(element); }
        catch (Exception ex) { Serilog.Log.Warning("Possession {Id} outline failed: {Error}", Id, ex.Message); }
    }

    protected void NameOnce(string? name = null)
    {
        if (_named || !IsBig || Context == null) return;
        _named = true;
        try { Context.Name(Id, string.IsNullOrWhiteSpace(name) ? Target?.DisplayName : name); }
        catch (Exception ex) { Serilog.Log.Warning("Possession {Id} name failed: {Error}", Id, ex.Message); }
    }

    protected abstract Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct);
    protected abstract Task UndoCoreAsync(TimeSpan duration);

    // Animation-priority values leave the original local value, style, or binding in place.
    protected void Override<T>(AvaloniaObject element, StyledProperty<T> property, T value)
    {
        var handle = element.SetValue(property, value, BindingPriority.Animation);
        if (handle != null) _overrides.Add(handle);
    }

    public async Task UndoAsync(TimeSpan duration)
    {
        if (!IsLive) return;
        if (_undoing)
        {
            if (duration <= TimeSpan.Zero)
            {
                try { await UndoCoreAsync(TimeSpan.Zero).ConfigureAwait(true); }
                catch (Exception ex) { Serilog.Log.Warning("Possession {Id} emergency undo failed: {Error}", Id, ex.Message); }
            }
            return;
        }
        _undoing = true;
        try { _cts?.Cancel(); } catch { }
        try { await UndoCoreAsync(duration).ConfigureAwait(true); }
        catch (Exception ex) { Serilog.Log.Warning("Possession {Id} undo failed: {Error}", Id, ex.Message); }
        for (int i = _overrides.Count - 1; i >= 0; i--) { try { _overrides[i].Dispose(); } catch { } }
        _overrides.Clear();
        try { _outline?.Dispose(); } catch { }
        _outline = null;
        if (Target != null) Target.IsLive = false;
        Target = null;
        Context = null;
        _charged = _named = false;
        _cts?.Dispose();
        _cts = null;
        IsLive = false;
        _undoing = false;
    }
}
