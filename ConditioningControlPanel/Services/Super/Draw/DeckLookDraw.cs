using System;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Super.Draw;

/// <summary>
/// Draws the Deck's worn looks (rules in CCP.Core <see cref="DeckLooks"/>): the four card backs,
/// painted procedurally into the card's picture box, no art files. The caller has already squashed,
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
    }
}
