using System;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws the Deck's worn looks (rules in CCP.Core <see cref="DeckLooks"/> and <see cref="DeckWhisper"/>):
/// the four card backs, painted procedurally into the card's picture box, no art files, and the
/// word behind the card. The caller has already squashed,
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
    }
}
