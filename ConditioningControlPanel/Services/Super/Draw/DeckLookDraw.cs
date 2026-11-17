using System;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws the Deck's worn looks and its turn (rules in CCP.Core <see cref="DeckLooks"/>, <see cref="DeckTurn"/>
/// and <see cref="DeckSpiralBack"/>): the card frames round the picture, the shade on a turning face, and the
/// spiral back with its phrase coming at the viewer. The caller has already lifted, tilted and turned the
/// canvas exactly as it does for the picture, so everything here turns with the card. Skia only, no WPF
/// and no App. One instance per layer; it owns its paints and shaders and allocates nothing per frame.
/// </summary>
public sealed class DeckLookDraw : IDisposable
{
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

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

    // ---- the turn: the face darkens toward its far edge as it turns away (DeckTurn.Shade) ----

    /// <summary>Unit-space ramp, black from a third at the near edge to full at the far edge; mapped onto the face per frame.</summary>
    private readonly SKPaint _shade = new()
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0),
            new[] { new SKColor(0, 0, 0, 85), new SKColor(0, 0, 0, 255) }, new[] { 0f, 1f }, SKShaderTileMode.Clamp),
    };

    /// <summary>
    /// Darken the face <paramref name="r"/> by <paramref name="shade"/> (0..1). <paramref name="farRight"/> says
    /// which edge is turning away. Drawn in the card's own space, so it narrows with the face.
    /// </summary>
    public void DrawShade(SKCanvas canvas, SKRect r, double shade, bool farRight, byte alpha)
    {
        var a = (byte)Math.Clamp(shade * alpha, 0, 255);
        if (a == 0 || r.Width < 1) return;
        int save = canvas.Save();
        canvas.ClipRect(r, antialias: true);
        if (farRight) { canvas.Translate(r.Left, r.Top); canvas.Scale(r.Width, r.Height); }
        else { canvas.Translate(r.Right, r.Top); canvas.Scale(-r.Width, r.Height); }
        _shade.Color = new SKColor(255, 255, 255, a);
        canvas.DrawRect(0, 0, 1, 1, _shade);
        canvas.RestoreToCount(save);
    }

    // ---- the spiral back (DeckSpiralBack): a Loom spiral filling the card, the phrase coming at you ----

    private readonly SKPaint _spiral = new() { IsAntialias = false, FilterQuality = SKFilterQuality.Low };
    /// <summary>Unit-space dim at the centre, so the phrase reads on any spiral colour.</summary>
    private readonly SKPaint _dim = new()
    {
        IsAntialias = true,
        Shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1,
            new[] { new SKColor(0, 0, 0, 150), new SKColor(0, 0, 0, 90), new SKColor(0, 0, 0, 0) },
            new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp),
    };

    /// <summary>
    /// The back: <paramref name="ground"/> (ARGB), then the spiral picture <paramref name="spiral"/> turned by
    /// <paramref name="turnRad"/> and sized to cover the whole card <paramref name="r"/> (its round edge sits past
    /// the corners), then a soft dim at the centre for the phrase. The caller clips to the card.
    /// </summary>
    public void DrawSpiralBack(SKCanvas canvas, SKRect r, SKBitmap spiral, uint ground, double turnRad, byte alpha, SpiralBackCache? cache = null)
    {
        if (alpha == 0 || r.Width < 2 || r.Height < 2) return;
        if (cache != null && cache.Draw(this, canvas, r, spiral, ground, turnRad, alpha)) return;
        Compose(canvas, r, spiral, ground, turnRad, alpha);
    }

    /// <summary>Ground, turned spiral and dim straight into <paramref name="canvas"/> over <paramref name="r"/>.</summary>
    internal void Compose(SKCanvas canvas, SKRect r, SKBitmap spiral, uint ground, double turnRad, byte alpha, SKImage? spiralImage = null, bool dim = true)
    {
        _fill.Color = Colour(ground, alpha);
        canvas.DrawRect(r, _fill);
        float half = (float)Math.Sqrt(r.Width * r.Width + r.Height * r.Height) / 2f;
        int save = canvas.Save();
        canvas.Translate(r.MidX, r.MidY);
        if (turnRad != 0) canvas.RotateRadians((float)turnRad);
        _spiral.Color = new SKColor(255, 255, 255, alpha);
        if (spiralImage != null) canvas.DrawImage(spiralImage, new SKRect(-half, -half, half, half), _spiral);
        else canvas.DrawBitmap(spiral, new SKRect(-half, -half, half, half), _spiral);
        canvas.RestoreToCount(save);
        if (dim) DrawDim(canvas, r, alpha);
    }

    /// <summary>The soft dim at the centre of the card (the phrase reads on any spiral colour). It never turns, so a
    /// cached back draws it once per card size (SpiralBackCache).</summary>
    internal void DrawDim(SKCanvas canvas, SKRect r, byte alpha)
    {
        float dimR = Math.Min(r.Width, r.Height) * 0.62f;
        int save = canvas.Save();
        canvas.Translate(r.MidX, r.MidY);
        canvas.Scale(dimR * Math.Max(1f, r.Width / r.Height), dimR * Math.Max(1f, r.Height / r.Width));
        _dim.Color = new SKColor(255, 255, 255, alpha);
        canvas.DrawCircle(0, 0, 1, _dim);
        canvas.RestoreToCount(save);
    }

    private readonly SKPaint _wordFill = new() { IsAntialias = true, TextAlign = SKTextAlign.Center, Style = SKPaintStyle.Fill, Typeface = WordFace };
    private readonly SKPaint _wordEdge = new()
    {
        IsAntialias = true, TextAlign = SKTextAlign.Center, Style = SKPaintStyle.Stroke,
        StrokeJoin = SKStrokeJoin.Round, Typeface = WordFace,
    };
    private static readonly SKTypeface WordFace =
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Black, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
        ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>The slot's ghosts (callout.js): pink to one side, blue to the other, as the word passes.</summary>
    public static readonly SKColor GhostPink = new(0xFF, 0x3D, 0x8F), GhostBlue = new(0x5F, 0xB0, 0xFF);
    /// <summary>The outline and the drop under the face of the word: a deep plum, like the slot's #49164f.</summary>
    public static readonly SKColor WordInk = new(0x2A, 0x0B, 0x30);
    public const float LineHeight = 1.08f;

    /// <summary>
    /// The phrase at <paramref name="p"/>, centred on the card <paramref name="r"/>, bold, a pale house tint
    /// (<paramref name="hue"/>) over a thick plum outline and a short drop, so it reads on any spiral.
    /// The block is fitted to the card at every shape (DeckSpiralBack.FontPx).
    /// </summary>
    public void DrawBackWord(SKCanvas canvas, SKRect r, string[]? lines, BackWordPose p, float hue, byte alpha)
    {
        if (!p.Visible || lines is not { Length: > 0 }) return;
        var a = p.Alpha * alpha;
        if (a < 1) return;
        _wordFill.TextSize = 100;
        float widest = 0;
        foreach (var l in lines) widest = Math.Max(widest, _wordFill.MeasureText(l));
        float font = (float)DeckSpiralBack.FontPx(widest / 100f, lines.Length, r.Width, r.Height, LineHeight);
        if (font <= 0) return;
        _wordFill.TextSize = font;
        _wordEdge.TextSize = font;
        var m = _wordFill.FontMetrics;
        float lineH = font * LineHeight;
        float top = -lineH * lines.Length / 2f;
        float baseline0 = top + (lineH - (m.Descent - m.Ascent)) / 2f - m.Ascent;

        int save = canvas.Save();
        canvas.Translate(r.MidX, r.MidY);
        if (p.RotRad != 0) canvas.RotateRadians((float)p.RotRad);
        canvas.Scale((float)p.Scale, (float)(p.Scale * p.ScaleY));

        if (p.GhostAlpha > 0.01 && p.Split > 0.001)
        {
            float off = (float)(p.Split * font);
            byte ga = (byte)Math.Clamp(p.GhostAlpha * alpha * Math.Max(0.35, p.Alpha), 0, 255);
            _wordFill.BlendMode = SKBlendMode.Screen;
            _wordFill.Color = GhostPink.WithAlpha(ga);
            for (int i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], -off, baseline0 + i * lineH, _wordFill);
            _wordFill.Color = GhostBlue.WithAlpha(ga);
            for (int i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], off, baseline0 + i * lineH, _wordFill);
            _wordFill.BlendMode = SKBlendMode.SrcOver;
        }

        byte ia = (byte)Math.Clamp(a, 0, 255);
        float drop = Math.Max(1.5f, font * 0.06f);
        _wordEdge.Color = WordInk.WithAlpha((byte)(ia * 0.85f));
        _wordEdge.StrokeWidth = Math.Max(2f, font * 0.17f);
        for (int i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], 0, baseline0 + i * lineH + drop, _wordEdge);
        _wordEdge.Color = WordInk.WithAlpha(ia);
        for (int i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], 0, baseline0 + i * lineH, _wordEdge);
        _wordFill.Color = SKColor.FromHsl(hue, 100, 90).WithAlpha(ia);
        for (int i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], 0, baseline0 + i * lineH, _wordFill);
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
        _band.Dispose();
        _shade.Shader?.Dispose(); _shade.Dispose();
        _dim.Shader?.Dispose(); _dim.Dispose();
        _spiral.Dispose();
        _wordFill.Dispose();
        _wordEdge.Dispose();
        ClearTiles();
    }
}
