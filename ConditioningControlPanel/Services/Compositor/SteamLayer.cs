using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Steam: the pink filter's Super look. Instead of a flat tint, a fog that creeps in from the
/// edges, wipes under the cursor, takes holes from pops and breaks, and is wound into arms by the
/// Whirlpool. The fog itself is <see cref="ConditioningControlPanel.SuperCore.SteamField"/>
/// (CCP.Core); this layer only asks the shared stage to step and draws each monitor's field.
/// The words in the fog are <see cref="SteamWords"/> (rules) and <see cref="SteamWordsDraw"/>
/// (picture): this layer hands them the word pool, the Afterglow line and the player's switch.
/// Sits in the pink filter's own slot and follows its monitor target and its colour.
/// </summary>
public sealed class SteamLayer : BaseLayer
{
    private sealed class Screen : IDisposable
    {
        public readonly SteamDraw Fog = new();
        public readonly SteamWords Words = new();
        public readonly SteamWordsDraw WordsDraw = new();
        /// <summary>Two-tone: the mix field on a small grid for this monitor, reused every frame.</summary>
        public readonly FogMixField Mix = new();
        public double LastT = -1;
        public void Dispose() { Fog.Dispose(); WordsDraw.Dispose(); }
    }

    private readonly Dictionary<System.Drawing.Rectangle, Screen> _screens = new();
    private readonly List<string> _pool = new();
    private double _poolAge = 1e9, _t, _sinceFrame;
    private bool _dirty = true;
    private static readonly SKTypeface Face = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>The line the fog writes across the middle (a finished Afterglow line), or null.</summary>
    public static string? Line { get; set; }

    /// <summary>The player's "Words in the fog" switch. Off: fog only; a word on screen fades out.</summary>
    public static bool WordsOn => App.Settings?.Current?.SteamWordsInFog != false;

    public SteamLayer(CompositorEngine engine) : base(engine.Wake) { }

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
        foreach (var s in _screens.Values) s.Dispose();
        _screens.Clear();
    }

    public override void Update(TimeSpan delta)
    {
        SuperStageHost.Step();
        double dt = Math.Min(delta.TotalSeconds, 0.1);
        _t += dt; _poolAge += dt; _sinceFrame += dt;
        if (_poolAge > 50) ReadPool();
        // The fog moves slowly: 30 pictures a second is plenty and halves the raster.
        if (_sinceFrame >= 1 / 30.0) { _sinceFrame = 0; _dirty = true; }
    }

    /// <summary>The player's own subliminal words, read now and then; each place draws a fresh one from it.</summary>
    private void ReadPool()
    {
        _poolAge = 0;
        _pool.Clear();
        try
        {
            var pool = App.Settings?.Current?.SubliminalPool;
            if (pool != null)
                _pool.AddRange(pool.Where(k => k.Value && k.Key.Length is > 0 and <= 16).Select(k => k.Key.ToLowerInvariant()));
        }
        catch (Exception ex) { App.Logger?.Debug("Steam words: {E}", ex.Message); }
        foreach (var s in _screens.Values) s.Words.SetPool(_pool);
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        var rect = new System.Drawing.Rectangle(boundsPx.Left, boundsPx.Top, boundsPx.Width, boundsPx.Height);
        SuperStageHost.NoteScreen(rect, dpiScale);
        var stage = SuperStageHost.Stage;
        if (!stage.SteamOn) return;
        var field = stage.Field(rect.ToString(), rect.X, rect.Y, rect.Width, rect.Height);
        // The gold-box options, read live: the next stage step heals and billows with them.
        var set = App.Settings?.Current;
        field.Heal = SteamOptions.HealRate(set?.SteamFogReturn ?? SteamOptions.ReturnDefault);
        field.Drift = SteamOptions.Drift(set?.SteamDrift ?? SteamOptions.DriftDefault);
        if (!_screens.TryGetValue(rect, out var screen))
        {
            screen = new Screen();
            screen.Words.SetPool(_pool);
            _screens[rect] = screen;
        }
        // The base filter's own colour and strength: thicker than the flat tint, never opaque.
        var look = PinkTintLayer.LastLook;
        double strength = Math.Clamp(0.3 + look.Opacity * 1.6, 0.3, 0.82);
        // The filter's own colour always (the shop sells no fog colours); Two-tone adds its second colour below.
        var worn = Prizes.WornFog.Shown((look.R, look.G, look.B));
        var (wr, wg, wb) = worn.Main;
        var colour = new SKColor(wr, wg, wb);
        var bounds = new SKRect(boundsPx.Left, boundsPx.Top, boundsPx.Right, boundsPx.Bottom);
        // Two-tone: the second colour moves through the fog on the mix's shared clock.
        FogMixField? mix = null;
        (byte R, byte G, byte B)[]? ramp = null;
        if (worn.TwoTone)
        {
            double aspect = rect.Width / (double)Math.Max(1, rect.Height);
            screen.Mix.Fill(48, Math.Clamp((int)Math.Round(48 / aspect), 12, 64), aspect,
                Prizes.WornFog.MixTime(Prizes.WornFog.MixSpeed(MotionFx.Level)));
            mix = screen.Mix;
            ramp = FogMix.Ramp(worn.Main, worn.Second);
        }
        // The fog only: SteamDraw's own words are retired, the words come from SteamWordsDraw below.
        screen.Fog.Draw(canvas, bounds, field, colour, strength, Array.Empty<string>(), null, Face, _t, mix, ramp);

        double dt = screen.LastT < 0 ? 0 : Math.Clamp(_t - screen.LastT, 0, 0.1);
        screen.LastT = _t;
        screen.Words.Line = Line;
        screen.Words.Step(field, dt, Safety.GentleVisuals.On, WordsOn);
        // The worn word style (shop): its face, colour and way in; Still under Gentle visuals.
        screen.WordsDraw.Draw(canvas, bounds, field, screen.Words, colour, Face, Looks.WornWordStyle.Current, Looks.WornWordStyle.Motion);
        field.ClearDirty();
    }
}
