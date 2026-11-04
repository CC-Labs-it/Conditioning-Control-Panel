using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>WPF's book rail painters, shared by both heads; no imaging or UI types.</summary>
public static class EmiBookGlyphPainters
{
    public const int Size = 16;
    public static IReadOnlyList<string> Ids => Array.AsReadOnly(Array.ConvertAll(Painters, p => p.Id));

    /// <summary>Draw onto a transparent 16-cell buffer. Unknown ids have no glyph.</summary>
    public static bool Draw(string id, EmiPixelCanvas canvas)
    {
        foreach (var (key, draw) in Painters)
        {
            if (key != id) continue;
            canvas.Clear(0);
            draw(canvas);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A filled disc. There is no circle in the props kit because nothing at 96 x 72
    /// needed one; at 16 cells a round shape is the only way an eye or a dial reads.</summary>
    private static void Disc(EmiPixelCanvas p, int cx, int cy, double r, uint c)
    {
        int ri = (int)Math.Ceiling(r);
        for (int y = cy - ri; y <= cy + ri; y++)
        for (int x = cx - ri; x <= cx + ri; x++)
        {
            double dx = x - cx, dy = y - cy;
            if (dx * dx + dy * dy <= r * r) p.Px(x, y, c);
        }
    }

    /// <summary>A triangle pointing right, for anything that means "play".</summary>
    private static void PlayHead(EmiPixelCanvas p, int x, int y, int h, uint c)
    {
        for (int i = 0; i * 2 < h; i++) p.Rect(x + i, y + i, 1, h - i * 2, c);
    }

    /// <summary>A triangle pointing down, for anything that means "into".</summary>
    private static void DownHead(EmiPixelCanvas p, int cx, int y, int w, uint c)
    {
        for (int i = 0; w - i * 2 > 0; i++) p.Rect(cx - (w - i * 2) / 2, y + i, w - i * 2, 1, c);
    }

    // ------------------------------------------------------------------ the glyphs

    private static readonly (string Id, Action<EmiPixelCanvas> Draw)[] Painters =
    {
        // ---- START ---------------------------------------------------------
        ("the-ccp", p =>
        {
            p.Rect(1, 2, 14, 10, EmiPix.Mid);
            p.Rect(2, 3, 12, 8, EmiPix.Ink);
            p.Rect(2, 3, 12, 2, EmiPix.Pink);      // the app is the thing with the pink bar on it
            p.Rect(6, 12, 4, 2, EmiPix.Mid);
            p.Rect(4, 14, 8, 1, EmiPix.Mid);
        }),

        ("the-panic-key", p =>
        {
            p.Rect(2, 4, 12, 8, EmiPixelCanvas.Rgb(0x8F, 0x8A, 0x7D));   // the key's side
            p.Rect(2, 3, 12, 7, EmiPix.Cream);                            // its face
            p.Rect(6, 5, 4, 3, EmiPix.Pink);
        }),

        ("your-media", p =>
        {
            p.Rect(2, 2, 6, 2, EmiPixelCanvas.Rgb(0xC9, 0x9B, 0x34));     // the tab
            p.Rect(2, 4, 12, 9, EmiPix.Gold);
            p.Rect(4, 6, 8, 5, EmiPix.Lav);                               // a picture inside it
            p.Rect(9, 7, 2, 2, EmiPix.Cream);
            p.Rect(4, 9, 8, 2, EmiPixelCanvas.Rgb(0x6A, 0x5C, 0xA8));
        }),

        ("sessions", p =>
        {
            p.Rect(2, 3, 10, 2, EmiPix.Pink);      // three booked lanes, one clock
            p.Rect(2, 7, 6, 2, EmiPix.Lav);
            p.Rect(2, 11, 12, 2, EmiPix.Pink);
            p.Rect(10, 1, 1, 14, EmiPix.Cream);    // the playhead crossing them
        }),

        ("progression", p =>
        {
            p.Rect(1, 14, 14, 1, EmiPix.Mid);
            p.Rect(2, 10, 3, 4, EmiPix.Lav);
            p.Rect(6, 7, 3, 7, EmiPix.Pink);
            p.Rect(10, 3, 3, 11, EmiPix.Gold);
        }),

        ("the-desk", p =>
        {
            p.Rect(3, 2, 10, 4, EmiPix.Pink);      // her, and she is the only face in the book
            p.Rect(3, 5, 1, 5, EmiPix.Pink);
            p.Rect(12, 5, 1, 5, EmiPix.Pink);
            p.Rect(4, 5, 8, 6, EmiPix.Cream);
            p.Rect(5, 7, 2, 2, EmiPix.Ink);
            p.Rect(9, 7, 2, 2, EmiPix.Ink);
            p.Rect(5, 11, 6, 4, EmiPix.Pink);
        }),

        // ---- TOOLS ---------------------------------------------------------
        ("flashes", p =>
        {
            p.Rect(3, 4, 10, 8, EmiPix.Cream);
            p.Rect(4, 5, 8, 6, EmiPix.Lav);
            // four sparks, because a flash is a picture that ARRIVED rather than a picture
            p.Rect(1, 2, 2, 1, EmiPix.Pink); p.Rect(1, 2, 1, 2, EmiPix.Pink);
            p.Rect(13, 2, 2, 1, EmiPix.Pink); p.Rect(14, 2, 1, 2, EmiPix.Pink);
            p.Rect(1, 13, 2, 1, EmiPix.Pink); p.Rect(1, 12, 1, 2, EmiPix.Pink);
            p.Rect(13, 13, 2, 1, EmiPix.Pink); p.Rect(14, 12, 1, 2, EmiPix.Pink);
        }),

        ("subliminals", p =>
        {
            p.Rect(2, 4, 12, 8, EmiPix.Cream);     // a card of words you do not get to read
            p.Rect(3, 6, 5, 1, EmiPix.Ink);
            p.Rect(9, 6, 3, 1, EmiPix.Ink);
            p.Rect(3, 8, 7, 1, EmiPix.Ink);
            p.Rect(3, 10, 4, 1, EmiPix.Ink);
        }),

        ("videos", p =>
        {
            p.Rect(1, 3, 14, 10, EmiPix.Mid);
            p.Rect(2, 4, 12, 8, EmiPix.Ink);
            PlayHead(p, 6, 5, 7, EmiPix.Cream);
        }),

        ("whispers", p =>
        {
            p.Rect(3, 6, 2, 4, EmiPix.Cream);      // a cone, then two arcs leaving it
            for (int i = 0; i < 3; i++) p.Rect(5 + i, 5 - i, 1, 6 + i * 2, EmiPix.Cream);
            p.Rect(10, 6, 1, 4, EmiPix.Pink);
            p.Rect(12, 4, 1, 8, EmiPix.Pink);
        }),

        ("spiral", p =>
        {
            EmiPix.Spiral(p, 8, 8, 7.0, 0.0, EmiPix.Pink);
            EmiPix.Spiral(p, 8, 8, 7.0, Math.PI, EmiPix.Lav);
        }),

        ("overlays", p =>
        {
            p.Rect(1, 2, 10, 8, EmiPix.Mid);       // three sheets, and they stack
            p.Rect(3, 4, 10, 8, EmiPix.Lav);
            p.Rect(5, 6, 10, 8, EmiPix.Pink);
        }),

        ("bubbles", p =>
        {
            Disc(p, 5, 10, 3.4, EmiPix.Lav);
            p.Rect(4, 8, 1, 1, EmiPix.Cream);
            Disc(p, 11, 6, 2.6, EmiPix.Pink);
            p.Rect(10, 4, 1, 1, EmiPix.Cream);
            Disc(p, 12, 12, 1.8, EmiPix.Lav);
        }),

        ("corner-gifs", p =>
        {
            p.Rect(1, 3, 14, 10, EmiPix.Mid);
            p.Rect(2, 4, 12, 8, EmiPix.Ink);
            p.Rect(3, 8, 4, 4, EmiPix.Pink);       // the whole point is WHICH corner
        }),

        // ---- DEEPER --------------------------------------------------------
        ("deeper", p =>
        {
            p.Rect(7, 1, 2, 8, EmiPix.Pink);       // something going down into a track
            DownHead(p, 8, 9, 7, EmiPix.Pink);
            p.Rect(1, 13, 14, 2, EmiPix.Mid);
            p.Rect(3, 13, 2, 2, EmiPix.Gold);
            p.Rect(11, 13, 2, 2, EmiPix.Gold);
        }),

        ("companion", p =>
        {
            p.Rect(2, 3, 12, 8, EmiPix.Cream);     // a bubble, since she is the one who talks
            p.Rect(4, 11, 3, 2, EmiPix.Cream);
            p.Rect(4, 13, 1, 1, EmiPix.Cream);
            p.Rect(5, 6, 2, 2, EmiPix.Pink);
            p.Rect(9, 6, 2, 2, EmiPix.Pink);
        }),

        ("awareness", p =>
        {
            // an eye, drawn as a lens rather than a circle so it cannot be mistaken for the vault
            for (int x = 2; x <= 13; x++)
            {
                int h = (int)Math.Round(4.2 * Math.Sin(Math.PI * (x - 2) / 11.0));
                if (h > 0) p.Rect(x, 8 - h, 1, h * 2, EmiPix.Cream);
            }
            Disc(p, 8, 8, 3.2, EmiPix.Pink);
            Disc(p, 8, 8, 1.4, EmiPix.Ink);
        }),

        ("lockdown", p =>
        {
            p.Rect(5, 2, 1, 5, EmiPix.Cream);      // a shut padlock
            p.Rect(10, 2, 1, 5, EmiPix.Cream);
            p.Rect(6, 1, 4, 1, EmiPix.Cream);
            p.Rect(3, 6, 10, 8, EmiPix.Gold);
            p.Rect(7, 9, 2, 3, EmiPix.Ink);
        }),

        ("takeover", p =>
        {
            EmiPix.Cursor(p, 5, 4);                // a cursor that is moving without you
            p.Rect(1, 2, 3, 1, EmiPix.Pink);
            p.Rect(12, 4, 3, 1, EmiPix.Pink);
            p.Rect(2, 12, 3, 1, EmiPix.Pink);
            p.Rect(11, 13, 3, 1, EmiPix.Pink);
        }),

        ("arcademy", p =>
        {
            p.Rect(6, 2, 4, 1, EmiPix.Pink);       // a mortarboard, seen head on
            p.Rect(4, 3, 8, 1, EmiPix.Pink);
            p.Rect(2, 4, 12, 1, EmiPix.Pink);
            p.Rect(4, 5, 8, 1, EmiPix.Pink);
            p.Rect(6, 6, 4, 1, EmiPix.Pink);
            p.Rect(5, 7, 6, 3, EmiPix.Lav);
            p.Rect(13, 4, 1, 6, EmiPix.Gold);
            p.Rect(12, 10, 2, 1, EmiPix.Gold);
        }),

        ("the-games", p =>
        {
            p.Rect(3, 1, 10, 14, EmiPix.Mid);      // a cabinet, because these are whole games
            p.Rect(4, 3, 8, 5, EmiPix.Pink);
            p.Rect(5, 10, 6, 1, EmiPix.Ink);
            p.Rect(5, 12, 2, 2, EmiPix.Cream);
            p.Rect(9, 12, 2, 2, EmiPix.Cream);
        }),

        ("vault", p =>
        {
            p.Rect(2, 3, 12, 10, EmiPixelCanvas.Rgb(0xC9, 0x9B, 0x34));   // a door, not a padlock
            p.Rect(3, 4, 10, 8, EmiPix.Gold);
            Disc(p, 8, 8, 3.2, EmiPix.Cream);
            Disc(p, 8, 8, 1.2, EmiPix.Ink);
            p.Rect(12, 7, 2, 2, EmiPix.Cream);
        }),
    };
}
