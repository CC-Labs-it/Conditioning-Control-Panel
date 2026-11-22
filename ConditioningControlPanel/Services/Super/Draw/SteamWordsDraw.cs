using System;
using System.Globalization;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Services.Looks;
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
/// Fades only. Skia only, no WPF and no App, so the Avalonia head can take this file as it is
/// (with Services/Looks/WordStyleSkia.cs for a worn word style).
/// A worn word style (shop slot <c>word</c>) lays the word out in the style's face and colour
/// and plays the style's own way in over the word's life (<see cref="SteamWordSlot.Life"/>):
/// typed letter by letter with a caret, chalked across, drifted up, echoed, lit bulb by bulb.
/// Each letter is a column of the same two pictures, so a frame still only places pictures.
/// Classic draws exactly the write-on above.
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
        // A worn style only: its face, the sign's lit fill (Marquee), and each letter's pen place,
        // width and column (Count + 1 edges) in picture px.
        public WordStyle Style;
        public WordFace? Look;
        public SKImage? Lit;
        public float[] At = Array.Empty<float>(), Wd = Array.Empty<float>(), Col = Array.Empty<float>();
        public int Count;
        public float Pad, Baseline, Mid;
        public void Dispose() { Letters?.Dispose(); Bloom?.Dispose(); Lit?.Dispose(); Letters = null; Bloom = null; Lit = null; Serial = -1; }
    }

    private readonly WordFrame _frame = new();

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
    public void Draw(SKCanvas canvas, SKRect bounds, SteamField f, SteamWords words, SKColor fog, SKTypeface? face,
        WordStyle style = WordStyle.Classic, WordMotion motion = WordMotion.Full)
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
            if (laid.Serial != s.Serial || laid.BoxW != boxW || laid.BoxH != boxH || !ReferenceEquals(laid.Face, face) || laid.Style != style)
            {
                if (style == WordStyle.Classic) Lay(laid, s, side ? boxH : boxW, side ? boxW : boxH, boxW, boxH, face);
                else LayStyled(laid, s, side ? boxH : boxW, side ? boxW : boxH, boxW, boxH, face, style);
            }
            if (laid.Letters == null || laid.Bloom == null) continue;
            if (style != WordStyle.Classic)
            {
                DrawStyled(canvas, bounds, s, laid, maskToCanvas, lettersTint, bloomTint, motion);
                continue;
            }

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
        laid.Style = WordStyle.Classic; laid.Look = null;
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

    /// <summary>
    /// One worn word: the classic place, turn and melt, with the style's frame in place of the
    /// write-on. Settled (every letter in, nothing moving) it is the same two rects as classic;
    /// while it moves, each letter is its own column of the pictures, moved and faded alone. The
    /// reveal mask stays where the fog is, so a letter drifting up rises through it.
    /// </summary>
    private void DrawStyled(SKCanvas canvas, SKRect bounds, SteamWordSlot s, Laid laid, SKMatrix maskToCanvas,
        SKColor lettersTint, SKColor bloomTint, WordMotion motion)
    {
        var look = laid.Look!;
        if (look.Colour is uint c)
        {
            var own = WordStyleSkia.Colour(c);
            lettersTint = Mix(own, lettersTint, 0.25);
            bloomTint = Mix(own, bloomTint, 0.5);
        }
        // Gentle visuals always reach here as Still, where the life does not matter.
        WordStyles.Fill(laid.Style, laid.Count, s.Life, SteamWords.LifeFor(false), motion, _frame);
        var fr = _frame;

        double melt = s.Melt * s.Melt * (3 - 2 * s.Melt);
        float dy = (float)(-melt * laid.Size * 0.20);
        float grow = (float)(1 + 0.06 * melt);
        var m = SKMatrix.CreateTranslation(bounds.Left + (float)s.X * bounds.Width, bounds.Top + (float)s.Y * bounds.Height);
        m = SKMatrix.Concat(m, SKMatrix.CreateRotationDegrees((float)(s.Turn + look.Tilt)));
        m = SKMatrix.Concat(m, SKMatrix.CreateTranslation(0, dy));
        m = SKMatrix.Concat(m, SKMatrix.CreateScale(grow, grow));
        if (!m.TryInvert(out var back)) return;
        var rect = new SKRect(-laid.W / 2, -laid.H / 2, laid.W / 2, laid.H / 2);
        var place = SKMatrix.CreateTranslation(rect.Left, rect.Top);
        double letters = s.Alpha * (1 - 0.7 * melt) * Strength;
        double bloom = s.Alpha * (0.55 + 0.3 * melt) * Strength;

        bool settled = fr.GhostCount == 0 && fr.CursorAlpha <= 0;
        for (int i = 0; settled && i < fr.Count; i++)
        {
            var l = fr.Letters[i];
            settled = l.Alpha >= 1 && l.Reveal >= 1 && l.Lit >= 1 && l.Dy == 0 && l.Dx == 0;
        }

        canvas.Save();
        canvas.Concat(ref m);
        using var mask = SKShader.CreateBitmap(_mask!, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, SKMatrix.Concat(back, maskToCanvas));
        using var bloomTintF = SKColorFilter.CreateBlendMode(bloomTint, SKBlendMode.SrcIn);
        using var lettersTintF = SKColorFilter.CreateBlendMode(lettersTint, SKBlendMode.SrcIn);
        using var bloomImg = laid.Bloom!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, place);
        using var bloomSh = SKShader.CreateCompose(bloomImg, mask, SKBlendMode.DstIn);
        using var lettersImg = laid.Letters!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, place);
        using var lettersSh = SKShader.CreateCompose(lettersImg, mask, SKBlendMode.DstIn);
        using var litImg = laid.Lit?.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, place);
        using var litSh = litImg == null ? null : SKShader.CreateCompose(litImg, mask, SKBlendMode.DstIn);

        if (settled)
        {
            Paint(canvas, rect, bloomSh, bloomTintF, bloom);
            Paint(canvas, rect, lettersSh, lettersTintF, letters);
            if (litSh != null) Paint(canvas, rect, litSh, lettersTintF, letters);
        }
        else
        {
            // The echo's swelling copies, under the word, scaled about its middle.
            for (int g = 0; g < fr.GhostCount; g++)
            {
                var gh = fr.Ghosts[g];
                float sc = (float)gh.Scale;
                using var img = laid.Letters!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                    SKMatrix.Concat(SKMatrix.CreateScale(sc, sc), place));
                using var sh = SKShader.CreateCompose(img, mask, SKBlendMode.DstIn);
                Paint(canvas, new SKRect(rect.Left * sc, rect.Top * sc, rect.Right * sc, rect.Bottom * sc), sh, lettersTintF, letters * gh.Alpha);
            }
            int n = Math.Min(fr.Count, laid.Count);
            for (int i = 0; i < n; i++)
            {
                var l = fr.Letters[i];
                if (l.Alpha <= 0.002 || l.Reveal <= 0) continue;
                float x0 = rect.Left + laid.Col[i], x1 = rect.Left + laid.Col[i + 1];
                if (l.Reveal < 1) x1 = x0 + (x1 - x0) * (float)l.Reveal;
                float ox = (float)(l.Dx * laid.Size), oy = (float)(l.Dy * laid.Size);
                var col = new SKRect(x0 + ox, rect.Top + oy, x1 + ox, rect.Bottom + oy);
                double lit = laid.Lit != null ? l.Lit : 1;
                if (ox == 0 && oy == 0)
                {
                    Paint(canvas, col, bloomSh, bloomTintF, bloom * l.Alpha * lit);
                    Paint(canvas, col, lettersSh, lettersTintF, letters * l.Alpha);
                    if (litSh != null) Paint(canvas, col, litSh, lettersTintF, letters * l.Alpha * lit);
                    continue;
                }
                // A moving letter: its column of the pictures moves, the mask does not.
                var at = SKMatrix.CreateTranslation(rect.Left + ox, rect.Top + oy);
                using (var bi = laid.Bloom!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, at))
                using (var bs = SKShader.CreateCompose(bi, mask, SKBlendMode.DstIn))
                    Paint(canvas, col, bs, bloomTintF, bloom * l.Alpha * lit);
                using (var li = laid.Letters!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, at))
                using (var ls = SKShader.CreateCompose(li, mask, SKBlendMode.DstIn))
                    Paint(canvas, col, ls, lettersTintF, letters * l.Alpha);
                if (laid.Lit != null)
                {
                    using var ti = laid.Lit.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, at);
                    using var ts = SKShader.CreateCompose(ti, mask, SKBlendMode.DstIn);
                    Paint(canvas, col, ts, lettersTintF, letters * l.Alpha * lit);
                }
            }
            // The typewriter's caret, through the same mask.
            if (fr.CursorAlpha > 0)
            {
                int after = fr.CursorAfter;
                float x = rect.Left + (after >= 0 && after < laid.Count
                    ? laid.At[after] + laid.Wd[after] + laid.Size * 0.04f : laid.Pad);
                float mid = rect.Top + laid.Mid, half = laid.Size * 0.38f;
                Paint(canvas, new SKRect(x, mid - half, x + laid.Size * 0.08f, mid + half), mask, lettersTintF, letters * fr.CursorAlpha);
            }
        }
        _paint.Shader = null; _paint.ColorFilter = null;
        canvas.Restore();
    }

    private void Paint(SKCanvas canvas, SKRect r, SKShader shader, SKColorFilter tint, double alpha)
    {
        byte a = Byte(alpha);
        if (a == 0 || r.Width <= 0 || r.Height <= 0) return;
        _paint.Shader = shader; _paint.ColorFilter = tint;
        _paint.Color = SKColors.White.WithAlpha(a);
        canvas.DrawRect(r, _paint);
    }

    /// <summary>
    /// Lay a worn word out once, in the style's face (fitted to the slot like classic), into the
    /// letters picture (the sign's tube for Marquee, with its lit fill beside it), the bloom, and
    /// each letter's column: edges halfway between letters, so the columns tile the pictures.
    /// </summary>
    private void LayStyled(Laid laid, SteamWordSlot s, int longPx, int shortPx, int boxW, int boxH, SKTypeface? face, WordStyle style)
    {
        laid.Dispose();
        laid.Serial = s.Serial; laid.BoxW = boxW; laid.BoxH = boxH; laid.Face = face;
        laid.Style = style;
        Layouts++;
        var look = WordStyles.FaceOf(style);
        laid.Look = look;
        string text = WordStyles.TextFor(style, s.Text);
        if (WordStyles.LetterCount(text) == 0 || longPx < 8 || shortPx < 8) return;
        var drawFace = WordStyleSkia.FaceFor(look, face ?? SKTypeface.Default);
        var fs = look.Bold ? SKFontStyle.Bold : SKFontStyle.Normal;
        float size = (float)(shortPx * 0.70f * look.Size);
        StyledLine line = StyledLine.Build(text, drawFace, fs, size);
        float Width(StyledLine l) => l.Width + l.Size * 0.07f * Math.Max(0, l.Count - 1);
        if (Width(line) > longPx)
        {
            size *= longPx / Width(line);
            line = StyledLine.Build(text, drawFace, fs, size);
        }
        int count = line.Count;
        float gap = size * 0.07f, width = Width(line);
        float pad = (float)Math.Ceiling(size * 0.5f);
        float asc = -line.Ascent, desc = line.Descent;
        int iw = (int)Math.Ceiling(width + pad * 2), ih = (int)Math.Ceiling(asc + desc + pad * 2);
        float baseline = pad + asc;
        laid.Count = count; laid.Pad = pad; laid.Baseline = baseline; laid.Mid = baseline + (line.Ascent + line.Descent) / 2f;
        laid.At = new float[count]; laid.Wd = new float[count]; laid.Col = new float[count + 1];
        for (int i = 0; i < count; i++) { laid.At[i] = pad + line.At[i] + gap * i; laid.Wd[i] = line.Widths[i]; }
        laid.Col[0] = 0; laid.Col[count] = iw;
        for (int i = 1; i < count; i++) laid.Col[i] = (laid.At[i - 1] + laid.Wd[i - 1] + laid.At[i]) / 2f;
        var a8 = new SKImageInfo(iw, ih, SKColorType.Alpha8, SKAlphaType.Premul);

        using var text8 = new SKPaint { IsAntialias = true, SubpixelText = true, Color = SKColors.White, TextSize = size };
        void Letters(SKCanvas c)
        {
            for (int i = 0; i < count; i++) { text8.Typeface = line.Faces[i]; c.DrawText(line.Parts[i], laid.At[i], baseline, text8); }
        }
        void Sink(SKSurface surf)
        {
            // The feet of the letters sink a little into the fog (as classic).
            using var sink = SKShader.CreateLinearGradient(new SKPoint(0, pad + asc * 0.35f), new SKPoint(0, pad + asc + desc),
                new[] { SKColors.White, SKColors.White.WithAlpha(190) }, null, SKShaderTileMode.Clamp);
            using var fade = new SKPaint { Shader = sink, BlendMode = SKBlendMode.DstIn };
            surf.Canvas.DrawRect(new SKRect(0, 0, iw, ih), fade);
        }

        using (var surf = SKSurface.Create(a8))
        {
            if (surf == null) return;
            surf.Canvas.Clear(SKColors.Transparent);
            if (look.Bulbs)
            {
                // The sign's tube: always there, thin and dim; the bulbs light in the second picture.
                text8.Style = SKPaintStyle.Stroke;
                text8.StrokeWidth = Math.Max(1, size * 0.025f);
                text8.Color = SKColors.White.WithAlpha(153);
                Letters(surf.Canvas);
                text8.Style = SKPaintStyle.Fill; text8.Color = SKColors.White;
            }
            else if (look.Rough)
            {
                // Chalk: a fixed wobble, laid once, so the edge never crawls.
                using var rough = SKPathEffect.CreateDiscrete(Math.Max(1.5f, size * 0.035f), Math.Max(0.6f, size * 0.012f), 7);
                text8.PathEffect = rough;
                Letters(surf.Canvas);
                text8.PathEffect = null;
            }
            else Letters(surf.Canvas);
            Sink(surf);
            laid.Letters = surf.Snapshot();
        }
        if (look.Bulbs)
        {
            using var surf = SKSurface.Create(a8);
            if (surf == null) { laid.Dispose(); return; }
            surf.Canvas.Clear(SKColors.Transparent);
            using (var glow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1, size * 0.09f)))
            {
                text8.MaskFilter = glow; text8.Color = SKColors.White.WithAlpha(190);
                Letters(surf.Canvas);
                text8.MaskFilter = null; text8.Color = SKColors.White;
            }
            Letters(surf.Canvas);
            Sink(surf);
            laid.Lit = surf.Snapshot();
        }
        using (var surf = SKSurface.Create(a8))
        {
            if (surf == null) { laid.Dispose(); return; }
            surf.Canvas.Clear(SKColors.Transparent);
            // Two blurs, a close one and a wide one: light spreading in fog, not an outline.
            foreach (var (sigma, alpha) in new[] { (size * 0.15f, 150), (size * 0.05f, 185) })
            {
                using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma);
                text8.MaskFilter = blur; text8.Color = SKColors.White.WithAlpha((byte)alpha);
                Letters(surf.Canvas);
                text8.MaskFilter = null;
            }
            laid.Bloom = surf.Snapshot();
        }
        laid.W = iw; laid.H = ih; laid.Size = size;
    }

    private static SKColor Mix(SKColor a, SKColor b, double towardB)
        => new((byte)(a.Red + (b.Red - a.Red) * towardB), (byte)(a.Green + (b.Green - a.Green) * towardB),
               (byte)(a.Blue + (b.Blue - a.Blue) * towardB));

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
