// WPF Services/Possession/Effects/SwapEffect.cs:136-194: partner geometry and preference.
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using Avalonia;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Possession;

// The partner score is independent of layout parent.
internal static class SwapPartnerRule
{
    internal static double Score(Rect origin, Rect candidate, bool originIsStartStop, bool candidateIsStartStop)
    {
        if (origin.Width <= 0 || origin.Height <= 0 || candidate.Width <= 0 || candidate.Height <= 0)
            return double.PositiveInfinity;
        double dx = candidate.Center.X - origin.Center.X;
        double dy = candidate.Center.Y - origin.Center.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(distance) || distance < 8 || distance > 220 ||
            Ratio(origin.Width, candidate.Width) > 1.6 || Ratio(origin.Height, candidate.Height) > 1.6)
            return double.PositiveInfinity;
        return distance - (originIsStartStop && candidateIsStartStop ? 10000 : 0);
    }

    private static double Ratio(double a, double b) => Math.Max(a, b) / Math.Min(a, b);
}

// WPF SwapEffect.cs:27-127. Both real controls keep their input and return through their leases.
internal sealed class SwapEffect : IPossessionEffect
{
    private static readonly PossessionRole[] ButtonRole = { PossessionRole.Button };
    private PossessionTarget? _first, _second;
    private PossessionLease? _a, _b;
    private IDisposable? _outlineA, _outlineB, _timer;
    private CancellationTokenSource? _cancel;
    private IFrameSource? _frames;
    private TaskCompletionSource? _glided, _returned;
    private TimeSpan? _start;
    private Vector _moveA, _moveB, _fromA, _fromB;
    private double _returnMs;
    private bool _returning;

    public string Id => "swap";
    public PossessionRung MinRung => PossessionRung.Drift;
    public PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
    public bool IsBig => true;
    public bool UsesFlicker => false;
    public double Weight => 4;
    public TimeSpan HoldFor => TimeSpan.FromSeconds(30);
    public IReadOnlyList<PossessionRole> Roles => ButtonRole;
    public bool IsLive { get; private set; }

    public bool CanApply(PossessionContext ctx, PossessionTarget? target)
    {
        try { return !IsLive && target != null && !target.IsLive && FindPartner(ctx, target) != null; }
        catch { return false; }
    }

    internal static PossessionTarget? FindPartner(
        PossessionContext ctx, PossessionTarget target)
    {
        if (ctx.Host is not IAvaloniaPossessionHost { GhostLayer: { } layer, PossessionWindow: not null } host ||
            target.Role != ButtonRole[0] || target.Visual is not Control first || !Eligible(first) ||
            host.BoundsOnGhostLayer(first) is not { } origin) return null;
        PossessionTarget? best = null;
        double bestScore = double.PositiveInfinity;
        foreach (var candidate in host.Targets)
        {
            if (ReferenceEquals(candidate, target) || candidate.IsLive || candidate.Role != ButtonRole[0] ||
                candidate.Visual is not Control other || ReferenceEquals(first, other) || !Eligible(other) ||
                host.BoundsOnGhostLayer(other) is not { } bounds) continue;
            var score = SwapPartnerRule.Score(origin, bounds, LooksStartStop(first), LooksStartStop(other));
            if (score >= bestScore || !PairMoves(layer, first, other, origin, bounds, out _, out _)) continue;
            best = candidate; bestScore = score;
        }
        return best;
    }

    private static bool Eligible(Control c)
    {
        var name = c.Name ?? "";
        return c.IsEffectivelyVisible && c.IsEffectivelyEnabled && c.IsHitTestVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0 &&
            !PossessionTree.IsOffLimits(c) && !PossessionLease.IsLeased(c) &&
            !name.Contains("close", StringComparison.OrdinalIgnoreCase) && !name.Contains("minimi", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("maximi", StringComparison.OrdinalIgnoreCase) && !name.Contains("titlebar", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksStartStop(Control c)
    {
        string? label = (c as ContentControl)?.Content as string;
        if (string.IsNullOrWhiteSpace(label))
            foreach (var child in c.GetVisualDescendants())
                if (child is TextBlock { Text: { } text } && !string.IsNullOrWhiteSpace(text)) { label = text; break; }
        if (RelabelEffect.IsStartOrStop(label)) return true;
        foreach (var name in new[] { c.Name, AutomationProperties.GetAutomationId(c), AutomationProperties.GetName(c) })
            if (name != null && (name.Contains("start", StringComparison.OrdinalIgnoreCase) || name.Contains("stop", StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    // Avalonia hit testing stops at ancestor bounds, even where drawing is not clipped.
    private static bool PairMoves(Canvas layer, Control a, Control b,
        Rect ra, Rect rb, out Vector moveA, out Vector moveB)
    {
        moveA = moveB = default;
        var delta = rb.Position - ra.Position;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return false;
        return MoveFor(a, layer, new Rect(rb.Position, ra.Size), delta, out moveA) &&
               MoveFor(b, layer, new Rect(ra.Position, rb.Size), -delta, out moveB);
    }

    private static bool MoveFor(Control element, Canvas layer, Rect landing, Vector delta, out Vector move)
    {
        move = default;
        var parent = element.GetVisualParent();
        if (parent?.TransformToVisual(layer) is not { } m || Math.Abs(m.M12) > .001 || Math.Abs(m.M21) > .001 || m.M11 <= 0 || m.M22 <= 0) return false;
        move = new Vector(delta.X / m.M11, delta.Y / m.M22);
        for (var node = parent; node != null; node = node.GetVisualParent())
        {
            if (node is Control parentControl && (!parentControl.IsHitTestVisible || !parentControl.IsEffectivelyEnabled)) return false;
            if (node.TransformToVisual(layer) is not { } transform) return false;
            var bounds = new Rect(node.Bounds.Size).TransformToAABB(transform);
            if (landing.Left < bounds.Left - .01 || landing.Top < bounds.Top - .01 ||
                landing.Right > bounds.Right + .01 || landing.Bottom > bounds.Bottom + .01) return false;
            if (node.Clip is { } clip)
            {
                if (clip is not global::Avalonia.Media.RectangleGeometry) return false;
                var r = clip.Bounds.TransformToAABB(transform);
                if (landing.Left < r.Left || landing.Top < r.Top || landing.Right > r.Right || landing.Bottom > r.Bottom) return false;
            }
        }
        return true;
    }

    public async Task ApplyAsync(PossessionContext ctx,
        PossessionTarget? target, CancellationToken ct)
    {
        if (IsLive || target == null || ct.IsCancellationRequested || FindPartner(ctx, target) is not { } partner ||
            ctx.Host is not IAvaloniaPossessionHost host || target.Visual is not Control first ||
            partner.Visual is not Control second) return;
        IsLive = target.IsLive = partner.IsLive = true;
        _first = target; _second = partner; _frames = host.PossessionFrames;
        var source = _cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            await Task.WhenAll(Charge(ctx, first, source.Token), Charge(ctx, second, source.Token)).ConfigureAwait(true);
            source.Token.ThrowIfCancellationRequested();
            if (!Eligible(first) || !Eligible(second) || host.GhostLayer is not { } layer ||
                host.BoundsOnGhostLayer(first) is not { } ra || host.BoundsOnGhostLayer(second) is not { } rb ||
                !double.IsFinite(SwapPartnerRule.Score(ra, rb, LooksStartStop(first), LooksStartStop(second))) ||
                !PairMoves(layer, first, second, ra, rb, out _moveA, out _moveB)) { Release(); return; }
            var name = string.Join(" and ", new[] { target.DisplayName, partner.DisplayName }.Where(n => !string.IsNullOrWhiteSpace(n)));
            try { ctx.Name(Id, string.IsNullOrWhiteSpace(name) ? "two buttons" : name); } catch { }
            _outlineA = ctx.Attribution.Possess(first); _outlineB = ctx.Attribution.Possess(second);
            _a = PossessionLease.Take(first); _b = PossessionLease.Take(second);
            if (_a == null || _b == null) { Release(); return; }
            _glided = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var glided = _glided;
            _frames!.Subscribe(Frame);
            _timer = DispatcherTimer.RunOnce(() => { if (ReferenceEquals(_glided, glided)) FinishGlide(); }, TimeSpan.FromMilliseconds(1550));
            using var registration = source.Token.Register(() => Dispatcher.UIThread.Post(() =>
            { if (ReferenceEquals(_cancel, source) && !_returning) Release(); }));
            await glided.Task.ConfigureAwait(true);
        }
        catch (OperationCanceledException) { if (ReferenceEquals(_cancel, source) && !_returning) Release(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Possession swap failed"); if (ReferenceEquals(_cancel, source)) Release(); }
    }

    private static async Task Charge(PossessionContext ctx,
        Control c, CancellationToken token)
    {
        try { await ctx.Attribution.ChargeAsync(c, token).ConfigureAwait(true); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Possession swap charge failed"); }
    }

    private void Frame(TimeSpan now)
    {
        _start ??= now;
        double f = Math.Clamp((now - _start.Value).TotalMilliseconds / (_returning ? _returnMs : 550), 0, 1);
        double k = HeadEffect.CubicInOut(f);
        if (_a != null) { var v = _returning ? _fromA * (1 - k) : _moveA * k; _a.Translate.X = v.X; _a.Translate.Y = v.Y; }
        if (_b != null) { var v = _returning ? _fromB * (1 - k) : _moveB * k; _b.Translate.X = v.X; _b.Translate.Y = v.Y; }
        if (f >= 1) { if (_returning) Release(); else FinishGlide(); }
    }

    private void FinishGlide()
    {
        if (_a != null) { _a.Translate.X = _moveA.X; _a.Translate.Y = _moveA.Y; }
        if (_b != null) { _b.Translate.X = _moveB.X; _b.Translate.Y = _moveB.Y; }
        _frames?.Unsubscribe(Frame); _timer?.Dispose(); _timer = null; _start = null;
        var done = _glided; _glided = null; done?.TrySetResult();
    }

    public Task UndoAsync(TimeSpan duration)
    {
        if (!IsLive) return Task.CompletedTask;
        try { _cancel?.Cancel(); } catch { }
        if (duration <= TimeSpan.Zero || _a == null || _b == null) { Release(); return Task.CompletedTask; }
        if (_returning) return _returned?.Task ?? Task.CompletedTask;
        _returning = true;
        _fromA = new Vector(_a.Translate.X, _a.Translate.Y); _fromB = new Vector(_b.Translate.X, _b.Translate.Y);
        _start = null; _returnMs = Math.Clamp(duration.TotalMilliseconds, 300, 900);
        _timer?.Dispose(); _frames?.Unsubscribe(Frame);
        _glided?.TrySetResult(); _glided = null;
        _returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = _returned;
        _frames?.Subscribe(Frame);
        _timer = DispatcherTimer.RunOnce(() => { if (ReferenceEquals(_returned, returned)) Release(); }, TimeSpan.FromMilliseconds(_returnMs + 1000));
        return returned.Task;
    }

    private void Release()
    {
        _frames?.Unsubscribe(Frame); _timer?.Dispose(); _timer = null;
        try { _a?.ReleaseImmediate(); } catch { } try { _b?.ReleaseImmediate(); } catch { }
        try { _outlineA?.Dispose(); } catch { } try { _outlineB?.Dispose(); } catch { }
        if (_first != null) _first.IsLive = false; if (_second != null) _second.IsLive = false;
        _a = _b = null; _outlineA = _outlineB = null; _first = _second = null;
        _cancel?.Dispose(); _cancel = null; _frames = null; _start = null; IsLive = _returning = false;
        var glided = _glided; _glided = null; glided?.TrySetResult();
        var returned = _returned; _returned = null; returned?.TrySetResult();
    }
}
