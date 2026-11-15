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
    private readonly SKPaint _arm = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _glow = new() { IsAntialias = false };
    private SKShader? _glowShader;
    private float _glowR; private int _glowKey;

    /// <summary><paramref name="strength"/> 0..1 is the base spiral's own opacity setting.</summary>
    public void Draw(SKCanvas canvas, Whirlpool p, double strength)
    {
        if (p.Size <= 0.004) return;
        double e = p.Eased;
        float r = (float)p.Radius, cx = (float)p.X, cy = (float)p.Y, dip = (float)p.Dip;
        // Pink when small, warming toward amber as it grows.
        float hue = (float)(330 + (384 - 330) * e) % 360;
        float master = (float)Math.Clamp(0.35 + 0.65 * strength, 0, 1) * (float)Math.Min(1, p.Size / 0.08);

        int key = (int)(hue * 4) ^ ((int)(e * 64) << 12);
        if (_glowShader == null || Math.Abs(_glowR - r) > 1.5f || key != _glowKey)
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

        double turns = 1.6 + e * 3.4, pitch = r / (turns * 2 * Math.PI), end = turns * 2 * Math.PI;
        const double step = 0.16;
        var colour = SKColor.FromHsl(hue, 92, 68);
        for (int arm = 0; arm < Arms; arm++)
        {
            double off = arm * 2 * Math.PI / Arms - p.Angle;
            double th = 0.35;
            float px = (float)(Math.Cos(th + off) * pitch * th), py = (float)(Math.Sin(th + off) * pitch * th);
            for (th += step; th < end; th += step)
            {
                double rr = pitch * th, u = rr / r;             // 0 at the eye, 1 at the rim
                float qx = (float)(Math.Cos(th + off) * rr), qy = (float)(Math.Sin(th + off) * rr);
                // Faded edge: full strength to 55% of the reach, then gone by the rim.
                double fade = u < 0.55 ? 1 : Math.Max(0, 1 - (u - 0.55) / 0.45);
                fade *= fade;
                double a = (0.3 + 0.5 * e) * fade * master;
                if (a > 0.012)
                {
                    _arm.Color = colour.WithAlpha((byte)(255 * a));
                    _arm.StrokeWidth = (float)((2.5 + 6.5 * e) * (0.45 + 0.55 * fade)) * dip;
                    canvas.DrawLine(px, py, qx, qy, _arm);
                }
                px = qx; py = qy;
            }
        }
        canvas.Restore();
    }

    public void Dispose()
    {
        _glowShader?.Dispose(); _glowShader = null;
        _arm.Dispose(); _glow.Dispose();
    }
}
