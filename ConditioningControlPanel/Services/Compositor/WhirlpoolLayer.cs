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
    private double _sinceFrame;

    public WhirlpoolLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => CompositorLayers.Whirlpool;
    public override bool WorldSpacePx => true;
    protected override bool GentleFades => true;
    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => App.ShouldRenderTargetOnScreen(
               App.Settings?.Current?.SpiralTargetMonitor ?? App.MonitorTargetFollowGlobal,
               screenBoundsPx);
    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    // The pool wears Loom spirals (owner, 2026-11-15): a newly rolled one every so often, each
    // melting into the next, or the one saved Loom spiral the user pinned.
    private readonly ConditioningControlPanel.SuperCore.LoomMorph _morph = new(new Random());
    private double _phase;
    private string _pinKey = "";

    private void StepSpiral(double dt, bool showing)
    {
        try
        {
            var key = App.Settings?.Current?.WhirlpoolSpiral ?? "";
            if (key != _pinKey)
            {
                _pinKey = key;
                ConditioningControlPanel.SuperCore.LoomSpec? pin = null;
                if (key.Length > 0)
                    foreach (var (slug, _, json) in Services.Chaos.DtrhLoomStore.List())
                        if (slug == key) { pin = LoomSpecJson.Parse(json); break; }
                _morph.Pin(pin);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Whirlpool spiral pick: {E}", ex.Message); }
        if (!showing) return;                 // a pool nobody sees does not burn through spirals
        bool gentle = Safety.GentleVisuals.On;
        _morph.Hold = ConditioningControlPanel.SuperCore.WhirlpoolOptions.ClampHold(
            App.Settings?.Current?.WhirlpoolNewEvery ?? ConditioningControlPanel.SuperCore.WhirlpoolOptions.HoldDefault);
        _phase += dt * (gentle ? 0.06 : 0.12);
        _morph.Step(gentle ? dt * 0.6 : dt);
    }

    public void Show() { _dirty = true; SetActive(true); }
    public void Hide() { if (IsActive) { SetActive(false); _dirty = true; } }

    public override void OnActivated() { }
    public override void OnDeactivated() { }

    public override void Update(TimeSpan delta)
    {
        SuperStageHost.Step();
        _sinceFrame += Math.Min(delta.TotalSeconds, 0.1);
        var pool = SuperStageHost.Stage.Pool;
        StepSpiral(Math.Min(delta.TotalSeconds, 0.1), pool.Size > 0.004);
        // Nothing to draw while the mouse keeps the pool at nothing: no repaint either.
        if (pool.Size <= 0.004) { if (_sinceFrame > 0.5) { _sinceFrame = 0; _dirty = true; } return; }
        if (_sinceFrame >= 1 / 30.0) { _sinceFrame = 0; _dirty = true; }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        SuperStageHost.NoteScreen(new System.Drawing.Rectangle(boundsPx.Left, boundsPx.Top, boundsPx.Width, boundsPx.Height), dpiScale);
        var stage = SuperStageHost.Stage;
        if (!stage.PoolOn) return;
        double strength = Math.Clamp((App.Settings?.Current?.SpiralOpacity ?? 10) / 100.0, 0, 1);
        _draw.Draw(canvas, stage.Pool, strength, _morph.From, _morph.To, _morph.Mix, _phase);
    }
}
