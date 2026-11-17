using System.Globalization;

namespace ConditioningControlPanel.Looks;

/// <summary>The word styles the shop sells (slot <c>word</c>). Classic is today's look.</summary>
public enum WordStyle { Classic, Type, Chalk, Drift, Echo, Marquee }

/// <summary>How much a word may move. <see cref="Still"/> is motion Off or Gentle visuals: the
/// style keeps its face and colour and the word only rides its surface's own fade.</summary>
public enum WordMotion { Full, Reduced, Still }

/// <summary>
/// A style's face. <see cref="Families"/> is a list in order of preference (the head draws with
/// the first one installed, then its own face); null keeps the surface's own face, which is the
/// user's font for subliminals. Colours are 0xRRGGBB; null keeps the surface's own colour.
/// <see cref="Size"/> multiplies the surface's own text size; <see cref="Tilt"/> is degrees.
/// <see cref="Rough"/> draws the strokes with a fixed chalky wobble; <see cref="Bulbs"/> draws a
/// sign: a tube outline always, the lit fill and its glow by <see cref="WordLetter.Lit"/>.
/// </summary>
public sealed record WordFace(string[]? Families, bool Bold, double Size, uint? Colour, uint? Border, bool Upper, double Tilt,
    bool Rough = false, bool Bulbs = false);

/// <summary>One letter in one frame. Offsets are in ems (shares of the text size), down is plus.
/// <see cref="Reveal"/> is how much of the letter shows, from its left edge (0..1);
/// <see cref="Lit"/> is how lit a sign's bulb is (0 = only its tube; 1 for every other style).</summary>
public struct WordLetter
{
    public double Alpha, Dx, Dy, Reveal, Lit;
}

/// <summary>A faint copy of the whole word, scaled about its centre.</summary>
public struct WordGhost
{
    public double Scale, Alpha;
}

/// <summary>
/// One frame of a styled word, filled by <see cref="WordStyles.Fill"/>. Reused frame to frame so
/// a drawer allocates nothing per frame. Alphas multiply the surface's own envelope; they never
/// replace it, so a style cannot change how long a word stays on screen.
/// </summary>
public sealed class WordFrame
{
    public WordLetter[] Letters = Array.Empty<WordLetter>();
    public int Count;
    public readonly WordGhost[] Ghosts = new WordGhost[2];
    public int GhostCount;
    /// <summary>The typewriter's cursor: its alpha and the letter it follows (-1 = before the first).</summary>
    public double CursorAlpha;
    public int CursorAfter = -1;

    internal void Reset(int count)
    {
        if (Letters.Length < count) Letters = new WordLetter[Math.Max(count, Letters.Length * 2)];
        Count = count;
        for (int i = 0; i < count; i++) Letters[i] = new WordLetter { Alpha = 1, Reveal = 1, Lit = 1 };
        GhostCount = 0; CursorAlpha = 0; CursorAfter = -1;
    }

    /// <summary>A cheap fingerprint of what this frame draws, so a layer can skip a frame that
    /// would paint the same pixels (alphas and offsets quantised).</summary>
    public long Key()
    {
        long k = 17;
        for (int i = 0; i < Count; i++)
        {
            var l = Letters[i];
            k = k * 31 + (long)(l.Alpha * 255) + ((long)(l.Dy * 400) << 9) + ((long)(l.Reveal * 255) << 20) + ((long)(l.Lit * 255) << 30);
        }
        for (int i = 0; i < GhostCount; i++) k = k * 31 + (long)(Ghosts[i].Alpha * 255) + ((long)(Ghosts[i].Scale * 400) << 9);
        return k * 31 + (long)(CursorAlpha * 255) + CursorAfter;
    }
}

/// <summary>
/// THE WORD STYLE RULE, shared by every surface that shows a word (subliminal cards, the Steam
/// fog's words). For a style, a word's letter count, the seconds since it was shown and its whole
/// life, it gives each letter's placement, opacity and reveal, plus the face and colour. The life
/// is the surface's own (settings, fades, Gentle stretch): every motion here is fitted inside it,
/// so the time on screen never depends on the style. Classic is never filled: a surface draws
/// classic exactly as it always has. Pure: no WPF, no Skia, no clock.
/// </summary>
public static class WordStyles
{
    public const string Prefix = "fx.word.";

    /// <summary>The style a worn row names (<c>fx.word.chalk</c>). Unknown or null = Classic.</summary>
    public static WordStyle Of(string? rowId) => rowId switch
    {
        "fx.word.type" => WordStyle.Type,
        "fx.word.chalk" => WordStyle.Chalk,
        "fx.word.drift" => WordStyle.Drift,
        "fx.word.echo" => WordStyle.Echo,
        "fx.word.marquee" => WordStyle.Marquee,
        _ => WordStyle.Classic,
    };

    /// <summary>The faces, from the mockup's wordFont/wordColour with system faces in place of the
    /// web ones (nothing is bundled): Caveat -> Segoe Print, IBM Plex Mono -> Consolas, Alfa Slab
    /// One -> Rockwell (Office) or Bahnschrift (every Windows 10+). Drift and Echo keep the
    /// surface's face, as the mockup draws them in its plain face.</summary>
    public static WordFace FaceOf(WordStyle style) => style switch
    {
        WordStyle.Type => new(new[] { "Consolas", "Courier New" }, true, 0.82, null, null, false, 0),
        WordStyle.Chalk => new(new[] { "Segoe Print", "Ink Free", "Comic Sans MS" }, true, 1.0, 0xFDF7E8, 0x2B3A33, false, -2, Rough: true),
        WordStyle.Marquee => new(new[] { "Rockwell Extra Bold", "Rockwell", "Bahnschrift", "Arial Black" }, true, 0.9, 0xFFD24A, 0x3A1E00, true, 0, Bulbs: true),
        _ => new(null, true, 1.0, null, null, false, 0),
    };

    /// <summary>The text as the style shows it (the marquee is in capitals).</summary>
    public static string TextFor(WordStyle style, string text)
        => style == WordStyle.Marquee ? text.ToUpperInvariant() : text;

    /// <summary>The letters a drawer places one by one: text elements, so an emoji or an accent
    /// stays whole.</summary>
    public static int LetterCount(string text) => new StringInfo(text).LengthInTextElements;

    /// <summary>Seconds into a life by which the word is fully in: a share of the life, capped,
    /// so a 200 ms subliminal and an 8 s fog word both finish their entrance early.</summary>
    public static double RevealSeconds(WordStyle style, double total)
    {
        total = Math.Max(0.001, total);
        double cap = style switch { WordStyle.Chalk => 1.1, WordStyle.Drift => 1.0, _ => 1.0 };
        return Math.Min(cap, total * 0.3);
    }

    /// <summary>
    /// Fill <paramref name="into"/> for a word of <paramref name="letters"/> letters,
    /// <paramref name="t"/> seconds into a life of <paramref name="total"/> seconds.
    /// </summary>
    public static void Fill(WordStyle style, int letters, double t, double total, WordMotion motion, WordFrame into)
    {
        int n = Math.Max(0, letters);
        into.Reset(n);
        if (n == 0 || style == WordStyle.Classic) return;
        total = Math.Max(0.001, total);
        t = Math.Clamp(t, 0, total);
        var L = into.Letters;

        if (motion == WordMotion.Still)
        {
            // Settled. Echo keeps two ghosts, held still: the look without the motion.
            if (style == WordStyle.Echo)
            {
                into.GhostCount = 2;
                into.Ghosts[0] = new WordGhost { Scale = 1.10, Alpha = 0.26 };
                into.Ghosts[1] = new WordGhost { Scale = 1.24, Alpha = 0.14 };
            }
            return;
        }

        bool full = motion == WordMotion.Full;
        double reveal = RevealSeconds(style, total);
        // The leaving stretch: the last fifth of the life, at most 0.7 s (the mockup's out).
        double outLen = Math.Min(0.7, total * 0.2);
        double stay = Math.Clamp((total - t) / outLen, 0, 1);

        switch (style)
        {
            case WordStyle.Type:
            {
                // Letter by letter across the reveal. Full: each letter lands at once; Reduced:
                // each fades in over its own step.
                double step = reveal / n;
                int shown = 0;
                for (int i = 0; i < n; i++)
                {
                    double since = t - i * step;
                    double a = since < 0 ? 0 : full ? 1 : Math.Clamp(since / step, 0, 1);
                    L[i].Alpha = a;
                    if (a > 0) shown = i + 1;
                }
                into.CursorAfter = shown - 1;
                // The caret blinks at 1.5 a second (Full) or stays lit (Reduced), and leaves with the word.
                bool on = !full || ((int)Math.Floor(t * 3)) % 2 == 0;
                into.CursorAlpha = t < total - outLen && on ? 1 : 0;
                break;
            }
            case WordStyle.Chalk:
            {
                // Written on: a front sweeps left to right across the letters.
                double w = Ease(t / reveal) * n;
                for (int i = 0; i < n; i++) L[i].Reveal = Math.Clamp(w - i, 0, 1);
                break;
            }
            case WordStyle.Drift:
            {
                // Letters rise from below and settle one after another, then lift as the word goes.
                double rise = Math.Min(0.55, reveal * 0.6);
                double lag = n > 1 ? Math.Min(0.07, (reveal - rise) / (n - 1)) : 0;
                double reach = full ? 1 : 0.5;
                for (int i = 0; i < n; i++)
                {
                    double e = Ease((t - i * lag) / rise);
                    L[i].Alpha = e;
                    L[i].Dy = reach * ((1 - e) * 0.55 - (1 - stay) * 0.4);
                }
                break;
            }
            case WordStyle.Echo:
            {
                // Two ghosts swell out of the word and thin away, over and over.
                double spread = full ? 0.4 : 0.2;
                into.GhostCount = 2;
                for (int i = 0; i < 2; i++)
                {
                    double fr = (t * 0.45 + (i + 1) * 0.5) % 1;
                    into.Ghosts[i] = new WordGhost { Scale = 1 + fr * spread, Alpha = (1 - fr) * 0.45 };
                }
                break;
            }
            case WordStyle.Marquee:
            {
                // Bulbs light one by one (only the tube shows until then), each warming up over a short ramp
                // (never a hard on/off), and at Full the whole sign breathes once when it is all
                // lit: one slow dip and back, never a blink and never a strobe.
                double start = Math.Min(0.1, total * 0.05);
                double step = n > 0 ? Math.Min(0.13, (reveal - start) / n) : 0;
                double ramp = Math.Max(0.001, full ? step * 0.6 : step);
                double allAt = start + n * step + ramp;
                double dip = 0;
                if (full && allAt + SwellSeconds < total - outLen)
                {
                    double u = (t - allAt) / SwellSeconds;
                    if (u > 0 && u < 1) dip = 0.3 * (1 - Math.Cos(u * 2 * Math.PI)) / 2;
                }
                for (int i = 0; i < n; i++)
                {
                    double lit = Math.Clamp((t - (start + i * step)) / ramp, 0, 1);
                    L[i].Lit = lit * (1 - dip);
                }
                break;
            }
        }
    }

    /// <summary>The marquee's one breath once every bulb is lit (seconds, dip and back).</summary>
    public const double SwellSeconds = 0.5;

    /// <summary>How far past its laid-out box a style's word can reach, for the awareness OCR's
    /// skip rect: the echo's widest ghost (times the width and height) and the drift's travel
    /// (in ems, added above and below).</summary>
    public static (double Scale, double RiseEm) Reach(WordStyle style) => style switch
    {
        WordStyle.Echo => (1.4, 0),
        WordStyle.Drift => (1, 0.55),
        _ => (1, 0),
    };

    /// <summary>Ease out (cubic), clamped: the mockup's ease.</summary>
    public static double Ease(double x) { x = Math.Clamp(x, 0, 1); return 1 - Math.Pow(1 - x, 3); }

    /// <summary>0xRRGGBB to its three bytes.</summary>
    public static (byte R, byte G, byte B) Rgb(uint c) => ((byte)(c >> 16), (byte)(c >> 8), (byte)c);
}
