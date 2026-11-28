using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF ChaosBackdropService.cs:78-120: a non-topmost, click-absorbing primary-screen plate.
internal sealed class ChaosBackdropWindow : Window
{
    private static readonly string[] Scenes = { "alley_background", "alley_deep", "dollhouse_background", "dollhouse_backroom_background", "paradise_background" };
    private readonly ChaosBackdropPlate _plate = new();
    private Bitmap? _source;
    internal static ChaosBackdropWindow? Active { get; private set; }
    internal static Bitmap? CurrentSource => Active?._source;
    internal ChaosBackdropPlate Plate => _plate;
    internal int Depth { get; private set; } = -1;

    private ChaosBackdropWindow()
    {
        SystemDecorations = WindowDecorations.None;
        // WPF AllowsTransparency + window Opacity (:181, :193): BackdropOpacity lets the desktop bleed through.
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Black;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _plate;
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width / scale;
            Height = screen.Bounds.Height / scale;
        }
        Closed += (_, _) => { if (Active == this) Active = null; _plate.Release(); };
    }

    internal static void ShowForRun(ChaosPlayMode mode, int depth)
    {
        if (mode != ChaosPlayMode.Story || !CoreSettings.Current.NarrativeModeEnabled || !CoreSettings.Current.BackdropEnabled)
        { CloseActive(); return; }
        var w = Active ??= new ChaosBackdropWindow();
        w.SetDepth(depth);
        if (!w.IsVisible) w.Show();
    }

    internal void SetDepth(int depth)
    {
        if (depth == Depth) return;   // WPF SwapTo :103: same zone keeps its plate and FX phase
        Depth = depth;
        var scene = Scenes[Math.Clamp(depth - 1, 0, Scenes.Length - 1)];
        _source = ChaosArt.Resolve("backdrops", scene) ?? ChaosArt.Resolve("backdrops", "depth" + depth);
        _plate.Load(ChaosArt.PathFor("backdrops", scene) ?? ChaosArt.FilePath($"backdrops/depth{depth}.png"),
            ChaosArt.FilePath($"backdrops/{scene}_fx.png"));
        Opacity = Math.Clamp(CoreSettings.Current.BackdropOpacity, 0, 1);
    }

    internal static void CloseActive()
    {
        var w = Active;
        Active = null;
        w?.Close();
    }
}

// WPF ChaosBackdropService.cs:115-342: the scene drawn through Skia with a 450 ms crossfade on a
// depth swap, and, behind Enhanced FX, a breathing drift plus the authored glow, sheen and twinkles.
internal sealed class ChaosBackdropPlate : Control
{
    private const float Dt = .033f;
    private readonly Random _rng = new();
    private readonly List<(float x, float y, float age, float life, float size, SKColor color)> _twinkles = new();
    private readonly BeatLoop _beat;
    private (float x, float y, float weight)[] _spots = Array.Empty<(float, float, float)>();
    private SKImage? _art, _artOut, _fx, _bloom;
    private float _glowI = 1, _glowF = 1, _twinkleI = 1, _twinkleF = 1, _sheenI = 1, _sheenF = 1;
    private float _breath, _sweep, _accum, _fadeT = 1;
    internal bool Fading => _artOut != null;
    internal bool HasArt => _art != null;
    internal bool HasBloom => _bloom != null;
    internal bool Ticking => _beat.IsRunning;
    internal int TwinkleCount => _twinkles.Count;
    internal float FadeT => _fadeT;

    public ChaosBackdropPlate()
    {
        IsHitTestVisible = false;
        _beat = new BeatLoop(this, elapsed => { if (elapsed > 0) Step(); });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _beat.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _beat.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    internal void Load(string? artPath, string? fxPath)
    {
        _artOut?.Dispose(); _artOut = _art;   // the outgoing plate for the crossfade
        _art = Decode(artPath);
        _fx?.Dispose(); _fx = null; _bloom?.Dispose(); _bloom = null;
        _spots = Array.Empty<(float, float, float)>();
        try
        {
            using var raw = fxPath == null ? null : SKBitmap.Decode(fxPath);
            if (raw != null)
            {
                _fx = SKImage.FromBitmap(raw);
                _spots = ChaosHubLogoFx.Spots(raw);
                if (_art != null) _bloom = ChaosHubLogoFx.Bloom(_art, _fx);
            }
        }
        catch (Exception) { /* No mask: the scene shows plainly (WPF :30-31). */ }
        LoadTuning();
        _twinkles.Clear(); _accum = 0; _sweep = 0;
        _fadeT = _artOut != null ? 0 : 1;
        if (_artOut != null && _art == null) { _artOut.Dispose(); _artOut = null; _fadeT = 1; }
        if (this.IsAttachedToVisualTree()) _beat.Start();
        InvalidateVisual();
    }

    internal void Release()
    {
        _beat.Stop();
        _art?.Dispose(); _artOut?.Dispose(); _fx?.Dispose(); _bloom?.Dispose();
        _art = _artOut = _fx = _bloom = null;
        _twinkles.Clear();
    }

    private static SKImage? Decode(string? path)
    {
        try { return path == null ? null : SKImage.FromEncodedData(path); }
        catch (Exception) { return null; }
    }

    private void LoadTuning()
    {
        _glowI = _glowF = _twinkleI = _twinkleF = _sheenI = _sheenF = 1;
        try
        {
            var json = ChaosArt.FilePath("backdrops/backdrop_fx.json");
            if (json == null) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            float Get(string group, string key) => doc.RootElement.TryGetProperty(group, out var g) && g.TryGetProperty(key, out var v) && v.TryGetSingle(out var n) ? n : 1f;
            _glowI = Get("glow", "intensity"); _glowF = Get("glow", "frequency");
            _twinkleI = Get("twinkle", "intensity"); _twinkleF = Get("twinkle", "frequency");
            _sheenI = Get("sheen", "intensity"); _sheenF = Get("sheen", "frequency");
        }
        catch (Exception) { /* Defaults stand (WPF :343). */ }
    }

    // WPF Step :155-169 and StepTwinkles :295-327.
    internal void Step()
    {
        bool fx = ChaosSkiaFxOverlay.Enabled;
        if (_artOut != null)
        {
            _fadeT += Dt / .45f;
            if (_fadeT >= 1) { _fadeT = 1; _artOut.Dispose(); _artOut = null; }
        }
        _breath += Dt;
        if (fx)
        {
            _sweep += Dt;
            if (_spots.Length > 0 && (_accum -= Dt) <= 0)
            {
                _accum = (.35f + (float)_rng.NextDouble() * .45f) / Math.Max(.05f, _twinkleF);
                if (_twinkles.Count < Math.Max(1, (int)Math.Round(3 * _twinkleI)))
                {
                    float pick = (float)_rng.NextDouble() * _spots.Sum(s => s.weight + .05f);
                    var spot = _spots[0];
                    foreach (var s in _spots) { pick -= s.weight + .05f; if (pick <= 0) { spot = s; break; } }
                    double r = _rng.NextDouble();
                    _twinkles.Add((spot.x, spot.y, 0, .7f + (float)_rng.NextDouble() * .45f, 6 + (float)_rng.NextDouble() * 8,
                        r < .55 ? SKColors.White : r < .8 ? new SKColor(255, 230, 176) : new SKColor(255, 199, 230)));
                }
            }
            for (int i = _twinkles.Count - 1; i >= 0; i--)
            {
                var t = _twinkles[i]; t.age += Dt;
                if (t.age >= t.life) _twinkles.RemoveAt(i); else _twinkles[i] = t;
            }
        }
        else if (_artOut == null) _beat.Stop();   // idle: nothing animates until the next swap (WPF :166)
        InvalidateVisual();
    }

    // WPF OnPaint :204-228, ApplyBreath :233-242, DrawAuthoredFx :248-293.
    public override void Render(DrawingContext context)
    {
        float w = (float)Bounds.Width, h = (float)Bounds.Height;
        if (_art == null || w <= 0 || h <= 0) return;
        bool fx = ChaosSkiaFxOverlay.Enabled;
        float fade = Math.Clamp(_fadeT, 0, 1);
        var rect = Cover(_art, w, h);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, w, h));
        canvas.Clear(SKColors.Black);
        if (fx)
        {
            float s = 1.015f + .006f * (float)Math.Sin(_breath * .55);
            canvas.Translate(w / 2, h / 2);
            canvas.Scale(s, s);
            canvas.Translate(-w / 2 + .0015f * w * (float)Math.Sin(_breath * .33), -h / 2 + .002f * h * (float)Math.Sin(_breath * .47));
        }
        using (var p = new SKPaint { IsAntialias = true })
        {
            if (_artOut != null) canvas.DrawImage(_artOut, Cover(_artOut, w, h), SkSampling.High, p);
            p.Color = SKColors.White.WithAlpha((byte)(fade * 255));
            canvas.DrawImage(_art, rect, SkSampling.High, p);
        }
        if (fx)
        {
            if (_bloom != null && _glowI > .001f)
            {
                float a = (.42f + .22f * (float)Math.Sin(_breath * 1.6 * _glowF)) * _glowI * fade;
                using var p = new SKPaint { BlendMode = SKBlendMode.Plus, Color = SKColors.White.WithAlpha((byte)Math.Clamp(a * 255, 0, 255)) };
                canvas.DrawImage(_bloom, rect, SkSampling.High, p);
            }
            float phase = _sweep % (7.5f / Math.Max(.05f, _sheenF));
            if (_fx != null && _sheenI > .001f && phase < 1.4f)
            {
                float pp = phase / 1.4f, center = -.15f + 1.3f * pp;
                float c0 = Math.Max(0, center - .16f), c2 = Math.Min(1, center + .16f);
                byte a = (byte)Math.Clamp(.5f * Math.Sin(Math.PI * pp) * _sheenI * fade * 255, 0, 255);
                if (a > 1 && c2 > c0)
                {
                    using var layer = new SKPaint { BlendMode = SKBlendMode.Plus };
                    canvas.SaveLayer(layer);
                    using (var shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                        new[] { SKColors.Transparent, SKColors.White.WithAlpha(a), SKColors.Transparent }, new[] { c0, Math.Clamp(center, c0, c2), c2 }, SKShaderTileMode.Clamp))
                    using (var p = new SKPaint { Shader = shader }) canvas.DrawRect(rect, p);
                    using (var filter = ChaosHubLogoFx.Channel(2))
                    using (var p = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = filter }) canvas.DrawImage(_fx, rect, SkSampling.Medium, p);
                    canvas.Restore();
                }
            }
            float surf = Math.Min(rect.Width, rect.Height) / 760f;
            using var tp = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            foreach (var t in _twinkles)
            {
                float env = (float)Math.Sin(Math.PI * t.age / t.life) * _twinkleI * fade;
                if (env <= .01f) continue;
                float x = rect.Left + t.x * rect.Width, y = rect.Top + t.y * rect.Height, r = t.size * surf * (.7f + .3f * env);
                using (var shader = SKShader.CreateRadialGradient(new SKPoint(x, y), r * 2.4f,
                    new[] { t.color.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 150)), t.color.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
                { tp.Shader = shader; canvas.DrawCircle(x, y, r * 2.4f, tp); }
                tp.Shader = null; tp.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 220));
                canvas.DrawCircle(x, y, r * .45f, tp);
            }
        }
        context.Custom(new ChaosHubLogoFx.PictureOp(recorder.EndRecording(), new Rect(Bounds.Size)));
    }

    // WPF CoverRect :369-375: fill, centred, edges may crop.
    private static SKRect Cover(SKImage img, float w, float h)
    {
        float s = Math.Max(w / img.Width, h / img.Height), dw = img.Width * s, dh = img.Height * s;
        return new SKRect((w - dw) / 2, (h - dh) / 2, (w + dw) / 2, (h + dh) / 2);
    }
}
