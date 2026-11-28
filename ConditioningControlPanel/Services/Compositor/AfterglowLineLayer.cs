using System;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Afterglow, take two: every subliminal that flashes leaves a ghost that joins a line along the
/// bottom of the screen; a full line glows as one mantra and sinks, and the fog writes it. The
/// timing is <see cref="AfterglowLine"/> (CCP.Core); this layer steps it and draws it. Visual
/// only: no XP, no sound. Compositor only.
/// </summary>
public sealed class AfterglowLineLayer : BaseLayer
{
    private static AfterglowLineLayer? _instance;
    private static bool _hooked;
    private readonly AfterglowLine _line = new();
    private readonly AfterglowLineDraw _draw = new();

    /// <summary>The face V2 subliminal words are drawn in (rule: Core <see cref="AfterglowLineDraw.FaceFor"/>).</summary>
    internal static SKTypeface FaceNow() => AfterglowLineDraw.FaceFor(App.Settings?.Current?.SubliminalFont);

    /// <summary>
    /// Where the line sits while it is up, in physical px, for "Ignore CCP's own windows" (the screen
    /// reader skips hits inside it). The bottom band of the line's monitor, only while words are on it:
    /// never a standing blind spot. Any thread.
    /// </summary>
    public static System.Drawing.Rectangle[] GetActiveTextRectsPx()
    {
        var l = _instance;
        if (l == null || !l.IsActive) return Array.Empty<System.Drawing.Rectangle>();
        var b = l._lastBounds;
        if (b.IsEmpty) return Array.Empty<System.Drawing.Rectangle>();
        return new[] { LineBand(b) };
    }

    /// <summary>The band the line is drawn in (rule: Core <see cref="AfterglowLineRules.LineBand"/>).</summary>
    internal static System.Drawing.Rectangle LineBand(System.Drawing.Rectangle b) => AfterglowLineRules.LineBand(b);
    private bool _dirty = true;
    private double _sinceFrame;
    private int _drawnStyle = -1;
    private System.Drawing.Rectangle _lastBounds;

    private AfterglowLineLayer(CompositorEngine engine) : base(engine.Wake) { }

    public override int ZIndex => CompositorLayers.Subliminal + 1;
    public override bool WorldSpacePx => true;
    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;
    /// <summary>One line, on the primary monitor of the app's content target: two copies of a
    /// mantra building at once read as noise.</summary>
    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => _lastBounds.IsEmpty ? PrimaryOnlyUnlessDualMonitor(screenBoundsPx) && Claim(screenBoundsPx) : screenBoundsPx == _lastBounds;

    private bool Claim(System.Drawing.Rectangle b) { _lastBounds = b; return true; }

    /// <summary>
    /// A subliminal was just shown. Any thread. Does nothing unless the Afterglow star is on and
    /// the compositor is up.
    /// </summary>
    public static void WordShown(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                if (!SuperAccess.IsOn(SuperEffect.Afterglow) || !App.CompositorEnabled || App.Compositor == null) return;
                if (!_hooked)
                {
                    _hooked = true;
                    SuperAccess.Changed += e => { if (e == SuperEffect.Afterglow && !SuperAccess.IsOn(e)) DispatcherHelper.RunOnUI(StopAll); };
                }
                if (_instance == null)
                {
                    _instance = new AfterglowLineLayer(App.Compositor);
                    App.Compositor.RegisterLayer(_instance);
                }
                _instance.ApplyOptions();
                if (_instance._line.Add(text.ToLowerInvariant()))
                {
                    _instance._dirty = true;
                    _instance.SetActive(true);
                    App.InvalidateCcpWindowRectsCache();
                }
            }
            catch (Exception ex) { App.Logger?.Debug("Afterglow line: {E}", ex.Message); }
        });
    }

    /// <summary>Gone now: panic, the subliminal service stopping, the star going off. UI thread.</summary>
    public static void StopAll()
    {
        var l = _instance;
        if (l == null) return;
        l._line.Clear();
        l._lastBounds = default;
        l._dirty = true;
        l.SetActive(false);
    }

    /// <summary>The option box, read live (rule: Core <see cref="AfterglowLineRules.Apply"/>).</summary>
    private void ApplyOptions()
    {
        if (AfterglowLineRules.Apply(_line, App.Settings?.Current)) _dirty = true;
    }

    public override void Update(TimeSpan delta)
    {
        double dt = Math.Min(delta.TotalSeconds, 0.1);
        ApplyOptions();
        string? done = _line.Step(dt);
        if (done != null)
        {
            // The fog writes the finished line across the middle of this monitor.
            SteamLayer.Line = done;
            var b = _lastBounds;
            if (!b.IsEmpty)
            {
                var f = AfterglowLineRules.FogBand(b);
                SuperStageHost.Stage.Remember(f.Left, f.Top, f.Right, f.Bottom);
            }
        }
        if (_line.Count == 0) { _lastBounds = default; _dirty = true; SetActive(false); return; }
        _sinceFrame += dt;
        if (_line.Animating && _sinceFrame >= AfterglowLineRules.RepaintSeconds) { _sinceFrame = 0; _dirty = true; }
        // A new worn word style (shop Wear) repaints the waiting line at once.
        if ((int)Looks.WornWordStyle.Current != _drawnStyle) _dirty = true;
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        // With the pool up on this monitor, words surface from its eye (unless the pool's "Words" switch is off).
        var (fx, fy) = AfterglowLineRules.WordsFrom(SuperStageHost.Stage, boundsPx.Left, boundsPx.Top, boundsPx.Width, boundsPx.Height);
        float fromX = (float)fx, fromY = (float)fy;
        var look = PinkTintLayer.LastLook;
        // The worn word style: its face and colour, and its way in as each ghost rises (Still
        // under Gentle visuals or motion Off). The style's motion is settled by the time a ghost
        // lands, inside the frames the line already repaints.
        var style = Looks.WornWordStyle.Current;
        _drawnStyle = (int)style;
        _draw.Draw(canvas, new SKRect(boundsPx.Left, boundsPx.Top, boundsPx.Right, boundsPx.Bottom), _line, FaceNow(),
            new SKColor(look.R, look.G, look.B), fromX, fromY, style, Looks.WornWordStyle.Motion);
    }
}
