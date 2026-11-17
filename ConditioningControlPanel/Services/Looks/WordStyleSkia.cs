using System.Globalization;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Looks;

/// <summary>
/// A word laid out letter by letter for a styled draw: each text element, the face that can draw
/// it (Bugs #615 / #717 runs), and its pen position. Built once when a word starts; a frame only
/// places the letters.
/// </summary>
internal sealed class StyledLine
{
    public string[] Parts = Array.Empty<string>();
    public SKTypeface[] Faces = Array.Empty<SKTypeface>();
    public float[] At = Array.Empty<float>(), Widths = Array.Empty<float>();
    public float Width, Size, Ascent, Descent;
    public int Count => Parts.Length;

    public static StyledLine Build(string text, SKTypeface primary, SKFontStyle style, float size)
    {
        var line = new StyledLine { Size = size };
        var runs = GlyphFallback.Split(text, primary, style);
        var parts = new List<string>(text.Length);
        var faces = new List<SKTypeface>(text.Length);
        foreach (var run in runs)
        {
            var info = new StringInfo(run.Text);
            for (int i = 0; i < info.LengthInTextElements; i++) { parts.Add(info.SubstringByTextElements(i, 1)); faces.Add(run.Face); }
        }
        line.Parts = parts.ToArray();
        line.Faces = faces.ToArray();
        line.At = new float[parts.Count];
        line.Widths = new float[parts.Count];
        using var paint = new SKPaint { IsAntialias = true, TextSize = size };
        float x = 0, asc = 0, desc = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            paint.Typeface = faces[i];
            line.At[i] = x;
            line.Widths[i] = paint.MeasureText(parts[i]);
            x += line.Widths[i];
            var fm = paint.FontMetrics;
            if (fm.Ascent < asc) asc = fm.Ascent;
            if (fm.Descent > desc) desc = fm.Descent;
        }
        line.Width = x; line.Ascent = asc; line.Descent = desc;
        return line;
    }
}

/// <summary>
/// The Skia half of the word styles (<see cref="WordStyles"/> is the rule): resolves a style's
/// face and draws a <see cref="WordFrame"/> of a <see cref="StyledLine"/>. Each letter that is not
/// fully opaque is drawn (outline and fill together) through its own small layer, so a fading
/// letter fades whole instead of its outline showing through its fill.
/// </summary>
internal static class WordStyleSkia
{
    private static readonly Dictionary<string, SKTypeface?> _faces = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, SKMaskFilter> _glows = new();
    private static readonly Dictionary<int, SKPathEffect> _rough = new();
    private static readonly object _sync = new();

    /// <summary>The first installed family of the style's list, or <paramref name="own"/> (the
    /// surface's face) when the style keeps it or none is installed.</summary>
    public static SKTypeface FaceFor(WordFace face, SKTypeface own)
    {
        if (face.Families == null) return own;
        foreach (var name in face.Families)
        {
            var key = name + (face.Bold ? "|b" : "|n");
            SKTypeface? hit;
            lock (_sync)
            {
                if (!_faces.TryGetValue(key, out hit))
                {
                    try
                    {
                        var m = SKTypeface.FromFamilyName(name, face.Bold ? SKFontStyle.Bold : SKFontStyle.Normal);
                        hit = m != null && string.Equals(m.FamilyName, name, StringComparison.OrdinalIgnoreCase) ? m : null;
                    }
                    catch { hit = null; }
                    _faces[key] = hit;
                }
            }
            if (hit != null) return hit;
        }
        return own;
    }

    public static SKColor Colour(uint rgb, byte alpha = 255)
    {
        var (r, g, b) = WordStyles.Rgb(rgb);
        return new SKColor(r, g, b, alpha);
    }

    /// <summary>A sign's glow for a text size, kept per whole pixel of size (a handful of screens).</summary>
    private static SKMaskFilter Glow(float size)
    {
        int key = (int)size;
        lock (_sync)
        {
            if (!_glows.TryGetValue(key, out var g)) _glows[key] = g = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(1, key * 0.09f));
            return g;
        }
    }

    /// <summary>Chalk's wobble for a text size. A fixed seed: the same wobble every frame, so the
    /// edge never crawls or shimmers.</summary>
    private static SKPathEffect Rough(float size)
    {
        int key = (int)size;
        lock (_sync)
        {
            if (!_rough.TryGetValue(key, out var e))
                _rough[key] = e = SKPathEffect.CreateDiscrete(Math.Max(1.5f, key * 0.035f), Math.Max(0.6f, key * 0.012f), 7);
            return e;
        }
    }

    /// <summary>
    /// The letters at their frame places, each with its outline copies (<paramref name="offsets"/>
    /// already in px) under its fill. <paramref name="alpha"/> is the surface's envelope (0..1).
    /// </summary>
    public static void DrawLetters(SKCanvas canvas, StyledLine line, float left, float baseline, WordFrame f, WordFace look,
        SKColor fill, SKColor border, (float X, float Y)[] offsets, float offsetScale, double alpha, SKPaint paint, SKPaint layer)
    {
        var align = paint.TextAlign;
        paint.TextAlign = SKTextAlign.Left;
        paint.TextSize = line.Size;
        float pad = line.Size * (look.Bulbs ? 0.45f : 0.25f) + 4 * offsetScale;
        byte env = Byte(alpha);
        int n = Math.Min(line.Count, f.Count);
        for (int i = 0; i < n; i++)
        {
            var l = f.Letters[i];
            byte a = Byte(alpha * l.Alpha);
            if (a == 0 || l.Reveal <= 0) continue;
            float x = left + line.At[i] + (float)(l.Dx * line.Size), y = baseline + (float)(l.Dy * line.Size);
            var box = new SKRect(x - pad, y + line.Ascent - pad, x + line.Widths[i] + pad, y + line.Descent + pad);
            int saved = canvas.Save();
            if (l.Reveal < 1)
                canvas.ClipRect(new SKRect(box.Left, box.Top, x - pad * 0.5f + (line.Widths[i] + pad) * (float)l.Reveal, box.Bottom));
            // The style's own fade goes through a layer (a letter fades whole); the card's
            // envelope multiplies the colours, exactly as the classic card does.
            byte own = Byte(l.Alpha);
            if (own < 255)
            {
                layer.Color = SKColors.White.WithAlpha(own);
                canvas.SaveLayer(box, layer);
            }
            paint.Typeface = line.Faces[i];
            var part = line.Parts[i];

            paint.Color = border.WithAlpha(env);
            foreach (var (ox, oy) in offsets) canvas.DrawText(part, x + ox * offsetScale, y + oy * offsetScale, paint);

            if (look.Bulbs)
            {
                // The sign: a thin tube always, then the bulb's glow and fill as it lights.
                byte lit = Byte(l.Lit);
                if (lit > 0)
                {
                    paint.MaskFilter = Glow(line.Size);
                    paint.Color = fill.WithAlpha(Byte(alpha * l.Lit * 0.75));
                    canvas.DrawText(part, x, y, paint);
                    paint.MaskFilter = null;
                }
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = Math.Max(1, line.Size * 0.025f);
                paint.Color = fill.WithAlpha(Byte(alpha * 0.6));
                canvas.DrawText(part, x, y, paint);
                paint.Style = SKPaintStyle.Fill;
                if (lit > 0)
                {
                    paint.Color = fill.WithAlpha(Byte(alpha * l.Lit));
                    canvas.DrawText(part, x, y, paint);
                }
            }
            else
            {
                if (look.Rough) paint.PathEffect = Rough(line.Size);
                paint.Color = fill.WithAlpha(env);
                canvas.DrawText(part, x, y, paint);
                paint.PathEffect = null;
            }
            canvas.RestoreToCount(saved);
        }
        paint.TextAlign = align;
    }

    /// <summary>The echo's ghosts: the whole word again, plain, scaled about (<paramref name="cx"/>,
    /// <paramref name="cy"/>) by moving the pen and the size.</summary>
    public static void DrawGhosts(SKCanvas canvas, StyledLine line, float left, float baseline, float cx, float cy, WordFrame f,
        SKColor fill, SKPaint paint, double alpha)
    {
        if (f.GhostCount == 0) return;
        var align = paint.TextAlign;
        paint.TextAlign = SKTextAlign.Left;
        int n = Math.Min(line.Count, f.Count);
        for (int g = 0; g < f.GhostCount; g++)
        {
            var gh = f.Ghosts[g];
            float s = (float)gh.Scale;
            paint.TextSize = line.Size * s;
            for (int i = 0; i < n; i++)
            {
                byte a = Byte(alpha * gh.Alpha * f.Letters[i].Alpha);
                if (a == 0) continue;
                paint.Typeface = line.Faces[i];
                paint.Color = fill.WithAlpha(a);
                canvas.DrawText(line.Parts[i], cx + (left + line.At[i] - cx) * s, cy + (baseline - cy) * s, paint);
            }
        }
        paint.TextSize = line.Size;
        paint.TextAlign = align;
    }

    /// <summary>The typewriter's caret after the last letter shown.</summary>
    public static void DrawCursor(SKCanvas canvas, StyledLine line, float left, float baseline, WordFrame f,
        SKColor fill, SKPaint paint, double alpha)
    {
        byte a = Byte(alpha * f.CursorAlpha);
        if (a == 0) return;
        float x = left + (f.CursorAfter >= 0 && f.CursorAfter < line.Count
            ? line.At[f.CursorAfter] + line.Widths[f.CursorAfter] + line.Size * 0.04f : 0);
        float mid = baseline + (line.Ascent + line.Descent) / 2f, half = line.Size * 0.38f;
        paint.Color = fill.WithAlpha(a);
        canvas.DrawRect(new SKRect(x, mid - half, x + line.Size * 0.08f, mid + half), paint);
    }

    private static byte Byte(double a) => (byte)Math.Round(Math.Clamp(a, 0, 1) * 255);
}
