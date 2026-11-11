using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.VisualTree;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF ChaosHubWindow.xaml.cs:2240-2265, 2291-2324, 2408-2510, 2751-2782.
internal sealed class ChaosHubLogoFx : Control
{
    private const float Dt = .033f;
    private readonly Random _rng = new();
    private readonly List<Twinkle> _twinkles = new();
    private (float x, float y, float weight)[] _spots = Array.Empty<(float, float, float)>();
    private SKImage? _mask, _bloom;
    private float _glowI = 1, _glowF = 1, _twinkleI = 1, _twinkleF = 1, _sheenI = 1, _sheenF = 1;
    private float _clock, _sweep, _accum;
    private int _logoWidth, _logoHeight;
    private readonly BeatLoop _beat;
    internal int TwinkleCount => _twinkles.Count;
    internal int SpotCount => _spots.Length;

    private sealed class Twinkle
    {
        public float X, Y, Age, Life, Size;
        public SKColor Color;
    }

    public ChaosHubLogoFx()
    {
        IsHitTestVisible = false;
        _beat = new BeatLoop(this, elapsed =>
        {
            if (elapsed > 0 && IsEffectivelyVisible) { Step(); InvalidateVisual(); }
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_mask == null) Load();
        _beat.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _beat.Stop();
        _mask?.Dispose(); _mask = null;
        _bloom?.Dispose(); _bloom = null;
        _twinkles.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private static SKColorFilter Channel(int channel)
    {
        var m = new float[20];
        m[4] = m[9] = m[14] = 1;
        m[15 + channel] = 1;
        return SKColorFilter.CreateColorMatrix(m);
    }

    private void Load()
    {
        _spots = Array.Empty<(float, float, float)>();
        _glowI = _glowF = _twinkleI = _twinkleF = _sheenI = _sheenF = 1;
        try
        {
            var path = ChaosArt.FilePath("menu_logo_fx.png");
            var logo = ChaosArt.FilePath("menu_logo.png");
            if (path == null || logo == null) return;
            using var raw = SKBitmap.Decode(path);
            using var source = SKImage.FromEncodedData(logo);
            if (raw == null || source == null) return;
            _logoWidth = source.Width; _logoHeight = source.Height;
            _mask = SKImage.FromBitmap(raw);
            int h = Math.Max(1, raw.Height * 96 / Math.Max(1, raw.Width));
            using var small = raw.Resize(new SKImageInfo(96, h), SKFilterQuality.Medium);
            if (small != null)
            {
                int cols = Math.Max(1, small.Width / 8), rows = Math.Max(1, small.Height / 8);
                var best = new (float v, int x, int y)[cols * rows];
                float max = .001f;
                for (int y = 0; y < small.Height; y++) for (int x = 0; x < small.Width; x++)
                {
                    float v = small.GetPixel(x, y).Green / 255f;
                    int i = Math.Min(rows - 1, y * rows / small.Height) * cols + Math.Min(cols - 1, x * cols / small.Width);
                    if (v > best[i].v) best[i] = (v, x, y);
                    max = Math.Max(max, v);
                }
                _spots = best.Where(b => b.v > max * .5f)
                    .OrderByDescending(b => b.v).Take(10)
                    .Select(b => ((b.x + .5f) / small.Width, (b.y + .5f) / small.Height, b.v / max)).ToArray();
            }
            int w = Math.Min(source.Width, 540), bh = Math.Max(1, source.Height * w / source.Width);
            var info = new SKImageInfo(w, bh, SKColorType.Rgba8888, SKAlphaType.Premul);
            var rect = new SKRect(0, 0, w, bh);
            using var s1 = SKSurface.Create(info);
            s1.Canvas.Clear(SKColors.Transparent);
            using (var p = new SKPaint { FilterQuality = SKFilterQuality.Medium }) s1.Canvas.DrawImage(source, rect, p);
            using (var filter = Channel(0))
            using (var p = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = filter, FilterQuality = SKFilterQuality.Medium }) s1.Canvas.DrawImage(_mask, rect, p);
            using var masked = s1.Snapshot();
            using var s2 = SKSurface.Create(info);
            s2.Canvas.Clear(SKColors.Transparent);
            using (var blur = SKImageFilter.CreateBlur(w * .013f, w * .013f))
            using (var p = new SKPaint { ImageFilter = blur }) s2.Canvas.DrawImage(masked, rect, p);
            _bloom = s2.Snapshot();
            var json = ChaosArt.FilePath("menu_logo_fx.json");
            if (json == null) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            float Get(string group, string key) => doc.RootElement.TryGetProperty(group, out var g) && g.TryGetProperty(key, out var v) && v.TryGetSingle(out var n) ? n : 1f;
            _glowI = Get("glow", "intensity"); _glowF = Get("glow", "frequency");
            _twinkleI = Get("twinkle", "intensity"); _twinkleF = Get("twinkle", "frequency");
            _sheenI = Get("sheen", "intensity"); _sheenF = Get("sheen", "frequency");
        }
        catch (Exception) { /* Missing or invalid optional art leaves the wordmark intact. */ }
    }

    internal void Step()
    {
        _clock += Dt;
        if (!ChaosSkiaFxOverlay.Enabled) return;
        _sweep += Dt;
        if (_spots.Length > 0)
        {
            _accum -= Dt;
            if (_accum <= 0)
            {
                _accum = (.35f + (float)_rng.NextDouble() * .45f) / Math.Max(.05f, _twinkleF);
                if (_twinkles.Count < Math.Max(1, (int)Math.Round(3 * _twinkleI)))
                {
                    float pick = (float)_rng.NextDouble() * _spots.Sum(s => s.weight + .05f);
                    var spot = _spots[0];
                    foreach (var s in _spots) { pick -= s.weight + .05f; if (pick <= 0) { spot = s; break; } }
                    double r = _rng.NextDouble();
                    _twinkles.Add(new Twinkle { X = spot.x, Y = spot.y, Life = .7f + (float)_rng.NextDouble() * .45f,
                        Size = 6 + (float)_rng.NextDouble() * 8, Color = r < .55 ? SKColors.White : r < .8 ? new SKColor(255, 230, 176) : new SKColor(255, 199, 230) });
                }
            }
        }
        for (int i = _twinkles.Count - 1; i >= 0; i--)
        {
            _twinkles[i].Age += Dt;
            if (_twinkles[i].Age >= _twinkles[i].Life) _twinkles.RemoveAt(i);
        }
    }

    public override void Render(DrawingContext context)
    {
        if (!ChaosSkiaFxOverlay.Enabled || _mask == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        // Match Image's Uniform stretch and center the effects on the same pixels.
        float scale = Math.Min((float)Bounds.Width / _logoWidth, (float)Bounds.Height / _logoHeight);
        float w = _logoWidth * scale, h = _logoHeight * scale;
        var rect = SKRect.Create(((float)Bounds.Width - w) / 2, ((float)Bounds.Height - h) / 2, w, h);
        var bounds = new Rect(Bounds.Size);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
        if (_bloom != null && _glowI > .001f)
        {
            float a = (.42f + .22f * (float)Math.Sin(_clock * 1.6 * _glowF)) * _glowI;
            using var p = new SKPaint { BlendMode = SKBlendMode.Plus, FilterQuality = SKFilterQuality.High, Color = SKColors.White.WithAlpha((byte)Math.Clamp(a * 255, 0, 255)) };
            canvas.DrawImage(_bloom, rect, p);
        }
        float phase = _sweep % (7.5f / Math.Max(.05f, _sheenF));
        if (_sheenI > .001f && phase < 1.4f)
        {
            float pp = phase / 1.4f, center = -.15f + 1.3f * pp;
            float c0 = Math.Max(0, center - .16f), c2 = Math.Min(1, center + .16f);
            byte a = (byte)Math.Clamp(.5f * Math.Sin(Math.PI * pp) * _sheenI * 255, 0, 255);
            if (a > 1 && c2 > c0)
            {
                using var layer = new SKPaint { BlendMode = SKBlendMode.Plus };
                canvas.SaveLayer(layer);
                using (var shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
                    new[] { SKColors.Transparent, SKColors.White.WithAlpha(a), SKColors.Transparent }, new[] { c0, Math.Clamp(center, c0, c2), c2 }, SKShaderTileMode.Clamp))
                using (var p = new SKPaint { Shader = shader }) canvas.DrawRect(rect, p);
                using (var filter = Channel(2))
                using (var p = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = filter, FilterQuality = SKFilterQuality.Medium }) canvas.DrawImage(_mask, rect, p);
                canvas.Restore();
            }
        }
        using (var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus })
            foreach (var t in _twinkles)
            {
                float env = (float)Math.Sin(Math.PI * t.Age / t.Life) * _twinkleI;
                if (env <= .01f) continue;
                float x = rect.Left + t.X * rect.Width, y = rect.Top + t.Y * rect.Height;
                float r = t.Size * rect.Height / 240f * (.7f + .3f * env);
                using (var shader = SKShader.CreateRadialGradient(new SKPoint(x, y), r * 2.4f,
                    new[] { t.Color.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 150)), t.Color.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
                { p.Shader = shader; canvas.DrawCircle(x, y, r * 2.4f, p); }
                p.Shader = null; p.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 220));
                canvas.DrawCircle(x, y, r * .45f, p);
            }
        context.Custom(new PictureOp(recorder.EndRecording(), bounds));
    }

    private sealed class PictureOp : ICustomDrawOperation
    {
        private readonly SKPicture _picture;
        public PictureOp(SKPicture picture, Rect bounds) { _picture = picture; Bounds = bounds; }
        public Rect Bounds { get; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() => _picture.Dispose();
        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature == null) return;
            using var lease = feature.Lease();
            lease.SkCanvas.DrawPicture(_picture);
        }
    }
}
