using System;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws the Deck's worn looks (rules in CCP.Core <see cref="DeckLooks"/> and <see cref="DeckWhisper"/>):
/// the card frames round the picture (the product, there the whole time), the same patterns on the
/// back mid-flip, painted procedurally, no art files, and the word behind the card. The caller has already squashed,
/// lifted and clipped the canvas exactly as it does for the picture, so a back turns with the card.
/// Skia only, no WPF and no App. One instance per layer; it owns its paints.
/// </summary>
public sealed class DeckLookDraw : IDisposable
{
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

    /// <summary>Paint <paramref name="back"/> into <paramref name="r"/> at <paramref name="alpha"/>. Plain draws nothing.</summary>
    public void DrawBack(SKCanvas canvas, SKRect r, DeckBack back, byte alpha)
    {
        if (back == DeckBack.Plain || alpha == 0 || r.Width < 2 || r.Height < 2) return;
        float w = r.Width, h = r.Height;
        int save = canvas.Save();
        canvas.ClipRect(r, antialias: true);
        _fill.Color = Colour(DeckLooks.Ground(back), alpha);
        canvas.DrawRect(r, _fill);
        var mark = Colour(DeckLooks.Mark(back), alpha);
        switch (back)
        {
            case DeckBack.Ink:
            {
                // Diagonal hatch, lower left to upper right, like a pen-ruled playing card.
                float step = (float)DeckLooks.Px(DeckLooks.InkStep, w);
                _line.Color = mark;
                _line.StrokeWidth = (float)DeckLooks.Px(DeckLooks.InkLine, w);
                for (float i = -h; i < w; i += step)
                    canvas.DrawLine(r.Left + i, r.Bottom, r.Left + i + h, r.Top, _line);
                break;
            }
            case DeckBack.Check:
            {
                float s = Math.Max(6f, w / DeckLooks.CheckCells);
                _fill.Color = mark;
                for (int cy = 0; cy * s < h; cy++)
                    for (int cx = 0; cx * s < w; cx++)
                        if ((cx + cy) % 2 == 0) canvas.DrawRect(r.Left + cx * s, r.Top + cy * s, s, s, _fill);
                break;
            }
            case DeckBack.Lace:
            {
                float step = (float)DeckLooks.Px(DeckLooks.LaceStep, w);
                float dot = (float)DeckLooks.Px(DeckLooks.LaceDot, w);
                float edge = step * 5f / 9f;   // the mockup's first dot sits 5 of its 9 in
                _fill.Color = mark;
                int row = 0;
                for (float ly = edge; ly < h; ly += step, row++)
                    for (float lx = edge + (row % 2) * step / 2f; lx < w; lx += step)
                        canvas.DrawCircle(r.Left + lx, r.Top + ly, dot, _fill);
                float inset = (float)DeckLooks.Px(DeckLooks.LaceInset, w);
                _line.Color = mark;
                _line.StrokeWidth = (float)DeckLooks.Px(DeckLooks.LaceFrame, w);
                canvas.DrawRect(r.Left + inset, r.Top + inset, w - inset * 2, h - inset * 2, _line);
                break;
            }
        }
        canvas.RestoreToCount(save);
        // The rim sits on top, half white, so a dark back still reads as a card on a dark desktop.
        float rim = (float)DeckLooks.Px(DeckLooks.RimWidth, w);
        _line.Color = Colour(DeckLooks.Rim, alpha);
        _line.StrokeWidth = rim;
        canvas.DrawRect(r.Left + rim / 2, r.Top + rim / 2, w - rim, h - rim, _line);
    }

    // ---- card frames: the pattern band round every Deck card (rules: DeckLooks.Frame*) ----
    private readonly SKPaint _band = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    /// <summary>One repeating tile per (look, tile size), built once and reused; a handful at most.</summary>
    private readonly System.Collections.Generic.Dictionary<(DeckBack, int), (SKBitmap Tile, SKShader Shader)> _tiles = new();

    /// <summary>
    /// The worn frame round a card's picture box <paramref name="r"/> whose corners round by
    /// <paramref name="radius"/>: a patterned band over the picture's own edge, an ink line along both
    /// sides of it. Plain, or a card too small for a band, draws nothing. Returns the band's width (0 = none),
    /// so a caller can keep a badge inside it.
    /// </summary>
    public float DrawFrame(SKCanvas canvas, SKRect r, float radius, DeckBack back, byte alpha)
    {
        if (!DeckLooks.FrameShows(back) || alpha == 0) return 0;
        float band = (float)DeckLooks.FrameBand(r.Width, r.Height);
        if (band <= 0) return 0;
        float w = r.Width, h = r.Height, ink = (float)DeckLooks.FrameInk(band);
        float outerR = Math.Max(0, radius), innerR = (float)DeckLooks.FrameInnerRadius(outerR, band);
        int save = canvas.Save();
        canvas.Translate(r.Left, r.Top);   // the pattern starts at the card's own corner
        using (var outer = new SKRoundRect(new SKRect(0, 0, w, h), outerR))
        using (var inner = new SKRoundRect(new SKRect(band, band, w - band, h - band), innerR))
        {
            _band.Shader = Tile(back, DeckLooks.FrameTile(back, band));
            _band.Color = new SKColor(255, 255, 255, alpha);
            canvas.DrawRoundRectDifference(outer, inner, _band);
            _band.Shader = null;
        }
        if (back == DeckBack.Lace)
        {
            // a fine thread down the middle of the lace band
            float mid = band / 2;
            _line.Color = Colour(DeckLooks.LaceThread, alpha);
            _line.StrokeWidth = Math.Max(1f, ink * 0.5f);
            canvas.DrawRoundRect(new SKRect(mid, mid, w - mid, h - mid), Math.Max(0, outerR - mid), Math.Max(0, outerR - mid), _line);
        }
        // ink along both edges of the band, inside it, so the picture keeps everything past the band
        _line.Color = Colour(DeckLooks.InkBlack, alpha);
        _line.StrokeWidth = ink;
        float o = ink / 2;
        canvas.DrawRoundRect(new SKRect(o, o, w - o, h - o), Math.Max(0, outerR - o), Math.Max(0, outerR - o), _line);
        canvas.DrawRoundRect(new SKRect(band - o, band - o, w - band + o, h - band + o), innerR + o, innerR + o, _line);
        canvas.RestoreToCount(save);
        return band;
    }

    private SKShader Tile(DeckBack back, int s)
    {
        if (_tiles.TryGetValue((back, s), out var kept)) return kept.Shader;
        if (_tiles.Count >= 8) ClearTiles();   // sizes only move with the card; never let them pile up
        int tw = back == DeckBack.Check ? s * 2 : s, th = back == DeckBack.Ink ? s : s * 2;
        var bmp = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp))
        using (var p = new SKPaint { IsAntialias = true })
        {
            c.Clear(new SKColor(DeckLooks.Ground(back)));
            p.Color = new SKColor(DeckLooks.Mark(back));
            switch (back)
            {
                case DeckBack.Ink:
                    // one hatch line per tile, lower left to upper right; the corner pieces make it seamless
                    p.Style = SKPaintStyle.Stroke;
                    p.StrokeWidth = Math.Max(1f, s * 0.24f);
                    for (int k = 0; k <= 2; k++) c.DrawLine(-1, k * s + 1, k * s + 1, -1, p);
                    break;
                case DeckBack.Check:
                    c.DrawRect(0, 0, s, s, p);
                    c.DrawRect(s, s, s, s, p);
                    break;
                case DeckBack.Lace:
                    float dot = Math.Max(1f, s * 0.24f);
                    c.DrawCircle(s / 2f, s / 2f, dot, p);
                    c.DrawCircle(0, s * 1.5f, dot, p);
                    c.DrawCircle(s, s * 1.5f, dot, p);
                    break;
            }
        }
        var shader = SKShader.CreateBitmap(bmp, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
        _tiles[(back, s)] = (bmp, shader);
        return shader;
    }

    private void ClearTiles()
    {
        foreach (var (tile, shader) in _tiles.Values) { shader.Dispose(); tile.Dispose(); }
        _tiles.Clear();
    }

    /// <summary>The whisper's ghosts (mockup drawWhisper): pink to one side, blue to the other.</summary>
    public static readonly SKColor GhostPink = new(0xFF, 0x9C, 0xC8), GhostBlue = new(0x9C, 0xE8, 0xFF);

    private readonly SKPaint _word = new()
    {
        IsAntialias = true,
        TextAlign = SKTextAlign.Center,
        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default,
    };

    /// <summary>
    /// The whisper at <paramref name="p"/> for a card centred on (<paramref name="cx"/>, <paramref name="cy"/>)
    /// and <paramref name="cardW"/> by <paramref name="cardH"/>. The caller draws it before the card
    /// while <see cref="WhisperPose.Front"/> is false and after it once true. Never squashed or tilted
    /// with the card: the word is in the air, not on it.
    /// </summary>
    public void DrawWhisper(SKCanvas canvas, float cx, float cy, float cardW, float cardH, string text, WhisperPose p, byte alpha)
    {
        if (!p.Visible || string.IsNullOrEmpty(text)) return;
        var a = p.Alpha * alpha;
        if (a < 1) return;
        float font = (float)DeckWhisper.FontPx(cardH);
        _word.TextSize = font;
        var m = _word.FontMetrics;
        float baseline = -(m.Ascent + m.Descent) / 2;
        int save = canvas.Save();
        canvas.Translate(cx + (float)(p.Dx * cardW), cy + (float)(p.Dy * cardH));
        canvas.Scale((float)p.Scale);
        if (p.Split > 0.01)
        {
            float off = (float)(p.Split * font);
            _word.Color = GhostPink.WithAlpha((byte)Math.Clamp(a * 0.5, 0, 255));
            canvas.DrawText(text, -off, baseline, _word);
            _word.Color = GhostBlue.WithAlpha((byte)Math.Clamp(a * 0.5, 0, 255));
            canvas.DrawText(text, off, baseline, _word);
        }
        _word.Color = SKColors.White.WithAlpha((byte)Math.Clamp(a, 0, 255));
        canvas.DrawText(text, 0, baseline, _word);
        canvas.RestoreToCount(save);
    }

    /// <summary>An ARGB colour with its own alpha scaled by the card's.</summary>
    internal static SKColor Colour(uint argb, byte alpha)
    {
        var c = new SKColor(argb);
        return c.WithAlpha((byte)(c.Alpha * alpha / 255));
    }

    public void Dispose()
    {
        _fill.Dispose();
        _line.Dispose();
        _word.Dispose();
        _band.Dispose();
        ClearTiles();
    }
}
