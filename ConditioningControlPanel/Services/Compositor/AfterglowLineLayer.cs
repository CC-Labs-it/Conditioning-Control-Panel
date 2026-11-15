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
    private static readonly SKTypeface Face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
    private bool _dirty = true;
    private double _sinceFrame;
    private System.Drawing.Rectangle _lastBounds;

    private AfterglowLineLayer(CompositorEngine engine) : base(engine) { }

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
                if (_instance._line.Add(text.ToLowerInvariant()))
                {
                    _instance._dirty = true;
                    _instance.SetActive(true);
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

    public override void Update(TimeSpan delta)
    {
        double dt = Math.Min(delta.TotalSeconds, 0.1);
        string? done = _line.Step(dt);
        if (done != null)
        {
            // The fog writes the finished line across the middle of this monitor.
            SteamLayer.Line = done;
            var b = _lastBounds;
            if (!b.IsEmpty)
                SuperStageHost.Stage.Remember(b.X + b.Width * 0.06, b.Y + b.Height * 0.38, b.X + b.Width * 0.94, b.Y + b.Height * 0.66);
        }
        if (_line.Count == 0) { _lastBounds = default; _dirty = true; SetActive(false); return; }
        _sinceFrame += dt;
        if (_line.Animating && _sinceFrame >= 1 / 30.0) { _sinceFrame = 0; _dirty = true; }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        var stage = SuperStageHost.Stage;
        float fromX = boundsPx.MidX, fromY = boundsPx.Top + boundsPx.Height * 0.46f;
        // With the pool up on this monitor, words surface from its eye.
        if (stage.PoolLive && stage.Pool.Size > 0.3 && boundsPx.Contains((int)stage.Pool.X, (int)stage.Pool.Y))
        {
            fromX = (float)stage.Pool.X; fromY = (float)stage.Pool.Y;
        }
        var look = PinkTintLayer.LastLook;
        _draw.Draw(canvas, new SKRect(boundsPx.Left, boundsPx.Top, boundsPx.Right, boundsPx.Bottom), _line, Face,
            new SKColor(look.R, look.G, look.B), fromX, fromY);
    }
}
