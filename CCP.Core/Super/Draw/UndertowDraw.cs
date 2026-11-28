using System;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws the Undertow lens (<see cref="UndertowLens"/>, <see cref="UndertowState"/>) into a blurred
/// frame: the blur with a feathered hole under the cursor, the tinted rim and the droplet sparks.
/// Moved from WPF <c>BrainDrainLayer</c> so both heads draw the same picture. Skia only, no WPF and
/// no App. The click-wave shader is NOT built here: its runtime-effect API differs between
/// SkiaSharp 2.88 (WPF) and 3.119 (Avalonia), so each head builds the wave paint and passes it in.
/// </summary>
public sealed class UndertowDraw : IDisposable
{
    /// <summary>The click waves (WPF BrainDrainLayer, unchanged): displace the sample point along the
    /// radial direction by a sine train that is sharp at the front and fades behind it, plus a little
    /// crest lighting. It uses <c>src.eval</c>: SkiaSharp 2.88 refuses it ("type 'fragmentProcessor'
    /// does not have a field named 'eval'"), so WPF ships without waves; SkiaSharp 3.119 compiles it
    /// and refuses <c>sample(src, c)</c> instead (probe 2026-11-25).</summary>
    public const string WaveSksl = @"
uniform shader src;
uniform float4 w[6];   // x, y, front radius, amplitude (px)
uniform float3 p;      // wavelength, trailing band, amplitude reference
uniform float4 k[6];   // x = specular crest strength (one fade from the click, photosafe-capped)
half4 main(float2 c) {
    float2 off = float2(0.0);
    float hi = 0.0;
    float spec = 0.0;
    for (int i = 0; i < 6; i++) {
        float amp = w[i].w;
        if (amp > 0.0) {
            float2 d = c - w[i].xy;
            float dist = length(d);
            if (dist > 0.5) {
                float x = dist - w[i].z;
                float env = x > 0.0 ? exp(-pow(x / (p.x * 0.5), 2.0)) : exp(x / p.y);
                float ph = x * 6.2831853 / p.x;
                off += (d / dist) * sin(ph) * amp * env;
                hi += cos(ph) * env * (amp / p.z);
                float lead = exp(-pow(x / (p.x * 0.6), 2.0));
                spec += k[i].x * lead * pow(max(cos(ph), 0.0), 6.0);
            }
        }
    }
    half4 col = src.eval(c + off);
    col.rgb *= half(1.0 + 0.12 * clamp(hi, -1.0, 1.0));
    col.rgb += half3(1.0, 0.86, 0.95) * half(clamp(spec, 0.0, 0.3)) * col.a;
    return col;
}";

    /// <summary>The wave shader's uniforms for the live ripples: <paramref name="w"/> and
    /// <paramref name="k"/> hold 6 x 4 floats, <paramref name="p"/> 3. False when no wave draws.</summary>
    public static bool FillWaves(UndertowState s, double screenWidth, UndertowMotion motion, double dpiScale,
                                 float[] w, float[] k, float[] p)
    {
        Array.Clear(w);
        Array.Clear(k);
        double maxAmp = 0, wl = 0, band = 0;
        int n = 0;
        foreach (var rp in s.Ripples)
        {
            if (!rp.Live || n >= 6) continue;
            if (!UndertowLens.WaveAt(rp.Age, screenWidth, motion, dpiScale, out double front, out double amp, out wl, out band)) continue;
            w[n * 4] = (float)rp.X; w[n * 4 + 1] = (float)rp.Y;
            w[n * 4 + 2] = (float)front; w[n * 4 + 3] = (float)amp;
            k[n * 4] = (float)UndertowLens.CrestAt(rp.Age, rp.Crest, motion);
            if (amp > maxAmp) maxAmp = amp;
            n++;
        }
        if (n == 0) return false;
        p[0] = (float)wl; p[1] = (float)band; p[2] = (float)Math.Max(1, maxAmp);
        return true;
    }

    private static readonly SKColor Tint = new(0xFF, 0xA8, 0xDC);   // soft pink, screened at low alpha
    private SKPaint? _maskPaint, _rimPaint, _dropPaint;
    private SKSurface? _small;
    private int _smallW, _smallH;

    /// <summary>
    /// The blur with the feathered lens left out, then the rim and the drops. <paramref name="basePaint"/>
    /// draws the blurred <paramref name="image"/> over <paramref name="dest"/> (its alpha is the blur's
    /// draw alpha); <paramref name="wave"/>, when not null, draws the base instead (a shader over dest).
    /// The outside of the lens box draws straight; the box is cut in the SMALL source and stretched
    /// (no screen-sized layer), with a bounded layer only as the fallback and under a wave.
    /// </summary>
    public void Draw(SKCanvas canvas, UndertowState s, SKImage image, SKRect dest, SKPaint basePaint,
                     double screenWidth, UndertowMotion motion, double dpiScale, SKPaint? wave = null)
    {
        float r = (float)UndertowLens.DrawnRadius(s, screenWidth, motion);
        float x = (float)s.X, y = (float)s.Y;
        // Cosmetic stretch along cursor motion: only ever LONGER along the motion, never narrower
        // across it, so the minimum clear area around the mouse holds.
        float stretch = (float)UndertowLens.Stretch(s.Vx, s.Vy, screenWidth, motion);
        float angle = stretch > 0 ? (float)Math.Atan2(s.Vy, s.Vx) : 0f;
        float box = r * (1f + stretch);
        var lens = new SKRect(x - box, y - box, x + box, y + box);
        byte drawAlpha = basePaint.Color.Alpha;

        if (r < 1f || !lens.IntersectsWith(dest))
        {
            DrawBase(canvas, image, dest, basePaint, wave);
        }
        else
        {
            canvas.Save();
            canvas.ClipRect(lens, SKClipOperation.Difference);
            DrawBase(canvas, image, dest, basePaint, wave);
            canvas.Restore();

            if (wave != null || !DrawLensSmall(canvas, image, dest, basePaint, lens, x, y, r, stretch, angle))
            {
                canvas.SaveLayer(lens, null);
                canvas.ClipRect(lens);
                DrawBase(canvas, image, dest, basePaint, wave);
                canvas.Translate(x, y);
                canvas.RotateRadians(angle);
                canvas.Scale(r * (1f + stretch), r);
                canvas.DrawCircle(0, 0, 1, MaskPaint());
                canvas.Restore();
            }

            DrawRim(canvas, s, x, y, r, stretch, angle, screenWidth, motion, drawAlpha);
        }
        DrawDrops(canvas, s, dpiScale, drawAlpha);
    }

    /// <summary>Drops the kept small surface (the lens went off).</summary>
    public void Release()
    {
        _small?.Dispose(); _small = null; _smallW = _smallH = 0;
    }

    public void Dispose()
    {
        Release();
        _maskPaint?.Dispose(); _rimPaint?.Dispose(); _dropPaint?.Dispose();
        _maskPaint = _rimPaint = _dropPaint = null;
    }

    private static void DrawBase(SKCanvas canvas, SKImage image, SKRect dest, SKPaint basePaint, SKPaint? wave)
    {
        if (wave != null) canvas.DrawRect(dest, wave);
        else canvas.DrawImage(image, dest, basePaint);
    }

    // The lens box without a screen-sized offscreen layer (measured 2026-11-17: the layer cost up to
    // 16 ms a frame at 1440p with a wide lens, this costs under 2). The blurred capture is small, so the
    // hole is cut out of the box's share of it at ITS size, on a small kept Bgra8888 surface, and the
    // result is stretched over the box exactly as the whole image would be: same texels, same filter.
    private bool DrawLensSmall(SKCanvas canvas, SKImage image, SKRect dest, SKPaint basePaint, SKRect lens,
                               float x, float y, float r, float stretch, float angle)
    {
        if (image.Width <= 0 || image.Height <= 0 || dest.Width <= 0 || dest.Height <= 0) return false;
        float sx = dest.Width / image.Width, sy = dest.Height / image.Height;
        // The box in the capture's own pixels, one pixel wider all round so the stretch filters the
        // same neighbours the whole image would; the extra is clipped away below.
        int tx0 = Math.Max(0, (int)Math.Floor((lens.Left - dest.Left) / sx) - 1), ty0 = Math.Max(0, (int)Math.Floor((lens.Top - dest.Top) / sy) - 1);
        int tx1 = Math.Min(image.Width, (int)Math.Ceiling((lens.Right - dest.Left) / sx) + 1), ty1 = Math.Min(image.Height, (int)Math.Ceiling((lens.Bottom - dest.Top) / sy) + 1);
        int tw = tx1 - tx0, th = ty1 - ty0;
        if (tw <= 0 || th <= 0) return true;   // the box misses this capture: nothing to draw
        if (_small == null || _smallW < tw || _smallH < th)
        {
            _small?.Dispose();
            _smallW = Math.Max(_smallW, tw); _smallH = Math.Max(_smallH, th);
            _small = SKSurface.Create(new SKImageInfo(_smallW, _smallH, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (_small == null) { _smallW = _smallH = 0; return false; }
        }
        var sc = _small.Canvas;
        sc.Save();
        sc.ClipRect(new SKRect(0, 0, tw, th));
        sc.Clear(SKColors.Transparent);
        sc.DrawImage(image, -tx0, -ty0);
        sc.Scale(1 / sx, 1 / sy);
        sc.Translate(-(dest.Left + tx0 * sx), -(dest.Top + ty0 * sy));
        sc.Translate(x, y);
        sc.RotateRadians(angle);
        sc.Scale(r * (1f + stretch), r);
        sc.DrawCircle(0, 0, 1, MaskPaint());
        sc.Restore();
        using var holed = _small.Snapshot(new SKRectI(0, 0, tw, th));
        if (holed == null) return false;
        canvas.Save();
        canvas.ClipRect(lens);
        canvas.DrawImage(holed, new SKRect(dest.Left + tx0 * sx, dest.Top + ty0 * sy, dest.Left + tx1 * sx, dest.Top + ty1 * sy), basePaint);
        canvas.Restore();
        return true;
    }

    private void DrawRim(SKCanvas canvas, UndertowState s, float x, float y, float r, float stretch, float angle,
                         double screenWidth, UndertowMotion motion, byte drawAlpha)
    {
        double a = UndertowLens.RimAlpha(s, screenWidth, motion) * (drawAlpha / 255.0);
        byte alpha = (byte)Math.Clamp(Math.Round(a * 255), 0, 255);
        if (alpha == 0) return;
        _rimPaint ??= new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.Screen,   // tinted and low: it can only lift the feather, never wash it white
            Shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1f,
                new[] { Tint.WithAlpha(0), Tint.WithAlpha(0), Tint, Tint.WithAlpha(0) },
                new[] { 0f, 0.58f, 0.8f, 1f }, SKShaderTileMode.Clamp),
        };
        _rimPaint.Color = SKColors.White.WithAlpha(alpha);
        canvas.Save();
        canvas.Translate(x, y);
        canvas.RotateRadians(angle);
        canvas.Scale(r * (1f + stretch), r);
        canvas.DrawCircle(0, 0, 1, _rimPaint);
        canvas.Restore();
    }

    private void DrawDrops(SKCanvas canvas, UndertowState s, double dpiScale, byte drawAlpha)
    {
        _dropPaint ??= new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Screen };
        float px = (float)(dpiScale > 0 ? dpiScale : 1.0);
        foreach (var d in s.Drops)
        {
            if (!d.Live) continue;
            double u = Math.Clamp(d.Age / UndertowLens.DropLife, 0, 1);
            double fadeIn = Math.Min(1, d.Age / 0.06);   // grows in over a few frames, never pops
            byte a = (byte)Math.Clamp(Math.Round(200 * fadeIn * (1 - u) * (1 - u) * (drawAlpha / 255.0)), 0, 255);
            if (a == 0) continue;
            _dropPaint.Color = new SKColor(0xFF, 0xE4, 0xF4, a);
            canvas.DrawCircle((float)d.X, (float)d.Y, 2.4f * px * (float)(1 - 0.5 * u), _dropPaint);
        }
    }

    private SKPaint MaskPaint()
    {
        if (_maskPaint != null) return _maskPaint;
        var colors = new SKColor[UndertowLens.MaskAlpha.Length];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = SKColors.Black.WithAlpha((byte)Math.Round(UndertowLens.MaskAlpha[i] * 255));
        _maskPaint = new SKPaint
        {
            IsAntialias = true,
            BlendMode = SKBlendMode.DstOut,
            Shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1f, colors,
                                                    UndertowLens.MaskPositions, SKShaderTileMode.Clamp),
        };
        return _maskPaint;
    }
}
