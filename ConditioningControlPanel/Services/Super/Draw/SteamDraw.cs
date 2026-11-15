using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws one monitor's <see cref="SteamField"/>: the fog as a small bitmap stretched soft over the
/// monitor. Skia only, no WPF and no App, so
/// the Avalonia head can take this file as it is. One instance per monitor; it owns its bitmaps.
/// </summary>
public sealed class SteamDraw : IDisposable
{
    private SKBitmap? _fog;
    private byte[] _fogPx = Array.Empty<byte>();
    private readonly SKPaint _soft = new() { FilterQuality = SKFilterQuality.Medium };

    // The fog's texture: one small tile of smooth noise that wraps, read at three sizes that
    // slide past each other. Built once from a fixed seed so every monitor and every run match.
    private const int NoiseN = 64;
    private static readonly float[] Noise = MakeNoise();

    private static float[] MakeNoise()
    {
        var a = new float[NoiseN * NoiseN];
        uint seed = 0x9E3779B9;
        for (int i = 0; i < a.Length; i++)
        {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            a[i] = (seed & 0xFFFF) / 65535f;
        }
        return a;
    }

    /// <summary>Smooth noise 0..1 at a point of the wrapping tile (one unit = one lattice step).</summary>
    private static float NoiseAt(double x, double y)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y);
        float tx = (float)(x - fx), ty = (float)(y - fy);
        tx = tx * tx * tx * (tx * (tx * 6 - 15) + 10); ty = ty * ty * ty * (ty * (ty * 6 - 15) + 10);
        int x0 = (int)fx & (NoiseN - 1), y0 = (int)fy & (NoiseN - 1), x1 = (x0 + 1) & (NoiseN - 1), y1 = (y0 + 1) & (NoiseN - 1);
        float a = Noise[y0 * NoiseN + x0], b = Noise[y0 * NoiseN + x1], c = Noise[y1 * NoiseN + x0], d = Noise[y1 * NoiseN + x1];
        float top = a + (b - a) * tx, bot = c + (d - c) * tx;
        return top + (bot - top) * ty;
    }

    /// <summary>
    /// <paramref name="bounds"/> is where the monitor sits on the canvas. <paramref name="maxAlpha"/>
    /// is the fog at its thickest (0..1). The words are drawn by SteamWordsDraw now;
    /// words, line and face are unused here. <paramref name="t"/> drifts the cloud.
    /// </summary>
    public void Draw(SKCanvas canvas, SKRect bounds, SteamField f, SKColor colour, double maxAlpha, IReadOnlyList<string> words, string? line, SKTypeface? face, double t)
    {
        int n = f.W * f.H;
        if (_fog == null || _fog.Width != f.W || _fog.Height != f.H)
        {
            _fog?.Dispose();
            var info = new SKImageInfo(f.W, f.H, SKColorType.Rgba8888, SKAlphaType.Premul);
            _fog = new SKBitmap(info);
            _fogPx = new byte[n * 4];
        }
        for (int gy = 0, i = 0, j = 0; gy < f.H; gy++)
        {
            for (int gx = 0; gx < f.W; gx++, i++, j += 4)
            {
                double fog = f.Fog[i];
                // Wisps: big slow banks bend two finer layers that slide the other ways, so the
                // fog is seen to roll. The pattern rides with the fog: where the cursor or the
                // pool drags it (ShiftX/Y) the wisps are pulled along and spring back.
                double u = gx - f.ShiftX[i], v = gy - f.ShiftY[i];
                // Each layer reads the tile at its own slant, so no layer lines up with the grid
                // or with another layer.
                double bank = NoiseAt((u * 0.914 + v * 0.407) / 34 + t * 0.036, (v * 0.914 - u * 0.407) / 26 - t * 0.022);
                double bend = (bank - 0.5) * 12;
                double mid = NoiseAt((u * 0.788 - v * 0.616 + bend) / 15 - t * 0.11, (v * 0.788 + u * 0.616 - bend * 0.6) / 10 + t * 0.06);
                double fine = NoiseAt((u * 0.530 + v * 0.848 - bend * 0.5) / 7.5 + t * 0.18, (v * 0.530 - u * 0.848 + bend) / 5.5 - t * 0.1);
                double cloud = 0.56 + 0.26 * bank + 0.36 * mid + 0.17 * fine;
                // Thin fog breaks into wisps more than thick fog does.
                double thin = 1 - Math.Min(1, fog);
                cloud += (mid - 0.5) * 0.5 * thin;
                // Piled-up fog (above 1) only thickens by half as much: a soft bank, never a bright line.
                double thick = fog <= 1 ? fog : 1 + (fog - 1) * 0.5;
                double a = Math.Clamp(thick * cloud * maxAlpha, 0, 1);
                _fogPx[j] = (byte)(colour.Red * a); _fogPx[j + 1] = (byte)(colour.Green * a);
                _fogPx[j + 2] = (byte)(colour.Blue * a); _fogPx[j + 3] = (byte)(a * 255);
            }
        }
        Marshal.Copy(_fogPx, 0, _fog.GetPixels(), _fogPx.Length);
        _fog.NotifyPixelsChanged();
        canvas.DrawBitmap(_fog, bounds, _soft);
    }

    public void Dispose()
    {
        _fog?.Dispose();
        _fog = null;
        _soft.Dispose();
    }
}
