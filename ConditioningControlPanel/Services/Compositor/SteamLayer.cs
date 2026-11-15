using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Steam: the pink filter's Super look. Instead of a flat tint, a fog that creeps in from the
/// edges, wipes under the cursor, takes holes from pops and breaks, and is wound into arms by the
/// Whirlpool. The fog itself is <see cref="ConditioningControlPanel.SuperCore.SteamField"/>
/// (CCP.Core); this layer only asks the shared stage to step and draws each monitor's field.
/// Sits in the pink filter's own slot and follows its monitor target and its colour.
/// </summary>
public sealed class SteamLayer : BaseLayer
{
    private readonly Dictionary<System.Drawing.Rectangle, SteamDraw> _draws = new();
    private readonly List<string> _words = new();
    private readonly Random _rng = new();
    private double _wordsAge = 1e9, _t, _sinceFrame;
    private bool _dirty = true;
    private static readonly SKTypeface Face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>The line the fog writes across the middle (a finished Afterglow line), or null.</summary>
    public static string? Line { get; set; }

    public SteamLayer(CompositorEngine engine) : base(engine) { }

    public override int ZIndex => PinkTintLayer.ZFor(App.Settings?.Current?.PinkFilterUnderEffects == true);
    public override bool WorldSpacePx => true;
    protected override bool GentleFades => true;
    public override bool ShouldRenderOnScreen(System.Drawing.Rectangle screenBoundsPx)
        => App.ShouldRenderTargetOnScreen(
               App.Settings?.Current?.PinkFilterTargetMonitor ?? App.MonitorTargetFollowGlobal,
               screenBoundsPx);
    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    public void Show() { _dirty = true; SetActive(true); }

    public void Hide()
    {
        if (!IsActive) return;
        SetActive(false);
        Line = null;
        _dirty = true;
    }

    public override void OnActivated() { }

    public override void OnDeactivated()
    {
        foreach (var d in _draws.Values) d.Dispose();
        _draws.Clear();
    }

    public override void Update(TimeSpan delta)
    {
        SuperStageHost.Step();
        double dt = Math.Min(delta.TotalSeconds, 0.1);
        _t += dt; _wordsAge += dt; _sinceFrame += dt;
        if (_wordsAge > 50) PickWords();
        // The fog moves slowly: 30 pictures a second is plenty and halves the raster.
        if (_sinceFrame >= 1 / 30.0) { _sinceFrame = 0; _dirty = true; }
    }

    private void PickWords()
    {
        _wordsAge = 0;
        _words.Clear();
        try
        {
            var pool = App.Settings?.Current?.SubliminalPool;
            if (pool == null) return;
            var on = pool.Where(k => k.Value && k.Key.Length is > 0 and <= 16).Select(k => k.Key).ToList();
            for (int i = 0; i < 6 && on.Count > 0; i++)
            {
                int k = _rng.Next(on.Count);
                _words.Add(on[k].ToLowerInvariant());
                on.RemoveAt(k);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Steam words: {E}", ex.Message); }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        var rect = new System.Drawing.Rectangle(boundsPx.Left, boundsPx.Top, boundsPx.Width, boundsPx.Height);
        SuperStageHost.NoteScreen(rect, dpiScale);
        var stage = SuperStageHost.Stage;
        if (!stage.SteamOn) return;
        var field = stage.Field(rect.ToString(), rect.X, rect.Y, rect.Width, rect.Height);
        if (!_draws.TryGetValue(rect, out var draw)) { draw = new SteamDraw(); _draws[rect] = draw; }
        // The base filter's own colour and strength: thicker than the flat tint, never opaque.
        var look = PinkTintLayer.LastLook;
        double strength = Math.Clamp(0.3 + look.Opacity * 1.6, 0.3, 0.82);
        draw.Draw(canvas, new SKRect(boundsPx.Left, boundsPx.Top, boundsPx.Right, boundsPx.Bottom), field,
            new SKColor(look.R, look.G, look.B), strength, _words, Line, Face, _t);
        field.ClearDirty();
    }
}
