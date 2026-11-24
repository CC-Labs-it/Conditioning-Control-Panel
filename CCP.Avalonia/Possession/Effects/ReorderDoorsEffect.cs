// WPF Services/Possession/Effects/ReorderDoorsEffect.cs:18-155.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;

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
        public PossessionGhost? Ghost;
        public TranslateTransform? OverlayMove;
        public double LayerDistance;
        public Action? Unmirror;
    }
    private readonly List<Mover> _movers = new();
    private readonly List<IDisposable> _outlines = new();
    private readonly List<IDisposable> _unclips = new();
    private IFrameSource? _frames;
    private Canvas? _inputLayer;
    private LockdownService? _lockdown;
    private IDisposable? _timeout;
    private TimeSpan? _start, _refreshed;
    private bool _held;
    private TaskCompletionSource? _returned, _swapped;
    private CancellationTokenRegistration _cancellation;
    private int _generation;
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
    protected override string? NameOverride => Loc.Get("possession_reorder_doors");

    // WPF CollectDoors walks the host, including visible headers in different panels.
    private static List<(PossessionTarget Target, Control Door, Rect Bounds)> Doors(PossessionContext ctx, PossessionTarget? target)
    {
        var result = new List<(PossessionTarget, Control, Rect)>();
        if (ctx.Host is not IAvaloniaPossessionHost { GhostLayer: not null } host) return result;
        foreach (var candidate in host.Targets)
        {
            if (candidate.Role != PossessionRole.TabHeader || candidate.Visual is not Control door ||
                (candidate.IsLive && candidate != target) || !door.IsEffectivelyVisible ||
                !door.IsEffectivelyEnabled || !door.IsHitTestVisible || PossessionTree.IsOffLimits(door) ||
                PossessionLease.IsLeased(door) || host.BoundsOnGhostLayer(door) is not { } bounds ||
                bounds.Width <= 0 || bounds.Height <= 0) continue;
            result.Add((candidate, door, bounds));
        }
        result.Sort((a, b) => a.Item3.Y.CompareTo(b.Item3.Y));
        return result;
    }

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target) => Doors(ctx, target).Count >= 2;

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        if (ctx.Host is not IAvaloniaPossessionHost { GhostLayer: { } layer } host || target?.Visual is not Control selected || ct.IsCancellationRequested) return;
        Live.Add(this);
        _lockdown = LockdownService.Current;
        if (_lockdown != null) _lockdown.LockdownDeactivated += StopForLockdown;
        await ChargeAndPossessAsync(selected, ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        var doors = Doors(ctx, target);
        for (int i = 0; i + 1 < doors.Count && i < 6; i += 2)
        {
            var a = doors[i]; var b = doors[i + 1];
            double gap = b.Bounds.Y - a.Bounds.Y;
            if (gap < 2) continue;
            var delta = new Vector(0, gap);
            var landingA = new Rect(a.Bounds.X, b.Bounds.Y, a.Bounds.Width, a.Bounds.Height);
            var landingB = new Rect(b.Bounds.X, a.Bounds.Y, b.Bounds.Width, b.Bounds.Height);
            var unclip = new List<Visual>();
            if (!SwapEffect.MoveFor(a.Door, layer, landingA, delta, out var moveA, unclip) ||
                !SwapEffect.MoveFor(b.Door, layer, landingB, -delta, out var moveB, unclip)) continue;
            bool covered = !SwapEffect.OnTop(layer, a.Door, b.Door, landingA) ||
                !SwapEffect.OnTop(layer, b.Door, a.Door, landingB);
            PossessionGhost? ga = null, gb = null;
            IInvokeProvider? invokeA = null, invokeB = null;
            if (covered)
            {
                // The shell's doors are buttons. Their automation peers invoke the original Click and
                // command, while the snapshot and input seat travel above the obstructing parents.
                if (a.Door is not Button || b.Door is not Button || layer.GetVisualParent() is not Grid root ||
                    ControlAutomationPeer.CreatePeerForElement(a.Door) is not IInvokeProvider pa ||
                    ControlAutomationPeer.CreatePeerForElement(b.Door) is not IInvokeProvider pb) continue;
                try
                {
                    ga = PossessionGhost.Capture(a.Door, layer);
                    gb = PossessionGhost.Capture(b.Door, layer);
                    if (ga == null || gb == null) { ga?.Dispose(); gb?.Dispose(); continue; }
                    invokeA = pa; invokeB = pb;
                    if (_inputLayer == null)
                    {
                        _inputLayer = new Canvas { Background = null, ClipToBounds = false, ZIndex = layer.ZIndex + 1 };
                        Grid.SetRow(_inputLayer, Grid.GetRow(layer)); Grid.SetColumn(_inputLayer, Grid.GetColumn(layer));
                        Grid.SetRowSpan(_inputLayer, Grid.GetRowSpan(layer)); Grid.SetColumnSpan(_inputLayer, Grid.GetColumnSpan(layer));
                        root.Children.Add(_inputLayer);
                    }
                }
                catch (Exception ex)
                {
                    try { ga?.Dispose(); } catch { }
                    try { gb?.Dispose(); } catch { }
                    if (_inputLayer?.GetVisualParent() == null) _inputLayer = null;
                    Serilog.Log.Warning(ex, "Possession reorderdoors snapshot failed");
                    continue;
                }
            }
            var la = PossessionLease.Take(a.Door);
            var lb = PossessionLease.Take(b.Door);
            if (la == null || lb == null) { la?.ReleaseImmediate(); lb?.ReleaseImmediate(); ga?.Dispose(); gb?.Dispose(); continue; }
            var ma = new Mover { Target = a.Target, Lease = la, Distance = moveA.Y, LayerDistance = gap, Ghost = ga };
            var mb = new Mover { Target = b.Target, Lease = lb, Distance = moveB.Y, LayerDistance = -gap, Ghost = gb };
            _movers.Add(ma); _movers.Add(mb);
            if (covered)
            {
                AddInput(ma, a.Door, a.Bounds, invokeA!);
                AddInput(mb, b.Door, b.Bounds, invokeB!);
                ga!.Hide(true); gb!.Hide(true);
            }
            foreach (var node in unclip)
                if (node.SetValue(Visual.ClipToBoundsProperty, false, global::Avalonia.Data.BindingPriority.Animation) is { } hold) _unclips.Add(hold);
            a.Target.IsLive = b.Target.IsLive = true;
            if (a.Target != target) _outlines.Add(ctx.Attribution.Possess(a.Door));
            if (b.Target != target) _outlines.Add(ctx.Attribution.Possess(b.Door));
        }
        if (_movers.Count == 0) { await UndoAsync(TimeSpan.Zero); return; }
        _frames = host.PossessionFrames;
        var swapped = _swapped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int generation = ++_generation;
        _cancellation = ct.Register(() => Dispatcher.UIThread.Post(() =>
        { if (generation == _generation && IsLive && !_returning) _ = UndoAsync(TimeSpan.Zero); }));
        _frames.Subscribe(Frame);
        _timeout = DispatcherTimer.RunOnce(CompleteSwap, TimeSpan.FromMilliseconds(520 + (_movers.Count / 2 - 1) * 90 + 1000));
        await swapped.Task.ConfigureAwait(true);

    }

    private void AddInput(Mover mover, Control door, Rect bounds, IInvokeProvider invoke)
    {
        var move = mover.OverlayMove = new TranslateTransform();
        mover.Ghost!.Image.RenderTransform = move;
        var seat = new Button
        {
            Width = bounds.Width, Height = bounds.Height, Focusable = false, Padding = new Thickness(0),
            RenderTransform = move,
            Template = new FuncControlTemplate<Button>((_, _) => new Border { Background = Brushes.Transparent })
        };
        bool Usable() => IsLive && door.IsEffectivelyVisible && door.IsEffectivelyEnabled && !PossessionTree.IsOffLimits(door);
        // WPF presses the real door, so the rail's misroute hook sees it first (MainWindow.NavRail.cs:532).
        seat.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            try { if (Usable() && TopLevel.GetTopLevel(door) is MainShellWindow shell) shell.RouteDoorPress(door, e); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession reorderdoors reroute failed"); }
        }, RoutingStrategies.Tunnel);
        seat.Click += (_, _) =>
        {
            try { if (Usable()) invoke.Invoke(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession reorderdoors input failed"); }
            Dispatcher.UIThread.Post(() => mover.Ghost?.Refresh(), DispatcherPriority.Background);
        };
        // The door keeps its own menu, tooltip, cursor and hover look while the seat stands in for it.
        seat.ContextRequested += (_, e) =>
        {
            try
            {
                if (!Usable()) return;
                if (door.ContextMenu is { } menu) { menu.Open(door); e.Handled = true; }
                else if (door.ContextFlyout is { } flyout) { flyout.ShowAt(door); e.Handled = true; }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession reorderdoors menu failed"); }
        };
        var tip = seat.Bind(ToolTip.TipProperty, door.GetObservable(ToolTip.TipProperty));
        var cursor = seat.Bind(InputElement.CursorProperty, door.GetObservable(InputElement.CursorProperty));
        var states = (IPseudoClasses)door.Classes;
        void Mirror(bool over, bool pressed)
        {
            states.Set(":pointerover", over); states.Set(":pressed", pressed);
            try { mover.Ghost?.Refresh(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Possession reorderdoors refresh failed"); }
        }
        seat.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsPointerOverProperty || e.Property == Button.IsPressedProperty)
                Mirror(seat.IsPointerOver, seat.IsPressed);
        };
        mover.Unmirror = () =>
        {
            tip.Dispose(); cursor.Dispose();
            states.Set(":pointerover", door.IsPointerOver); states.Set(":pressed", door is Button { IsPressed: true });
        };
        Canvas.SetLeft(seat, bounds.X); Canvas.SetTop(seat, bounds.Y);
        _inputLayer!.Children.Add(seat);
    }

    private void Frame(TimeSpan now)
    {
        // A covered door's stand-in follows its live highlight and badges for the whole hold.
        if (_refreshed is not { } last || now - last >= TimeSpan.FromMilliseconds(100))
        {
            _refreshed = now;
            foreach (var mover in _movers)
                try { mover.Ghost?.Refresh(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Possession reorderdoors refresh failed"); }
        }
        if (_held) return;
        _start ??= now;
        double elapsed = (now - _start.Value).TotalMilliseconds;
        for (int i = 0; i < _movers.Count; i++)
        {
            var mover = _movers[i];
            double duration = _returning ? _returnMs : 520;
            double f = Math.Clamp((elapsed - (_returning ? 0 : i / 2 * 90)) / duration, 0, 1);
            double k = HeadEffect.CubicInOut(f);
            mover.Lease.Translate.Y = _returning ? mover.From * (1 - k) : mover.Distance * k;
            if (mover.OverlayMove != null)
                mover.OverlayMove.Y = mover.Distance == 0 ? 0 : mover.LayerDistance * mover.Lease.Translate.Y / mover.Distance;
        }
        if (_returning && elapsed >= _returnMs) Finish();
        else if (!_returning && elapsed >= 520 + (_movers.Count / 2 - 1) * 90) CompleteSwap();
    }

    private void CompleteSwap()
    {
        foreach (var mover in _movers)
        {
            mover.Lease.Translate.Y = mover.Distance;
            if (mover.OverlayMove != null) mover.OverlayMove.Y = mover.LayerDistance;
        }
        _held = true;
        if (!_movers.Exists(m => m.Ghost != null)) Park();
        _timeout?.Dispose(); _timeout = null; _start = null;
        var done = _swapped; _swapped = null; done?.TrySetResult();
    }

    protected override async Task UndoCoreAsync(TimeSpan duration)
    {
        _swapped?.TrySetResult(); _swapped = null;
        if (_movers.Count == 0 || duration <= TimeSpan.Zero) { Finish(); return; }
        _returning = true; _held = false;
        _timeout?.Dispose(); _timeout = null;
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
        ++_generation;
        Park(); _timeout?.Dispose(); _timeout = null; _cancellation.Dispose();
        _swapped?.TrySetResult(); _swapped = null;
        if (_lockdown != null) _lockdown.LockdownDeactivated -= StopForLockdown;
        _lockdown = null;
        if (_inputLayer?.GetVisualParent() is Panel parent) parent.Children.Remove(_inputLayer);
        _inputLayer = null;
        foreach (var mover in _movers)
        {
            try { mover.Unmirror?.Invoke(); } catch { }
            try { mover.Ghost?.Dispose(); } catch { }
            try { mover.Lease.ReleaseImmediate(); } catch { }
            mover.Target.IsLive = false;
        }
        foreach (var outline in _outlines) try { outline.Dispose(); } catch { }
        foreach (var hold in _unclips) try { hold.Dispose(); } catch { }
        _movers.Clear(); _outlines.Clear(); _unclips.Clear(); Live.Remove(this);
        _frames = null; _start = _refreshed = null; _returning = _held = false;
        var done = _returned; _returned = null; done?.TrySetResult();
    }
    internal static void UndoAllLive()
    {
        foreach (var effect in Live.ToArray()) try { _ = effect.UndoAsync(TimeSpan.Zero); } catch { }
    }
}
