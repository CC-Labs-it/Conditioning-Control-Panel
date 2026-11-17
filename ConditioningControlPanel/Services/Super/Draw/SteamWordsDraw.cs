using System;
using System.Globalization;
using System.Runtime.InteropServices;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws one monitor's <see cref="SteamWords"/> over its fog: light showing through where a
/// finger went. Each word is laid out once, at the monitor's own resolution, into two small
/// alpha pictures (the letters, and a soft bloom of them); a frame only places those pictures
/// through the reveal mask, so nothing big is rebuilt while a word is on screen.
/// IN: the bloom comes up first and the letters write across, rising a little into place.
/// OUT: the letters thin, the bloom lingers, and the lot drifts up and spreads as it goes.
/// Fades only. Skia only, no WPF and no App, so the Avalonia head can take this file as it is.
/// One instance per monitor; it owns its pictures.
/// </summary>
public sealed class SteamWordsDraw : IDisposable
{
    /// <summary>The words at their brightest (0..1).</summary>
    public const double Strength = 0.9;

    private sealed class Laid : IDisposable
    {
        public int Serial = -1, BoxW, BoxH;
        public SKTypeface? Face;
        public SKImage? Letters, Bloom;
        public float W, H, Size;
        public void Dispose() { Letters?.Dispose(); Bloom?.Dispose(); Letters = null; Bloom = null; Serial = -1; }
    }

    private Laid[] _laid = Array.Empty<Laid>();
    private SKBitmap? _mask;
    private byte[] _maskPx = Array.Empty<byte>();
    private float[] _raw = Array.Empty<float>(), _row = Array.Empty<float>();
    private readonly SKPaint _paint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };

    /// <summary>How many times a word has been laid out (tests: a held word is laid out once).</summary>
    public int Layouts { get; private set; }

    /// <summary>
    /// <paramref name="bounds"/> is where the monitor sits on the canvas, <paramref name="fog"/>
    /// the fog's colour (the words lean a little toward it). Draws nothing while no word shows.
    /// </summary>
    public void Draw(SKCanvas canvas, SKRect bounds, SteamField f, SteamWords words, SKColor fog, SKTypeface? face)
    {
        if (!words.Any || bounds.Width < 8 || bounds.Height < 8) return;
        var slots = words.Slots;
        if (_laid.Length != slots.Count)
        {
            foreach (var l in _laid) l.Dispose();
            _laid = new Laid[slots.Count];
            for (int i = 0; i < _laid.Length; i++) _laid[i] = new Laid();
        }
        BuildMask(f);
        // Mask cell (gx, gy) covers the same share of the monitor as the fog cell does.
        var maskToCanvas = SKMatrix.Concat(SKMatrix.CreateTranslation(bounds.Left, bounds.Top),
            SKMatrix.CreateScale(bounds.Width / f.W, bounds.Height / f.H));
        var lettersTint = Lean(fog, 0.95);
        var bloomTint = Lean(fog, 0.72);

        for (int k = 0; k < slots.Count; k++)
        {
            var s = slots[k];
            if (s.Alpha <= 0.002 || string.IsNullOrEmpty(s.Text)) continue;
            bool side = Math.Abs(s.Turn) > 45;
            int boxW = (int)(s.Wide * bounds.Width), boxH = (int)(s.High * bounds.Height);
            var laid = _laid[k];
            if (laid.Serial != s.Serial || laid.BoxW != boxW || laid.BoxH != boxH || !ReferenceEquals(laid.Face, face))
                Lay(laid, s, side ? boxH : boxW, side ? boxW : boxH, boxW, boxH, face);
            if (laid.Letters == null || laid.Bloom == null) continue;

            double rise = 1 - EaseOut(s.Write), melt = s.Melt * s.Melt * (3 - 2 * s.Melt);
            float dy = (float)(rise * laid.Size * 0.14 - melt * laid.Size * 0.20);
            float grow = (float)(1 + 0.06 * melt);
            var m = SKMatrix.CreateTranslation(bounds.Left + (float)s.X * bounds.Width, bounds.Top + (float)s.Y * bounds.Height);
            m = SKMatrix.Concat(m, SKMatrix.CreateRotationDegrees((float)s.Turn));
            m = SKMatrix.Concat(m, SKMatrix.CreateTranslation(0, dy));
            m = SKMatrix.Concat(m, SKMatrix.CreateScale(grow, grow));
            if (!m.TryInvert(out var back)) continue;
            var rect = new SKRect(-laid.W / 2, -laid.H / 2, laid.W / 2, laid.H / 2);
            var place = SKMatrix.CreateTranslation(rect.Left, rect.Top);

            canvas.Save();
            canvas.Concat(ref m);
            using var mask = SKShader.CreateBitmap(_mask!, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKMatrix.Concat(back, maskToCanvas));

            // The bloom: under the letters, first in and last out.
            using (var img = laid.Bloom.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, place))
            using (var sh = SKShader.CreateCompose(img, mask, SKBlendMode.DstIn))
            using (var tint = SKColorFilter.CreateBlendMode(bloomTint, SKBlendMode.SrcIn))
            {
                _paint.Shader = sh; _paint.ColorFilter = tint;
                _paint.Color = SKColors.White.WithAlpha(Byte(s.Alpha * (0.55 + 0.3 * melt) * Strength));
                canvas.DrawRect(rect, _paint);
            }

            // The letters: written across while the word comes in, thinning first as it goes.
            using (var img = laid.Letters.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, place))
            using (var revealed = SKShader.CreateCompose(img, mask, SKBlendMode.DstIn))
            using (var tint = SKColorFilter.CreateBlendMode(lettersTint, SKBlendMode.SrcIn))
            {
                SKShader? front = null, written = null;
                if (s.Write < 1)
                {
                    float soft = laid.W * 0.3f;
                    float x = rect.Left + (float)(s.Write * s.Write * (3 - 2 * s.Write)) * (laid.W + soft);
                    front = SKShader.CreateLinearGradient(new SKPoint(x - soft, 0), new SKPoint(x, 0),
                        new[] { SKColors.White, SKColors.Transparent }, null, SKShaderTileMode.Clamp);
                    written = SKShader.CreateCompose(revealed, front, SKBlendMode.DstIn);
                }
                _paint.Shader = written ?? revealed; _paint.ColorFilter = tint;
                _paint.Color = SKColors.White.WithAlpha(Byte(s.Alpha * (1 - 0.7 * melt) * Strength));
                canvas.DrawRect(rect, _paint);
                written?.Dispose(); front?.Dispose();
            }
            _paint.Shader = null; _paint.ColorFilter = null;
            canvas.Restore();
        }
    }

    /// <summary>
    /// The reveal: wiped once and fogged over again, softened across neighbouring cells so the
    /// stretch up to the monitor shows no cell and no step.
    /// </summary>
    private void BuildMask(SteamField f)
    {
        int w = f.W, h = f.H, n = w * h;
        if (_mask == null || _mask.Width != w || _mask.Height != h)
        {
            _mask?.Dispose();
            _mask = new SKBitmap(new SKImageInfo(w, h, SKColorType.Alpha8, SKAlphaType.Premul));
            _maskPx = new byte[n]; _raw = new float[n]; _row = new float[n];
        }
        for (int i = 0; i < n; i++)
            _raw[i] = f.Mem[i] * Math.Clamp(f.Fog[i] * (float)SteamWords.FogBack, 0f, 1f);
        // 1-2-1 across, then down.
        for (int y = 0, i = 0; y < h; y++)
            for (int x = 0; x < w; x++, i++)
                _row[i] = (_raw[x > 0 ? i - 1 : i] + 2 * _raw[i] + _raw[x < w - 1 ? i + 1 : i]) * 0.25f;
        for (int y = 0, i = 0; y < h; y++)
            for (int x = 0; x < w; x++, i++)
            {
                float v = (_row[y > 0 ? i - w : i] + 2 * _row[i] + _row[y < h - 1 ? i + w : i]) * 0.25f;
                _maskPx[i] = (byte)(Math.Clamp(v, 0f, 1f) * 255f);
            }
        // Rows of an Alpha8 bitmap may be padded: copy row by row.
        IntPtr px = _mask.GetPixels();
        int stride = _mask.RowBytes;
        for (int y = 0; y < h; y++) Marshal.Copy(_maskPx, y * w, px + y * stride, w);
        _mask.NotifyPixelsChanged();
    }

    /// <summary>Lay a word out once: letters a little apart, fitted to the slot's long side.</summary>
    private void Lay(Laid laid, SteamWordSlot s, int longPx, int shortPx, int boxW, int boxH, SKTypeface? face)
    {
        laid.Dispose();
        laid.Serial = s.Serial; laid.BoxW = boxW; laid.BoxH = boxH; laid.Face = face;
        Layouts++;
        var info = new StringInfo(s.Text);
        int count = info.LengthInTextElements;
        if (count == 0 || longPx < 8 || shortPx < 8) return;
        using var text = new SKPaint { IsAntialias = true, SubpixelText = true, Color = SKColors.White, Typeface = face };
        float size = shortPx * 0.70f;
        var parts = new string[count];
        var at = new float[count];
        float width = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            text.TextSize = size;
            float gap = size * 0.07f;
            width = 0;
            for (int i = 0; i < count; i++)
            {
                parts[i] = info.SubstringByTextElements(i, 1);
                at[i] = width;
                width += text.MeasureText(parts[i]) + (i < count - 1 ? gap : 0);
            }
            if (width <= longPx || pass == 1) break;
            size *= longPx / width;
        }
        var metrics = text.FontMetrics;
        float pad = (float)Math.Ceiling(size * 0.5f);
        float asc = -metrics.Ascent, desc = metrics.Descent;
        int iw = (int)Math.Ceiling(width + pad * 2), ih = (int)Math.Ceiling(asc + desc + pad * 2);
        var a8 = new SKImageInfo(iw, ih, SKColorType.Alpha8, SKAlphaType.Premul);

        using (var surf = SKSurface.Create(a8))
        {
            if (surf == null) return;
            surf.Canvas.Clear(SKColors.Transparent);
            for (int i = 0; i < count; i++) surf.Canvas.DrawText(parts[i], pad + at[i], pad + asc, text);
            // The feet of the letters sink a little into the fog.
            using (var sink = SKShader.CreateLinearGradient(new SKPoint(0, pad + asc * 0.35f), new SKPoint(0, pad + asc + desc),
                       new[] { SKColors.White, SKColors.White.WithAlpha(190) }, null, SKShaderTileMode.Clamp))
            using (var fade = new SKPaint { Shader = sink, BlendMode = SKBlendMode.DstIn })
                surf.Canvas.DrawRect(new SKRect(0, 0, iw, ih), fade);
            laid.Letters = surf.Snapshot();
        }
        using (var surf = SKSurface.Create(a8))
        {
            if (surf == null) { laid.Dispose(); return; }
            surf.Canvas.Clear(SKColors.Transparent);
            // Two blurs, a close one and a wide one: light spreading in fog, not an outline.
            foreach (var (sigma, alpha) in new[] { (size * 0.15f, 150), (size * 0.05f, 185) })
            {
                using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
                text.MaskFilter = blur; text.Color = SKColors.White.WithAlpha((byte)alpha);
                for (int i = 0; i < count; i++) surf.Canvas.DrawText(parts[i], pad + at[i], pad + asc, text);
                text.MaskFilter = null;
            }
            laid.Bloom = surf.Snapshot();
        }
        laid.W = iw; laid.H = ih; laid.Size = size;
    }

    private static SKColor Lean(SKColor fog, double toWhite)
        => new((byte)(fog.Red + (255 - fog.Red) * toWhite), (byte)(fog.Green + (255 - fog.Green) * toWhite),
               (byte)(fog.Blue + (255 - fog.Blue) * toWhite));

    private static double EaseOut(double x) { x = 1 - Math.Clamp(x, 0, 1); return 1 - x * x * x; }
    private static byte Byte(double a) => (byte)Math.Round(Math.Clamp(a, 0, 1) * 255);

    public void Dispose()
    {
        foreach (var l in _laid) l.Dispose();
        _laid = Array.Empty<Laid>();
        _mask?.Dispose(); _mask = null;
        _paint.Dispose();
    }
}
