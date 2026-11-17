using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Two-tone on the pink filter's flat tint: the <see cref="FogMix"/> field painted at one even strength
/// on a small bitmap, stretched soft over the monitor, so the tint's strength stays exactly the filter's
/// and only its colour moves. Skia only, no WPF and no App (the Avalonia head can take it). One per layer;
/// it owns its bitmap and is reused frame to frame (a few thousand pixels, whatever the screen size).
/// </summary>
public sealed class FogMixDraw : IDisposable
{
    /// <summary>Grid cells across a screen. The field is smooth, so the stretch is soft, never blocky.</summary>
    public const int Columns = 64;

    private readonly FogMixField _field = new();
    private readonly SKPaint _soft = new() { FilterQuality = SKFilterQuality.Medium };
    private SKBitmap? _bmp;
    private byte[] _px = Array.Empty<byte>();
    private (byte R, byte G, byte B)[] _ramp = Array.Empty<(byte R, byte G, byte B)>();
    private (byte R, byte G, byte B) _rampA, _rampB;

    /// <summary>Paint the tint over <paramref name="bounds"/>: colour <paramref name="main"/> with
    /// <paramref name="second"/> moving through it, both at <paramref name="alpha"/> (0..1), at mix time <paramref name="t"/>.</summary>
    public void Draw(SKCanvas canvas, SKRect bounds, (byte R, byte G, byte B) main, (byte R, byte G, byte B) second, double alpha, double t)
    {
        if (alpha <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return;
        double aspect = bounds.Width / bounds.Height;
        int w = Columns, h = Math.Clamp((int)Math.Round(Columns / aspect), 12, Columns);
        _field.Fill(w, h, aspect, t);
        if (_ramp.Length == 0 || _rampA != main || _rampB != second)
        {
            _ramp = FogMix.Ramp(main, second);
            _rampA = main; _rampB = second;
        }
        if (_bmp == null || _bmp.Width != w || _bmp.Height != h)
        {
            _bmp?.Dispose();
            _bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            _px = new byte[w * h * 4];
        }
        double a = Math.Clamp(alpha, 0, 1);
        byte a8 = (byte)Math.Round(a * 255);
        int top = _ramp.Length - 1;
        var s = _field.S;
        for (int i = 0, j = 0; i < s.Length; i++, j += 4)
        {
            var c = _ramp[(int)(FogMix.Shape(s[i]) * top + 0.5)];
            _px[j] = (byte)(c.R * a); _px[j + 1] = (byte)(c.G * a); _px[j + 2] = (byte)(c.B * a); _px[j + 3] = a8;
        }
        Marshal.Copy(_px, 0, _bmp.GetPixels(), _px.Length);
        _bmp.NotifyPixelsChanged();
        canvas.DrawBitmap(_bmp, bounds, _soft);
    }

    public void Dispose()
    {
        _bmp?.Dispose();
        _bmp = null;
        _soft.Dispose();
    }
}
