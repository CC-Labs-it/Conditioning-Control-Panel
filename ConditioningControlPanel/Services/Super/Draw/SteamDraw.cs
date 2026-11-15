using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws one monitor's <see cref="SteamField"/>: the fog as a small bitmap stretched soft over the
/// monitor, and the words that come back in fog that was wiped. Skia only, no WPF and no App, so
/// the Avalonia head can take this file as it is. One instance per monitor; it owns its bitmaps.
/// </summary>
public sealed class SteamDraw : IDisposable
{
    private const int WordScale = 4;   // the words are composed at a quarter of the monitor
    private SKBitmap? _fog, _mem;
    private byte[] _fogPx = Array.Empty<byte>(), _memPx = Array.Empty<byte>();
    private SKSurface? _words;
    private int _wordsW, _wordsH;
    private readonly SKPaint _soft = new() { FilterQuality = SKFilterQuality.Medium };
    private readonly SKPaint _mask = new() { FilterQuality = SKFilterQuality.Medium, BlendMode = SKBlendMode.DstIn };
    private readonly SKPaint _text = new() { IsAntialias = true, Color = SKColors.White, TextAlign = SKTextAlign.Center };
    private readonly SKPaint _wordsOut = new() { FilterQuality = SKFilterQuality.Low };

    /// <summary>
    /// <paramref name="bounds"/> is where the monitor sits on the canvas. <paramref name="maxAlpha"/>
    /// is the fog at its thickest (0..1). <paramref name="words"/> are written around the edges;
    /// <paramref name="line"/> (may be null) across the middle. <paramref name="t"/> drifts the cloud.
    /// </summary>
    public void Draw(SKCanvas canvas, SKRect bounds, SteamField f, SKColor colour, double maxAlpha, IReadOnlyList<string> words, string? line, SKTypeface? face, double t)
    {
        int n = f.W * f.H;
        if (_fog == null || _fog.Width != f.W || _fog.Height != f.H)
        {
            _fog?.Dispose(); _mem?.Dispose();
            var info = new SKImageInfo(f.W, f.H, SKColorType.Rgba8888, SKAlphaType.Premul);
            _fog = new SKBitmap(info); _mem = new SKBitmap(info);
            _fogPx = new byte[n * 4]; _memPx = new byte[n * 4];
        }
        bool anyMem = false;
        for (int gy = 0, i = 0, j = 0; gy < f.H; gy++)
        {
            for (int gx = 0; gx < f.W; gx++, i++, j += 4)
            {
                double fog = f.Fog[i];
                // Slow uneven drift. Two crossed waves bent by a third, so no grid shows.
                double bend = Math.Sin(gx * 0.045 - gy * 0.06 + t * 0.17) * 2.2;
                double cloud = 0.86 + 0.09 * Math.Sin(gx * 0.085 + gy * 0.05 + bend + t * 0.23)
                                    + 0.05 * Math.Sin(gx * 0.031 - gy * 0.12 - bend * 0.7 - t * 0.19);
                double a = Math.Clamp(fog * cloud * maxAlpha, 0, 1);
                _fogPx[j] = (byte)(colour.Red * a); _fogPx[j + 1] = (byte)(colour.Green * a);
                _fogPx[j + 2] = (byte)(colour.Blue * a); _fogPx[j + 3] = (byte)(a * 255);
                double m = f.Mem[i] * Math.Clamp(fog * 1.7, 0, 1);
                byte mb = (byte)(m * 255);
                if (mb > 6) anyMem = true;
                _memPx[j] = _memPx[j + 1] = _memPx[j + 2] = _memPx[j + 3] = mb;
            }
        }
        Marshal.Copy(_fogPx, 0, _fog.GetPixels(), _fogPx.Length);
        _fog.NotifyPixelsChanged();
        canvas.DrawBitmap(_fog, bounds, _soft);

        if (!anyMem || (words.Count == 0 && string.IsNullOrEmpty(line))) return;
        Marshal.Copy(_memPx, 0, _mem!.GetPixels(), _memPx.Length);
        _mem.NotifyPixelsChanged();
        int ww = Math.Max(8, (int)(bounds.Width / WordScale)), wh = Math.Max(8, (int)(bounds.Height / WordScale));
        if (_words == null || _wordsW != ww || _wordsH != wh)
        {
            _words?.Dispose();
            _words = SKSurface.Create(new SKImageInfo(ww, wh, SKColorType.Rgba8888, SKAlphaType.Premul));
            _wordsW = ww; _wordsH = wh;
        }
        if (_words == null) return;
        var c = _words.Canvas;
        c.Clear(SKColors.Transparent);
        _text.Typeface = face;
        float size = wh * 0.13f;
        _text.TextSize = size;
        // Six places around the edges, the way a finger would write on a steamed mirror.
        for (int k = 0; k < Math.Min(6, words.Count); k++)
        {
            string w = words[k];
            switch (k)
            {
                case 0: c.DrawText(w, ww * 0.2f, wh * 0.16f, _text); break;
                case 1: c.DrawText(w, ww * 0.77f, wh * 0.92f, _text); break;
                case 2: c.DrawText(w, ww * 0.84f, wh * 0.2f, _text); break;
                case 3: c.DrawText(w, ww * 0.17f, wh * 0.9f, _text); break;
                case 4: c.Save(); c.Translate(ww * 0.075f, wh * 0.5f); c.RotateDegrees(-90); c.DrawText(w, 0, 0, _text); c.Restore(); break;
                default: c.Save(); c.Translate(ww * 0.925f, wh * 0.52f); c.RotateDegrees(90); c.DrawText(w, 0, 0, _text); c.Restore(); break;
            }
        }
        if (!string.IsNullOrEmpty(line))
        {
            _text.TextSize = size * 1.15f;
            float lw = _text.MeasureText(line);
            if (lw > ww * 0.86f) _text.TextSize *= ww * 0.86f / lw;
            c.DrawText(line, ww * 0.5f, wh * 0.53f, _text);
        }
        c.DrawBitmap(_mem, new SKRect(0, 0, ww, wh), _mask);
        canvas.Save();
        canvas.Translate(bounds.Left, bounds.Top);
        canvas.Scale(bounds.Width / ww, bounds.Height / wh);
        _wordsOut.Color = SKColors.White.WithAlpha(205);
        canvas.DrawSurface(_words, 0, 0, _wordsOut);
        canvas.Restore();
    }

    public void Dispose()
    {
        _fog?.Dispose(); _mem?.Dispose(); _words?.Dispose();
        _fog = null; _mem = null; _words = null;
        _soft.Dispose(); _mask.Dispose(); _text.Dispose(); _wordsOut.Dispose();
    }
}
