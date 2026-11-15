// WPF: Services/Possession/Effects/ReorderDoorsEffect.cs:18-145.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class ReorderDoorsEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] DoorRoles = { PossessionRole.TabHeader };
    private static readonly HashSet<ReorderDoorsEffect> Live = new();
    private readonly List<(PossessionLease Lease, double Destination)> _movers = new();
    private readonly List<IDisposable> _outlines = new();
    private IFrameSource? _frames;
    private Action<TimeSpan>? _frame;
    private TaskCompletionSource<bool>? _animation;
    private int _generation;

    public override string Id => "reorderdoors";
    public override PossessionRung MinRung => PossessionRung.Collapse;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(10);
    public override IReadOnlyList<PossessionRole> Roles => DoorRoles;
    protected override string? NameOverride => "the doors";

    // Only the main rail: other TabHeader controls and the separately pinned Settings door
    // do not share its coordinate space. Lockdown is never a candidate.
    private static List<(Control Door, double Top)> Doors(PossessionContext ctx, PossessionTarget? selected = null)
    {
        var result = new List<(Control, double)>();
        foreach (var target in ctx.Host.Targets)
        {
            if (target.Role != PossessionRole.TabHeader || target.Visual is not Control door || target.IsLive && !ReferenceEquals(target, selected)) continue;
            if (door.GetVisualParent() is not StackPanel { Name: "NavSectionRows" } rail ||
                !door.IsEffectivelyVisible || !door.IsEffectivelyEnabled || !door.IsHitTestVisible ||
                PossessionTree.IsOffLimits(door) || PossessionLease.IsLeased(door) ||
                door.Tag is not string tag || tag.Equals("lockdown", StringComparison.OrdinalIgnoreCase) ||
                door.Bounds.Width <= 0 || door.Bounds.Height <= 0 || door.TransformToVisual(rail) is not { } matrix) continue;
            var bounds = new Rect(door.Bounds.Size).TransformToAABB(matrix);
            if (double.IsFinite(bounds.Y)) result.Add((door, bounds.Y));
        }
        result.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return result;
    }

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) =>
        ctx.Host is IAvaloniaPossessionHost && target != null && Doors(ctx).Any(d => ReferenceEquals(d.Door, target.Visual)) && Doors(ctx).Count >= 2;

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost host) return;
        var doors = Doors(ctx, target);
        Live.Add(this);
        for (int i = 0; i + 1 < doors.Count && i < 6; i += 2)
        {
            var a = doors[i]; var b = doors[i + 1];
            double gap = b.Top - a.Top;
            if (gap < 2) continue;
            var la = PossessionLease.Take(a.Door);
            if (la == null) continue;
            var lb = PossessionLease.Take(b.Door);
            if (lb == null) { la.ReleaseImmediate(); continue; }
            _movers.Add((la, gap)); _movers.Add((lb, -gap));
            foreach (var door in new[] { a.Door, b.Door })
                if (!ReferenceEquals(door, target?.Visual))
                    try { _outlines.Add(ctx.Attribution.Possess(door)); } catch { }
        }
        if (_movers.Count == 0) return;
        _frames = host.PossessionFrames;
        await AnimateAsync(false, 520, ct).ConfigureAwait(true);
    }

    private async Task AnimateAsync(bool returning, double milliseconds, CancellationToken ct)
    {
        StopAnimation();
        int generation = _generation;
        var from = _movers.Select(m => m.Lease.Translate.Y).ToArray();
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _animation = done;
        TimeSpan? start = null;
        _frame = now =>
        {
            start ??= now;
            double elapsed = (now - start.Value).TotalMilliseconds;
            for (int i = 0; i < _movers.Count; i++)
            {
                double delay = returning ? 0 : (i / 2) * 90;
                double t = Math.Clamp((elapsed - delay) / milliseconds, 0, 1);
                double ease = t * t * (3 - 2 * t);
                double end = returning ? 0 : _movers[i].Destination;
                _movers[i].Lease.Translate.Y = from[i] + (end - from[i]) * ease;
            }
            if (elapsed >= milliseconds + (returning ? 0 : ((_movers.Count - 1) / 2) * 90)) done.TrySetResult(true);
        };
        _frames?.Subscribe(_frame);
        try
        {
            // A minimized window may stop producing frames. Do not strand the director's pick slot.
            await Task.WhenAny(done.Task, Task.Delay(TimeSpan.FromSeconds(2), ct)).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (generation == _generation)
                foreach (var m in _movers) m.Lease.Translate.Y = returning ? 0 : m.Destination;
        }
        finally { if (generation == _generation) StopAnimation(); }
    }

    private void StopAnimation()
    {
        _generation++;
        if (_frame != null) _frames?.Unsubscribe(_frame);
        _frame = null;
        _animation?.TrySetResult(true);
        _animation = null;
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        StopAnimation();
        if (duration > TimeSpan.Zero && _movers.Count > 0)
            await AnimateAsync(true, Math.Clamp(duration.TotalMilliseconds, 260, 520), CancellationToken.None).ConfigureAwait(true);
        StopAnimation();
        foreach (var mover in _movers) mover.Lease.ReleaseImmediate();
        _movers.Clear();
        foreach (var outline in _outlines) { try { outline.Dispose(); } catch { } }
        _outlines.Clear();
        _frames = null;
        Live.Remove(this);
    }

    // The director releases its ledger entry before awaiting a glide; panic must also find that glide.
    internal static void UndoAllLive()
    {
        foreach (var effect in Live.ToArray()) _ = effect.UndoAsync(TimeSpan.Zero);
    }
}
