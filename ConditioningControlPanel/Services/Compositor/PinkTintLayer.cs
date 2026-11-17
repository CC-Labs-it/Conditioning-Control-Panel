using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the pink-filter windows: a fullscreen solid tint whose opacity rides in
/// the color's alpha channel, exactly like the legacy Border/SolidColorBrush (OverlayService
/// owns all opacity math - ramps, pulses, holds - and just pushes the final value here).
/// </summary>
public class PinkTintLayer : BaseLayer
{
    private byte _r = 255, _g = 105, _b = 180;
    private double _opacity;
    private bool _dirty = true;   // #550: a steady tint is static - only repaint when it changes
    private readonly SKPaint _paint = new();
    /// <summary>The colour and strength the pink filter was last told to show. Steam (its Super look) wears them.</summary>
    internal static (byte R, byte G, byte B, double Opacity) LastLook { get; private set; } = (255, 105, 180, 0.2);

    private readonly CompositorEngine _engine;
    private int _sortedZ;

    /// <summary>The pink filter's own tint wears the shop's fog colour (<see cref="Prizes.WornFog"/>).
    /// Off for other users of the layer (the welcome show paints its own pink).</summary>
    internal bool WearsLooks { get; init; }
    private long _wornStamp;

    public PinkTintLayer(CompositorEngine engine) : base(engine)
    {
        _engine = engine;
        _sortedZ = ZIndex;   // RegisterLayer sorts on the value read right after this
    }

    /// <summary>Over every session effect (today's look), or under spirals, subliminals, bubbles and
    /// bouncing text when the user asks for it (#1546). Read live; <see cref="SyncOrder"/> re-sorts.</summary>
    public override int ZIndex => ZFor(App.Settings?.Current?.PinkFilterUnderEffects == true);

    internal static int ZFor(bool underEffects)
        => underEffects ? CompositorLayers.PinkTintUnder : CompositorLayers.PinkTint;

    /// <summary>Re-sort the engine when the setting moved the slot since the last sort. Cheap when
    /// nothing changed; called on every show and by the feature page toggle. UI thread.</summary>
    public void SyncOrder()
    {
        int z = ZIndex;
        if (z == _sortedZ) return;
        _sortedZ = z;
        _engine.ResortLayers();
        _dirty = true;   // repaint the surface in the new order
    }

    protected override bool GentleFades => true;   // gentle visuals: tint fades in and out

    // Fullscreen fill: honor the pink filter's per-effect monitor target (suggestion #639), which
    // itself falls back to DualMonitorEnabled when set to -1. The compositor hosts every screen,
    // so a non-targeted monitor just records an empty (transparent) frame for this layer.
    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => App.ShouldRenderTargetOnScreen(
               App.Settings?.Current?.PinkFilterTargetMonitor ?? App.MonitorTargetFollowGlobal,
               screenBoundsPx);

    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    /// <summary>Show the tint (or retarget a visible one). Opacity 0..1. UI thread.</summary>
    public void Show(byte r, byte g, byte b, double opacity)
    {
        Set(r, g, b, opacity);
        SyncOrder();              // a setting changed since the last show (cloud restore, preset) takes effect here
        _dirty = true;            // guarantee a paint on (re)show even if the values were unchanged
        SetActive(true);
    }

    /// <summary>Update color + opacity without changing visibility. UI thread.</summary>
    public void Set(byte r, byte g, byte b, double opacity)
    {
        var o = Math.Clamp(opacity, 0.0, 1.0);
        if (r == _r && g == _g && b == _b && o == _opacity) return;  // no visible change
        _r = r; _g = g; _b = b; _opacity = o;
        LastLook = (r, g, b, o);
        _dirty = true;
    }

    public void Hide() => SetActive(false);

    // Super Creep picked as "Super only": the tint steps aside behind a veil while the fog runs.
    // OverlayService pushes the decision (UI thread); the veil eases on the engine tick.
    private volatile bool _stepAside;
    private double _veil = 1;

    /// <summary>Hide the tint for Super only (true) or bring it back. A layer not on screen snaps, so a fresh show never flashes. UI thread.</summary>
    public void SetStepAside(bool on)
    {
        if (!IsActive) _veil = on ? 0 : 1;
        if (_stepAside == on) return;   // the 500 ms reconciler pushes every tick: no repaint for nothing
        _stepAside = on;
        _dirty = true;
    }

    public override void Update(TimeSpan delta)
    {
        if (WearsLooks)
        {
            // A worn fog colour changed, or is still cross-fading in: repaint.
            long stamp = Prizes.WornFog.Stamp;
            if (stamp != _wornStamp || Prizes.WornFog.Easing) { _wornStamp = stamp; _dirty = true; }
        }
        double target = _stepAside ? 0 : 1;
        if (_veil == target) return;
        _veil = Super.SuperStepAside.Step(_veil, _stepAside, delta.TotalSeconds,
            Super.SuperStepAside.FadeSecondsFor(MotionFx.Level));
        _dirty = true;
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        double o = _opacity * Super.SuperStepAside.Ease(_veil);
        if (o <= 0) return;
        var (r, g, b) = WearsLooks ? Prizes.WornFog.Over((_r, _g, _b)) : (_r, _g, _b);
        _paint.Color = new SKColor(r, g, b, (byte)Math.Clamp(o * 255, 0, 255));
        canvas.DrawRect(boundsPx, _paint);
    }
}
