// WPF Services/Possession/Effects/ScrollHijackEffect.cs:23-142.
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

internal sealed class ScrollHijackEffect : PossessionEffectBase
{
    private static readonly PossessionRole[] ScrollRole = { PossessionRole.Scroll };
    private ScrollViewer? _viewer;
    private IFrameSource? _frames;
    private TimeSpan? _start;
    private TaskCompletionSource? _completion;
    private double _home, _lastSet;
    public override string Id => "scrollhijack";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override bool IsBig => true;
    protected override string? NameOverride => ConditioningControlPanel.Localization.Loc.Get("possession_target_page");
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(3);
    public override IReadOnlyList<PossessionRole> Roles => ScrollRole;

    private static ScrollViewer? Resolve(object? visual) => visual is ScrollViewer sv ? sv :
        (visual as Visual)?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    protected override bool CanApplyCore(PossessionContext ctx, PossessionTarget? target)
    {
        var sv = Resolve(target?.Visual);
        return ctx.Host is IAvaloniaPossessionHost && sv != null && sv.Extent.Height - sv.Viewport.Height - sv.Offset.Y >= 40;
    }

    protected override async Task ApplyCoreAsync(PossessionContext ctx, PossessionTarget? target, CancellationToken ct)
    {
        _viewer = Resolve(target?.Visual);
        _frames = (ctx.Host as IAvaloniaPossessionHost)?.PossessionFrames;
        if (_viewer != null && _frames != null)
        {
            _home = _lastSet = _viewer.Offset.Y;
            _start = null;
            _completion = new TaskCompletionSource();
            _frames.Subscribe(Frame);
            await Task.WhenAny(_completion.Task, Task.Delay(1200, ct)).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            _frames?.Unsubscribe(Frame);
            _frames = null;
            AdoptUserScroll();
            SetOffset(_home);
            _completion = null;
        }
    }

    private void AdoptUserScroll()
    {
        if (_viewer == null) return;
        var drift = _viewer.Offset.Y - _lastSet;
        if (Math.Abs(drift) > 2) _home += drift;
    }

    private void Frame(TimeSpan now)
    {
        if (_viewer == null) return;
        _start ??= now;
        var ms = (now - _start.Value).TotalMilliseconds;
        AdoptUserScroll();
        double t = Math.Clamp(ms / 600, 0, 2);
        double leg = t <= 1 ? t : 2 - t;
        double eased = leg < 0.5 ? 4 * leg * leg * leg : 1 - Math.Pow(-2 * leg + 2, 3) / 2;
        SetOffset(_home + 40 * eased);
        if (ms >= 1200) { _frames?.Unsubscribe(Frame); _completion?.TrySetResult(); }
    }

    private void SetOffset(double y)
    {
        if (_viewer == null) return;
        y = Math.Clamp(y, 0, Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height));
        _viewer.Offset = new Vector(_viewer.Offset.X, y);
        _lastSet = _viewer.Offset.Y;
    }

    protected override Task UndoCoreAsync(TimeSpan duration)
    {
        _frames?.Unsubscribe(Frame);
        _frames = null;
        _completion = null;
        if (_viewer != null) { AdoptUserScroll(); SetOffset(_home); }
        _viewer = null;
        return Task.CompletedTask;
    }
}
