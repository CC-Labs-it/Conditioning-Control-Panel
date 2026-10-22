using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Convenience base for compositor layers: activity flag with engine wake-up, no-op lifecycle
/// hooks. Services own the state a layer renders; the layer holds no business logic.
/// </summary>
public abstract class BaseLayer : IWpfLayer
{
    private readonly Action _wake;
    private volatile bool _isActive;

    /// <summary>The head's monitor-target predicate: true when the monitor at these device-px
    /// bounds is in the resolved set for a <see cref="ConditioningControlPanel.Services.UI.MonitorTarget"/>
    /// value. The WPF head seeds it with App.ShouldRenderTargetOnScreen at startup; unset (no head
    /// that resolves screens) every monitor draws, the same "err toward showing" the predicate
    /// itself takes on an enumeration hiccup.</summary>
    public static volatile Func<int, System.Drawing.Rectangle, bool>? ScreenTargetGate;

    /// <param name="wake">Wakes the parked engine (CompositorEngine.Wake on the WPF head).</param>
    protected BaseLayer(Action wake)
    {
        _wake = wake;
    }

    /// <summary>True when the monitor at <paramref name="screenBoundsPx"/> is in <paramref name="target"/>'s
    /// resolved set (see <see cref="ScreenTargetGate"/>).</summary>
    protected static bool ShouldRenderTargetOnScreen(int target, System.Drawing.Rectangle screenBoundsPx)
        => ScreenTargetGate?.Invoke(target, screenBoundsPx) ?? true;

    public abstract int ZIndex { get; }

    public bool IsActive => _isActive;

    public virtual bool ExcludeFromCapture => false;

    public virtual bool WorldSpacePx => false;

    public virtual bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx) => true;

    /// <summary>Shared gate for fullscreen FILL layers (pink/spiral): when the user has multi-monitor
    /// overlays off, paint only the primary monitor. Compared by origin so a mixed-DPI width/height
    /// rounding difference can't misidentify the screen. Errs toward showing the effect on any
    /// enumeration hiccup rather than blanking it.</summary>
    protected static bool PrimaryOnlyUnlessDualMonitor(System.Drawing.Rectangle screenBoundsPx)
    {
        try
        {
            // Follows the app-wide "Show content on" picker: all screens, the Windows primary,
            // or the single monitor the user pinned content to. Errs toward showing when the
            // display set cannot be enumerated (display transition).
            return ShouldRenderTargetOnScreen(Services.UI.MonitorTarget.FollowGlobal, screenBoundsPx);
        }
        catch { return true; }
    }

    /// <summary>
    /// Flip layer activity. Safe from any thread; wakes the parked engine so the first frame
    /// renders promptly. The engine invokes OnActivated/OnDeactivated on the UI thread.
    /// </summary>
    protected void SetActive(bool value)
    {
        if (_isActive == value) return;
        _isActive = value;
        if (GentleFades)
        {
            if (GentleVisuals.On)
            {
                // A fresh show rises from nothing; a re-show mid fade-out climbs from where it is.
                if (value) { if (!_fadingOut) _fadeAlpha = 0; _fadingOut = false; }
                else _fadingOut = true;
            }
            else { _fadeAlpha = 1; _fadingOut = false; }
        }
        _wake();
    }

    // ---- Gentle visuals (Services/Safety/GentleVisuals): fade in and out instead of cutting ----

    private double _fadeAlpha = 1.0;   // stays 1 while gentle visuals are off
    private bool _fadingOut;           // hidden for the services, still drawn by the engine

    /// <summary>Opt in: fullscreen fills (spiral, pink tint, brain drain) fade in and out over
    /// <see cref="GentleVisuals.FadeSeconds"/> while gentle visuals are on.</summary>
    protected virtual bool GentleFades => false;

    public bool IsRendering => _isActive || _fadingOut;

    public double VisibilityAlpha => _fadeAlpha;

    public bool AdvanceVisibility(TimeSpan delta)
    {
        if (!_fadingOut && _fadeAlpha >= 1) return false;
        if (!GentleVisuals.On)
        {
            // Switched off mid-fade: finish at once, exactly like today.
            _fadeAlpha = 1;
            _fadingOut = false;
            return true;
        }
        _fadeAlpha = GentleFade.Step(_fadeAlpha, _fadingOut ? 0 : 1, delta.TotalSeconds, GentleVisuals.FadeSeconds);
        if (_fadingOut && _fadeAlpha <= 0) { _fadingOut = false; _fadeAlpha = 1; }
        return true;
    }

    public virtual void OnActivated() { }

    public virtual void OnDeactivated() { }

    public virtual void Update(TimeSpan delta) { }

    /// <summary>Default true: repaint every active frame. Every layer in the app overrides this
    /// with real change-tracking; the default is only a safe fallback for a NEW layer, and it is
    /// far from free. The engine folds dirt per SURFACE, so one permanently-dirty layer re-rasters
    /// the whole shared surface - fullscreen pink tint and spiral included - at the display's
    /// refresh rate (#853: that is exactly how the #550 throttle got defeated once bubbles or
    /// subliminals joined the stack).</summary>
    public virtual bool Dirty => true;

    public virtual void ClearDirty() { }

    public abstract void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed);
}
