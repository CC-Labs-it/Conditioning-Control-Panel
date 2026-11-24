// WPF Services/Possession/Effects/ReorderDoorsEffect.cs:18-155.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class ReorderDoorsEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] DoorRoles = { PossessionRole.TabHeader };
    private static readonly List<ReorderDoorsEffect> Live = new();
    private sealed class Mover
    {
        public required PossessionTarget Target;
        public required PossessionLease Lease;
        public required double Distance;
        public double From;
    }
    private readonly List<Mover> _movers = new();
    private readonly List<IDisposable> _outlines = new();
    private IFrameSource? _frames;
    private LockdownService? _lockdown;
    private IDisposable? _timeout;
    private TimeSpan? _start;
    private TaskCompletionSource? _returned;
    private bool _returning;
    private double _returnMs;

    public override string Id => "reorderdoors";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(10);
    public override IReadOnlyList<PossessionRole> Roles => DoorRoles;
    protected override bool ChargeOnApply => false;

    // Only pair doors in the same rail panel, so the translated controls still receive clicks.
    private static List<(PossessionTarget Target, Control Door, double Top, double Scale)> Doors(
        PossessionContext ctx, PossessionTarget? target)
    {
        var result = new List<(PossessionTarget, Control, double, double)>();
        if (ctx.Host is not IAvaloniaPossessionHost { GhostLayer: { } layer } host ||
            target?.Visual is not Button selected || selected.GetVisualParent() is not StackPanel parent ||
            parent.Name != "NavSectionRows") return result;
        foreach (var candidate in host.Targets)
        {
            if (candidate.Role != PossessionRole.TabHeader || candidate.Visual is not Button door ||
                door.GetVisualParent() != parent || (candidate.IsLive && candidate != target) ||
                !door.IsEffectivelyVisible || !door.IsEffectivelyEnabled || !door.IsHitTestVisible ||
                PossessionTree.IsOffLimits(door) || PossessionLease.IsLeased(door) ||
                host.BoundsOnGhostLayer(door) is not { } bounds || bounds.Height <= 0 ||
                parent.TransformToVisual(layer) is not { } matrix || matrix.M22 <= 0.0001 ||
                Math.Abs(matrix.M12) > 0.001 || Math.Abs(matrix.M21) > 0.001) continue;
            result.Add((candidate, door, bounds.Y, matrix.M22));
        }
        result.Sort((a, b) => a.Item3.CompareTo(b.Item3));
        return result;
    }

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => Doors(ctx, target).Count >= 2;

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host || target?.Visual is not Control selected || ct.IsCancellationRequested) return;
        Live.Add(this);
        _lockdown = LockdownService.Current;
        if (_lockdown != null) _lockdown.LockdownDeactivated += StopForLockdown;
        await ChargeAndPossessAsync(selected, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        var doors = Doors(ctx, target);
        for (int i = 0; i + 1 < doors.Count && i < 6; i += 2)
        {
            var a = doors[i]; var b = doors[i + 1];
            double gap = b.Top - a.Top;
            if (gap < 2) continue;
            var la = PossessionLease.Take(a.Door);
            var lb = PossessionLease.Take(b.Door);
            if (la == null || lb == null) { la?.ReleaseImmediate(); lb?.ReleaseImmediate(); continue; }
            _movers.Add(new Mover { Target = a.Target, Lease = la, Distance = gap / a.Scale });
            _movers.Add(new Mover { Target = b.Target, Lease = lb, Distance = -gap / b.Scale });
            a.Target.IsLive = b.Target.IsLive = true;
            if (a.Target != target) _outlines.Add(ctx.Attribution.Possess(a.Door));
            if (b.Target != target) _outlines.Add(ctx.Attribution.Possess(b.Door));
        }
        if (_movers.Count == 0) { await UndoAsync(TimeSpan.Zero); return; }
        _frames = host.PossessionFrames;
        _frames.Subscribe(Frame);
    }

    private void Frame(TimeSpan now)
    {
        _start ??= now;
        double elapsed = (now - _start.Value).TotalMilliseconds;
        for (int i = 0; i < _movers.Count; i++)
        {
            var mover = _movers[i];
            double duration = _returning ? _returnMs : 520;
            double f = Math.Clamp((elapsed - (_returning ? 0 : i / 2 * 90)) / duration, 0, 1);
            double k = HeadEffect.CubicInOut(f);
            mover.Lease.Translate.Y = _returning ? mover.From * (1 - k) : mover.Distance * k;
        }
        if (_returning && elapsed >= _returnMs) Finish();
        else if (!_returning && elapsed >= 520 + (_movers.Count / 2 - 1) * 90) Park();
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        if (_movers.Count == 0 || duration <= TimeSpan.Zero) { Finish(); return; }
        _returning = true;
        _returnMs = Math.Clamp(duration.TotalMilliseconds, 260, 520);
        foreach (var mover in _movers) mover.From = mover.Lease.Translate.Y;
        _start = null;
        _frames?.Unsubscribe(Frame);
        _frames?.Subscribe(Frame);
        var returned = _returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _timeout = DispatcherTimer.RunOnce(Finish, TimeSpan.FromMilliseconds(_returnMs + 100));
        await returned.Task.ConfigureAwait(true);
    }

    private void StopForLockdown()
    {
        if (Dispatcher.UIThread.CheckAccess()) _ = UndoAsync(TimeSpan.Zero);
        else Dispatcher.UIThread.Post(() => _ = UndoAsync(TimeSpan.Zero));
    }
    private void Park() => _frames?.Unsubscribe(Frame);
    private void Finish()
    {
        Park(); _timeout?.Dispose(); _timeout = null;
        if (_lockdown != null) _lockdown.LockdownDeactivated -= StopForLockdown;
        _lockdown = null;
        foreach (var mover in _movers) { try { mover.Lease.ReleaseImmediate(); } catch { } mover.Target.IsLive = false; }
        foreach (var outline in _outlines) try { outline.Dispose(); } catch { }
        _movers.Clear(); _outlines.Clear(); Live.Remove(this);
        _frames = null; _start = null; _returning = false;
        var done = _returned; _returned = null; done?.TrySetResult();
    }
    internal static void UndoAllLive()
    {
        foreach (var effect in Live.ToArray()) try { _ = effect.UndoAsync(TimeSpan.Zero); } catch { }
    }
}
