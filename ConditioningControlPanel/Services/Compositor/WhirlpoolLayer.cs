using System;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// The Whirlpool: the spiral's Super look. A spiral that sits under the cursor, grows while the
/// mouse rests and shrinks while it moves. The pool itself is
/// <see cref="ConditioningControlPanel.SuperCore.Whirlpool"/> (CCP.Core) on the shared stage;
/// this layer steps the stage and draws it. Drawn over the fog so its arms read through it.
/// </summary>
public sealed class WhirlpoolLayer : BaseLayer
{
    private readonly WhirlpoolDraw _draw = new();
    private bool _dirty = true;

    public WhirlpoolLayer(CompositorEngine engine) : base(engine.Wake) { }

    public override int ZIndex => CompositorLayers.Whirlpool;
    public override bool WorldSpacePx => true;
    protected override bool GentleFades => true;
    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => App.ShouldRenderTargetOnScreen(
               App.Settings?.Current?.SpiralTargetMonitor ?? App.MonitorTargetFollowGlobal,
               screenBoundsPx);
    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    // The pool wears Loom spirals (owner, 2026-11-15); what it wears and when it repaints is Core's.
    private readonly ConditioningControlPanel.SuperCore.WhirlpoolLook _look = new(new Random());

    public void Show() { _dirty = true; SetActive(true); }
    public void Hide() { if (IsActive) { SetActive(false); _dirty = true; } }

    public override void OnActivated() { }
    public override void OnDeactivated() { }

    public override void Update(TimeSpan delta)
    {
        SuperStageHost.Step();
        double dt = Math.Min(delta.TotalSeconds, 0.1);
        var pool = SuperStageHost.Stage.Pool;
        var set = App.Settings?.Current;
        _look.Step(dt, pool.Size > ConditioningControlPanel.SuperCore.WhirlpoolLook.NothingSize, Safety.GentleVisuals.On,
            set?.WhirlpoolSpiral, set?.WhirlpoolNewEvery ?? ConditioningControlPanel.SuperCore.WhirlpoolOptions.HoldDefault,
            ConditioningControlPanel.SuperCore.WhirlpoolLook.FindSaved);
        if (_look.Due(dt, pool.Size)) _dirty = true;
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        SuperStageHost.NoteScreen(new System.Drawing.Rectangle(boundsPx.Left, boundsPx.Top, boundsPx.Width, boundsPx.Height), dpiScale);
        var stage = SuperStageHost.Stage;
        if (!stage.PoolOn) return;
        // A monitor the pool does not reach has nothing to draw.
        if (!ConditioningControlPanel.SuperCore.WhirlpoolLook.Reaches(stage.Pool, boundsPx.Left, boundsPx.Top, boundsPx.Right, boundsPx.Bottom)) return;
        double strength = ConditioningControlPanel.SuperCore.WhirlpoolLook.Strength(App.Settings?.Current?.SpiralOpacity ?? 10);
        _draw.Draw(canvas, stage.Pool, strength, _look.Morph.From, _look.Morph.To, _look.Morph.Mix, _look.Phase);
    }
}
