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
using ConditioningControlPanel.Avalonia.Helpers;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF ChaosHubWindow.xaml.cs:2197-2400, 2470-2760.
internal sealed class ChaosHubMenuFx : Control
{
    private const float Dt = .033f;
    private readonly BeatLoop _beat;
    private readonly Random _random = new();
    private readonly Frame[] _frames = Enumerable.Range(0, 7).Select(_ => new Frame()).ToArray();
    private readonly List<Twinkle> _twinkles = new();
    private readonly Puff[] _fog = new Puff[14];
    private float _clock, _accum, _glowI = 1, _glowF = 1, _twinkleI = 1, _twinkleF = 1, _sheenI = 1, _sheenF = 1;
    private int _baseFrame, _topFrame;
    private float _fade, _scale = 1, _angle, _offset;
    internal int SpotCount => _frames.Sum(f => f.Spots.Length);
    internal int TwinkleCount => _twinkles.Count;
    internal float FogX => _fog[0].X;
    private sealed class Frame
    {
        public SKImage? Mask, Bloom;
        public int Width, Height;
        public (float x, float y, float weight)[] Spots = Array.Empty<(float, float, float)>();
        public void Clear() { Mask?.Dispose(); Bloom?.Dispose(); Mask = Bloom = null; Spots = Array.Empty<(float, float, float)>(); }
    }
    private sealed class Twinkle { public float X, Y, Age, Life, Size; public SKColor Color; }
    private sealed class Puff { public float X, Y, R, Vx, Vy, Phase, Speed, Alpha; }
    private static float Frac(float v) => v - MathF.Floor(v);

    public ChaosHubMenuFx()
    {
        IsHitTestVisible = false;
        for (int i = 0; i < _fog.Length; i++)
        {
            float t = (i + .5f) / _fog.Length;
            _fog[i] = new Puff { X = .10f + .85f * Frac(t * 1.7f), Y = .40f + .65f * Frac(t * 2.3f),
                R = .34f + .26f * Frac(t * 3.1f), Vx = (.004f + .006f * Frac(t * 5)) * (i % 2 == 0 ? 1 : -1),
                Vy = -(.003f + .004f * Frac(t * 4)), Phase = t * 6.283f, Speed = .012f + .01f * Frac(t * 6), Alpha = .34f + .22f * Frac(t * 7) };
        }
        _beat = new BeatLoop(this, t => { if (t > 0 && IsEffectivelyVisible) { Step(); InvalidateVisual(); } });
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); Load(); _beat.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _beat.Stop(); foreach (var f in _frames) f.Clear(); _twinkles.Clear(); base.OnDetachedFromVisualTree(e);
    }
    internal void SetFrames(int baseFrame, int topFrame, double fade)
    {
        _baseFrame = baseFrame; _topFrame = topFrame; _fade = (float)Math.Clamp(fade, 0, 1); InvalidateVisual();
    }
    internal void SetMotion(double scale, double angle, double offset) { _scale = (float)scale; _angle = (float)angle; _offset = (float)offset; }
    private Frame GetFrame(int i) => i >= 0 && i < 6 && _frames[i].Mask != null ? _frames[i] : _frames[6];
    private static SKColorFilter Channel(int channel)
    {
        var m = new float[20]; m[4] = m[9] = m[14] = 1; m[15 + channel] = 1;
        return SKColorFilter.CreateColorMatrix(m);
    }
    private void Load()
    {
        foreach (var f in _frames) f.Clear();
        _glowI = _glowF = _twinkleI = _twinkleF = _sheenI = _sheenF = 1;
        for (int i = 0; i < 7; i++)
        {
            try
            {
                string art = i < 6 ? $"menu_{i + 1}.png" : "menu.png";
                string mask = i < 6 ? $"menu_{i + 1}_fx.png" : "menu_fx.png";
                string? ap = ChaosArt.FilePath(art) ?? (i == 6 ? ChaosArt.FilePath("banner.png") : null);
                string? mp = ChaosArt.FilePath(mask);
                if (mp == null) continue;
                using var source = ap == null ? null : SKImage.FromEncodedData(ap);
                using var raw = SKBitmap.Decode(mp);
                if (raw == null) continue;
                var f = _frames[i]; f.Width = source?.Width ?? 0; f.Height = source?.Height ?? 0; f.Mask = SKImage.FromBitmap(raw);
                int h = Math.Max(1, raw.Height * 96 / Math.Max(1, raw.Width));
                using var small = raw.Resize(new SKImageInfo(96, h), SKFilterQuality.Medium);
                if (small != null)
                {
                    int cols = Math.Max(1, small.Width / 8), rows = Math.Max(1, small.Height / 8);
                    var best = new (float v, int x, int y)[cols * rows]; float max = .001f;
                    for (int y = 0; y < small.Height; y++) for (int x = 0; x < small.Width; x++)
                    {
                        float v = small.GetPixel(x, y).Green / 255f;
                        int cell = Math.Min(rows - 1, y * rows / small.Height) * cols + Math.Min(cols - 1, x * cols / small.Width);
                        if (v > best[cell].v) best[cell] = (v, x, y); max = Math.Max(max, v);
                    }
                    f.Spots = best.Where(b => b.v > max * .5f).OrderByDescending(b => b.v).Take(10)
                        .Select(b => ((b.x + .5f) / small.Width, (b.y + .5f) / small.Height, b.v / max)).ToArray();
                }
                if (source == null) continue;
                int w = Math.Min(source.Width, 540), bh = Math.Max(1, source.Height * w / source.Width);
                var info = new SKImageInfo(w, bh, SKColorType.Rgba8888, SKAlphaType.Premul); var rect = new SKRect(0, 0, w, bh);
                using var s1 = SKSurface.Create(info); s1.Canvas.Clear(SKColors.Transparent);
                using (var p = new SKPaint { FilterQuality = SKFilterQuality.Medium }) s1.Canvas.DrawImage(source, rect, p);
                using (var filter = Channel(0)) using (var p = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = filter }) s1.Canvas.DrawImage(f.Mask, rect, p);
                using var masked = s1.Snapshot(); using var s2 = SKSurface.Create(info); s2.Canvas.Clear(SKColors.Transparent);
                using (var blur = SKImageFilter.CreateBlur(w * .013f, w * .013f)) using (var p = new SKPaint { ImageFilter = blur }) s2.Canvas.DrawImage(masked, rect, p);
                f.Bloom = s2.Snapshot();
            }
            catch (Exception) { _frames[i].Clear(); }
        }
        try
        {
            var path = ChaosArt.FilePath("menu_fx.json"); if (path == null) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            float Get(string group, string key) => doc.RootElement.TryGetProperty(group, out var g) && g.TryGetProperty(key, out var v) && v.TryGetSingle(out var n) ? n : 1;
            _glowI = Get("glow", "intensity"); _glowF = Get("glow", "frequency");
            _twinkleI = Get("twinkle", "intensity"); _twinkleF = Get("twinkle", "frequency");
            _sheenI = Get("sheen", "intensity"); _sheenF = Get("sheen", "frequency");
        }
        catch (Exception) { /* Optional tuning. */ }
    }
    internal void Step()
    {
        if (!ChaosSkiaFxOverlay.Enabled) return;
        _clock += Dt;
        float aspect = Bounds.Height > 0 ? (float)(Bounds.Width / Bounds.Height) : 1;
        foreach (var p in _fog)
        {
            p.X += p.Vx; p.Y += p.Vy; p.Phase += p.Speed; float ry = p.R * aspect;
            if (p.Y + ry < 0) { p.Y = 1 + ry; p.X = .15f + .7f * Frac(p.Phase); }
            if (p.X - p.R > 1) p.X = -p.R; if (p.X + p.R < 0) p.X = 1 + p.R;
        }
        var spots = GetFrame(_baseFrame).Spots;
        if (spots.Length > 0 && (_accum -= Dt) <= 0)
        {
            _accum = (.35f + (float)_random.NextDouble() * .45f) / Math.Max(.05f, _twinkleF);
            if (_twinkles.Count < Math.Max(1, (int)Math.Round(3 * _twinkleI)))
            {
                float pick = (float)_random.NextDouble() * spots.Sum(s => s.weight + .05f); var spot = spots[0];
                foreach (var s in spots) { pick -= s.weight + .05f; if (pick <= 0) { spot = s; break; } }
                double r = _random.NextDouble();
                _twinkles.Add(new Twinkle { X = spot.x, Y = spot.y, Life = .7f + (float)_random.NextDouble() * .45f,
                    Size = 6 + (float)_random.NextDouble() * 8, Color = r < .55 ? SKColors.White : r < .8 ? new SKColor(255, 230, 176) : new SKColor(255, 199, 230) });
            }
        }
        for (int i = _twinkles.Count - 1; i >= 0; i--) { _twinkles[i].Age += Dt; if (_twinkles[i].Age >= _twinkles[i].Life) _twinkles.RemoveAt(i); }
    }
    private static SKRect Cover(Frame f, float w, float h)
    {
        float s = Math.Max(w / f.Width, h / f.Height), iw = f.Width * s, ih = f.Height * s;
        return SKRect.Create((w - iw) / 2, (h - ih) / 2, iw, ih);
    }
    private void DrawFrame(SKCanvas canvas, Frame f, float weight, float w, float h)
    {
        if (f.Mask == null || f.Width == 0 || weight <= 0) return;
        var rect = Cover(f, w, h);
        if (f.Bloom != null && _glowI > .001f)
        {
            float a = (.42f + .22f * MathF.Sin(_clock * 1.6f * _glowF)) * _glowI * weight;
            using var p = new SKPaint { BlendMode = SKBlendMode.Plus, FilterQuality = SKFilterQuality.High, Color = SKColors.White.WithAlpha((byte)Math.Clamp(a * 255, 0, 255)) };
            canvas.DrawImage(f.Bloom, rect, p);
        }
        float phase = _clock % (7.5f / Math.Max(.05f, _sheenF));
        if (_sheenI > .001f && phase < 1.4f)
        {
            float pp = phase / 1.4f, center = -.15f + 1.3f * pp, c0 = Math.Max(0, center - .16f), c2 = Math.Min(1, center + .16f);
            byte a = (byte)Math.Clamp(.5f * Math.Sin(Math.PI * pp) * _sheenI * weight * 255, 0, 255);
            if (a > 1 && c2 > c0)
            {
                using var layer = new SKPaint { BlendMode = SKBlendMode.Plus }; canvas.SaveLayer(layer);
                using (var shader = SKShader.CreateLinearGradient(new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom), new[] { SKColors.Transparent, SKColors.White.WithAlpha(a), SKColors.Transparent }, new[] { c0, Math.Clamp(center, c0, c2), c2 }, SKShaderTileMode.Clamp))
                using (var p = new SKPaint { Shader = shader }) canvas.DrawRect(rect, p);
                using (var filter = Channel(2)) using (var p = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = filter }) canvas.DrawImage(f.Mask, rect, p);
                canvas.Restore();
            }
        }
    }
    public override void Render(DrawingContext context)
    {
        if (!ChaosSkiaFxOverlay.Enabled || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        float w = (float)Bounds.Width, h = (float)Bounds.Height; var bounds = new Rect(Bounds.Size);
        using var recorder = new SKPictureRecorder(); var canvas = recorder.BeginRecording(new SKRect(0, 0, w, h));
        canvas.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, w, h), 22, 22), antialias: true);
        canvas.Save(); canvas.Translate(w / 2, h / 2); canvas.Scale(_scale); canvas.RotateDegrees(_angle); canvas.Translate(-w / 2, -h / 2 + h * _offset);
        DrawFrame(canvas, GetFrame(_baseFrame), 1 - _fade, w, h); if (_fade > 0) DrawFrame(canvas, GetFrame(_topFrame), _fade, w, h);
        var frame = GetFrame(_baseFrame);
        if (frame.Width > 0)
        {
            var rect = Cover(frame, w, h); using var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            foreach (var t in _twinkles)
            {
                float env = MathF.Sin(MathF.PI * t.Age / t.Life) * _twinkleI; if (env <= .01f) continue;
                float x = rect.Left + t.X * rect.Width, y = rect.Top + t.Y * rect.Height, r = t.Size * Math.Min(rect.Width, rect.Height) / 760f * (.7f + .3f * env);
                using (var shader = SKShader.CreateRadialGradient(new SKPoint(x, y), r * 2.4f, new[] { t.Color.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 150)), t.Color.WithAlpha(0) }, null, SKShaderTileMode.Clamp)) { p.Shader = shader; canvas.DrawCircle(x, y, r * 2.4f, p); }
                p.Shader = null; p.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 220)); canvas.DrawCircle(x, y, r * .45f, p);
            }
        }
        canvas.Restore();
        using (var p = new SKPaint { IsAntialias = true }) foreach (var f in _fog)
        {
            float x = f.X * w, y = f.Y * h, r = f.R * w; byte a = (byte)(Math.Clamp(f.Alpha * (.7f + .3f * MathF.Sin(f.Phase)), 0, 1) * 255);
            using var shader = SKShader.CreateRadialGradient(new SKPoint(x, y), r, new[] { new SKColor(0xE8, 0x43, 0x93, a), new SKColor(0xE8, 0x43, 0x93, 0) }, null, SKShaderTileMode.Clamp);
            p.Shader = shader; canvas.DrawCircle(x, y, r, p);
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
        public void Render(ImmediateDrawingContext context) { var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>(); if (feature == null) return; using var lease = feature.Lease(); lease.SkCanvas.DrawPicture(_picture); }
    }
}
