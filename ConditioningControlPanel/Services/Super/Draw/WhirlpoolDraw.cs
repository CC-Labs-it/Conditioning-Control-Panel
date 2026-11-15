using System;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws the <see cref="Whirlpool"/>: a soft glow and three spiral arms that thin and fade toward
/// the rim, so the pool has no hard edge. Skia only, no WPF and no App: the Avalonia head can take
/// this file as it is.
/// </summary>
public sealed class WhirlpoolDraw : IDisposable
{
    private const int Arms = 3;
    private readonly SKPaint _arm = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath _path = new();
    private SKShader? _armShader;
    private readonly SKPaint _glow = new() { IsAntialias = false };
    private SKShader? _glowShader;
    private float _glowR; private int _glowKey;

    /// <summary><paramref name="strength"/> 0..1 is the base spiral's own opacity setting.</summary>
    /// <summary><paramref name="picture"/> = a frame of the user's own spiral: it is drawn as the pool
    /// (round, soft at the rim) in place of the three arms.</summary>
    public void Draw(SKCanvas canvas, Whirlpool p, double strength, SKImage? picture = null)
    {
        if (p.Size <= 0.004) return;
        double e = p.Eased;
        float r = (float)p.Radius, cx = (float)p.X, cy = (float)p.Y, dip = (float)p.Dip;
        // Pink when small, warming toward amber as it grows.
        float hue = (float)(330 + (384 - 330) * e) % 360;
        float master = (float)Math.Clamp(0.35 + 0.65 * strength, 0, 1) * (float)Math.Min(1, p.Size / 0.08);

        int key = (int)(hue * 4) ^ ((int)(e * 64) << 12);
        bool rebuilt = _glowShader == null || Math.Abs(_glowR - r) > 1.5f || key != _glowKey;
        if (rebuilt)
        {
            _glowShader?.Dispose();
            var core = SKColor.FromHsl(hue, 100, 80).WithAlpha((byte)(255 * (0.22 + 0.4 * e)));
            var mid = SKColor.FromHsl(hue, 90, 60).WithAlpha((byte)(255 * (0.08 + 0.14 * e)));
            _glowShader = SKShader.CreateRadialGradient(new SKPoint(0, 0), r,
                new[] { core, mid, SKColors.Transparent }, new[] { 0f, 0.28f, 1f }, SKShaderTileMode.Clamp);
            _glowR = r; _glowKey = key;
        }
        canvas.Save();
        canvas.Translate(cx, cy);
        _glow.Shader = _glowShader;
        _glow.Color = SKColors.White.WithAlpha((byte)(255 * master));
        canvas.DrawCircle(0, 0, r, _glow);

        if (picture != null && picture.Width > 1 && picture.Height > 1)
        {
            DrawPicture(canvas, picture, r, master, (float)e);
            canvas.Restore();
            return;
        }

        // Each arm is ONE stroked path (no joints to show), and one radial shader fades all three
        // from full strength at 45% of the reach to nothing at the rim: a soft edge, no circle.
        double turns = p.Turns, pitch = r / (turns * 2 * Math.PI), end = turns * 2 * Math.PI;
        const double step = 0.05;
        _path.Rewind();
        for (int arm = 0; arm < Arms; arm++)
        {
            double off = arm * 2 * Math.PI / Arms - p.Angle;
            bool first = true;
            for (double th = 0.3; th < end; th += step)
            {
                // Not a ruler-drawn spiral: each arm strays a little and keeps morphing.
                double rr = pitch * th, a = th + off + Whirlpool.Wobble(rr / r, th + off, p.Angle);
                rr *= 1 + 0.07 * (rr / r) * Math.Sin(2 * a + p.Angle * 0.29);   // and breathes in and out a little
                float qx = (float)(Math.Cos(a) * rr), qy = (float)(Math.Sin(a) * rr);
                if (first) { _path.MoveTo(qx, qy); first = false; } else _path.LineTo(qx, qy);
            }
        }
        if (_armShader == null || rebuilt)
        {
            _armShader?.Dispose();
            var full = SKColor.FromHsl(hue, 92, 70).WithAlpha((byte)(255 * (0.4 + 0.5 * e)));
            _armShader = SKShader.CreateRadialGradient(new SKPoint(0, 0), r,
                new[] { full, full, full.WithAlpha((byte)(full.Alpha * 0.45f)), SKColors.Transparent },
                new[] { 0f, 0.45f, 0.72f, 1f }, SKShaderTileMode.Clamp);
        }
        _arm.Shader = _armShader;
        _arm.Color = SKColors.White.WithAlpha((byte)(255 * master));
        // A wide faint pass first, so the line has a soft shoulder instead of a hard edge.
        float width = (float)(5 + 13 * e) * dip;
        _arm.Color = SKColors.White.WithAlpha((byte)(255 * master * 0.28f));
        _arm.StrokeWidth = width * 2.6f;
        canvas.DrawPath(_path, _arm);
        _arm.Color = SKColors.White.WithAlpha((byte)(255 * master));
        _arm.StrokeWidth = width;
        canvas.DrawPath(_path, _arm);
        canvas.Restore();
    }

    private readonly SKPaint _pic = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
    private readonly SKPaint _rim = new() { BlendMode = SKBlendMode.DstIn };
    private SKShader? _rimShader; private float _rimR;

    /// <summary>The middle square of the picture, as a disc that fades out over its outer third.
    /// The canvas is already centred on the pool.</summary>
    private void DrawPicture(SKCanvas canvas, SKImage picture, float r, float master, float e)
    {
        float side = Math.Min(picture.Width, picture.Height);
        var src = SKRect.Create((picture.Width - side) / 2f, (picture.Height - side) / 2f, side, side);
        var dst = new SKRect(-r, -r, r, r);
        if (_rimShader == null || Math.Abs(_rimR - r) > 1.5f)
        {
            _rimShader?.Dispose();
            _rimShader = SKShader.CreateRadialGradient(new SKPoint(0, 0), r,
                new[] { SKColors.White, SKColors.White, SKColors.White.WithAlpha(120), SKColors.Transparent },
                new[] { 0f, 0.55f, 0.8f, 1f }, SKShaderTileMode.Clamp);
            _rimR = r;
        }
        canvas.SaveLayer(dst, null);
        _pic.Color = SKColors.White.WithAlpha((byte)(255 * master * (0.55f + 0.45f * e)));
        canvas.DrawImage(picture, src, dst, _pic);
        _rim.Shader = _rimShader;
        canvas.DrawRect(dst, _rim);
        canvas.Restore();
    }

    public void Dispose()
    {
        _rimShader?.Dispose(); _rimShader = null; _pic.Dispose(); _rim.Dispose();
        _glowShader?.Dispose(); _glowShader = null;
        _armShader?.Dispose(); _armShader = null;
        _arm.Dispose(); _glow.Dispose(); _path.Dispose();
    }
}
