namespace ConditioningControlPanel.SuperCore;

/// <summary>Where the phrase on a spiral back is this frame: scale 1 = fitted to the card; RotRad tilts it;
/// ScaleY squashes it; Split is how far the pink and blue ghosts stand off, in font sizes.</summary>
public readonly record struct BackWordPose(bool Visible, double Alpha, double Scale, double ScaleY, double RotRad, double Split, double GhostAlpha);

/// <summary>
/// The spiral back (owner 2026-11-17, worn with "words behind the card", shop row <c>fx.behind.whisper</c>):
/// "1 image on front and 1 spiral on the back, the spiral and words change with each flip, and each flip
/// has the subliminals coming at us". With the look worn a card goes picture, spiral, picture, spiral: every
/// flip to the back rolls a new Loom spiral (never the user's own spiral file) in the house colours, turning
/// slowly, and one phrase from the player's own pool comes at the viewer over it, the Back Room slot's word
/// beat (shared/hypno/callout.js zoomIn): small and far at the centre, a quick ease-out zoom toward the
/// screen with a little tilt and squash on the way, a breath at full size, then it grows on past the viewer
/// and thins into a pink and a blue ghost. One phrase per flip. Pure maths: the flash layer draws it.
/// Reduced: half the zoom and the tilt. Off: the spiral stands still and the phrase fades in place.
/// Gentle Visuals: a slower turn, a slower zoom, a 300 ms rise floor, no ghosts.
/// </summary>
public static class DeckSpiralBack
{
    // ---- the word beat (the slot's ZOOM 0.6 -> 1.3 over 1 s, hold 0.2 s, dissolve 0.4 s; here from further away) ----
    public const double ZoomSec = 1.1, HoldSec = 0.35, OutSec = 0.45;
    public const double GentleZoomSec = 1.3, GentleHoldSec = 0.5, GentleOutSec = 0.7, GentleRiseSec = 0.35;
    public const double StillRiseSec = 0.35, StillHoldSec = 1.1, StillOutSec = 0.5;
    /// <summary>The phrase starts this small (far away at the spiral's centre) and passes the viewer at <see cref="PassScale"/>.</summary>
    public const double FarScale = 0.14, PassScale = 1.14;
    /// <summary>The slot's tilt on the way in: -3 degrees at 40% of the zoom, +2 at 72%.</summary>
    public const double TiltA = -3 * Math.PI / 180, TiltB = 2 * Math.PI / 180;
    /// <summary>Ghosts stand off up to this many font sizes as the phrase passes.</summary>
    public const double SplitMax = 0.3;

    /// <summary>Seconds from the back landing until the phrase is gone.</summary>
    public static double Life(bool motionOff, bool gentle)
        => motionOff ? StillRiseSec + StillHoldSec + StillOutSec
           : gentle ? GentleZoomSec + GentleHoldSec + GentleOutSec : ZoomSec + HoldSec + OutSec;

    /// <summary>
    /// The phrase <paramref name="age"/> seconds after the back landed. <paramref name="amp"/> is the motion
    /// amount (1 Full, 0.5 Reduced).
    /// </summary>
    public static BackWordPose Word(double age, double amp, bool motionOff, bool gentle)
    {
        if (age < 0 || age >= Life(motionOff, gentle)) return default;
        if (motionOff)
        {
            // Still: it fades up where it stands, holds, fades out. No zoom, no tilt, no ghosts.
            double a = age < StillRiseSec ? age / StillRiseSec
                : age < StillRiseSec + StillHoldSec ? 1
                : 1 - (age - StillRiseSec - StillHoldSec) / StillOutSec;
            return new BackWordPose(true, Smooth(a), 1, 1, 0, 0, 0);
        }
        double zoom = gentle ? GentleZoomSec : ZoomSec, hold = gentle ? GentleHoldSec : HoldSec, outs = gentle ? GentleOutSec : OutSec;
        double from = 1 - (1 - FarScale) * amp;   // Reduced comes from half as far
        if (age < zoom)
        {
            double u = age / zoom, e = ZoomEase(u);
            double rise = gentle ? Math.Min(1, age / GentleRiseSec) : Math.Min(1, u / 0.12);
            // the slot's wobble: tilt left and stretch at 40%, tilt right and squash at 72%, level at the end
            double tilt = Bump(u, 0.4, 0.3) * TiltA + Bump(u, 0.72, 0.22) * TiltB;
            double sy = 1 + 0.05 * Bump(u, 0.4, 0.3) - 0.02 * Bump(u, 0.72, 0.22);
            return new BackWordPose(true, Smooth(rise), from + (1 - from) * e, 1 + (sy - 1) * amp, tilt * amp, 0, 0);
        }
        if (age < zoom + hold)
        {
            // a breath at full size: 3% in and back, a touch taller
            double u = (age - zoom) / hold, b = Math.Sin(Math.PI * u);
            return new BackWordPose(true, 1, 1 - 0.03 * b * amp, 1 + 0.02 * b * amp, 0, 0, 0);
        }
        {
            // it passes the viewer: on past full size, fading, the ghosts pulling apart
            double u = (age - zoom - hold) / outs;
            double grow = 1 + (PassScale - 1) * amp * EaseOut(u);
            double split = gentle ? 0 : SplitMax * amp * EaseOut(u);
            double ghost = gentle ? 0 : 0.75 * Math.Sin(Math.PI * Math.Min(1, u * 1.25));
            return new BackWordPose(true, 1 - Smooth(u), grow, 1, 0, split, Math.Max(0, ghost));
        }
    }

    /// <summary>How fast the back's spiral turns, radians a second (Gentle slower, Reduced half, Off still).</summary>
    public static double TurnRate(bool motionOff, bool gentle, double amp)
        => motionOff ? 0 : (gentle ? 0.35 : 0.9) * amp;

    // ---- the phrase on the card ----

    /// <summary>The longest phrase a back carries (they wrap onto up to three lines).</summary>
    public const int MaxChars = 40;
    /// <summary>Past this many characters a phrase takes two lines (the slot's WORD_WRAP_AT).</summary>
    public const int WrapAt = 12;
    public const int MaxLines = 3;
    /// <summary>At full size the block fills at most this share of the card's width and of its height.</summary>
    public const double FitW = 0.9, FitH = 0.45;

    /// <summary>
    /// Break a phrase into 1 to 3 lines at its spaces, as even as it can: one line up to <see cref="WrapAt"/>
    /// characters, then two, then three. A single long word stays whole (the draw scales it to fit).
    /// </summary>
    public static string[] Lines(string text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return Array.Empty<string>();
        string joined = string.Join(' ', words);
        if (words.Length == 1 || joined.Length <= WrapAt) return new[] { joined };
        int lines = Math.Min(Math.Min(MaxLines, words.Length), joined.Length > WrapAt * 2 ? 3 : 2);
        // greedy fill toward an even target per line
        double target = joined.Length / (double)lines;
        var result = new List<string>(lines);
        var cur = new System.Text.StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            int left = words.Length - i, slots = lines - result.Count;
            if (cur.Length > 0 && slots > 1 && (cur.Length + 1 + words[i].Length > target + 2 || left < slots))
            {
                result.Add(cur.ToString());
                cur.Clear();
            }
            if (cur.Length > 0) cur.Append(' ');
            cur.Append(words[i]);
        }
        if (cur.Length > 0) result.Add(cur.ToString());
        return result.ToArray();
    }

    /// <summary>
    /// The font size in px that fits a block whose widest line measures <paramref name="widestAtOne"/> px at
    /// size 1 and has <paramref name="lines"/> lines, inside a card <paramref name="cardW"/> by <paramref name="cardH"/>,
    /// with the pass's growth allowed for. <paramref name="lineHeight"/> is in font sizes.
    /// </summary>
    public static double FontPx(double widestAtOne, int lines, double cardW, double cardH, double lineHeight = 1.08)
    {
        if (widestAtOne <= 0 || lines <= 0) return 0;
        double byW = cardW * FitW / PassScale / widestAtOne;
        double byH = cardH * FitH / PassScale / (lines * lineHeight);
        return Math.Max(6, Math.Min(Math.Min(byW, byH), cardH * 0.22));
    }

    // ---- the spiral ----

    /// <summary>The house threads a back's spiral is spun from, with the mod's accent first.</summary>
    public static uint[] Palette(uint accentRgb)
    {
        accentRgb &= 0xFFFFFF;
        return new[] { accentRgb, Lighten(accentRgb, 0.45), 0xff5fa8u, 0xc77dffu, 0xffffffu };
    }

    /// <summary>The ground behind the spiral: the accent, very dark.</summary>
    public static uint Ground(uint accentRgb) => 0xFF000000u | Scale(accentRgb & 0xFFFFFF, 0.14);

    /// <summary>
    /// A new spiral for a back: the Loom's dice (<see cref="LoomSpec.Random"/>) for shape, threads from
    /// <see cref="Palette"/>, no pulse and no wobble (the back turns as a whole). Never the same shape as
    /// <paramref name="last"/> twice running.
    /// </summary>
    public static LoomSpec Roll(Random rng, uint accentRgb, LoomSpec? last = null)
    {
        var pal = Palette(accentRgb);
        LoomSpec s = LoomSpec.Random(rng);
        for (int tries = 0; tries < 6 && last != null && SameShape(s, last); tries++) s = LoomSpec.Random(rng);
        s.PulseAmp = 0; s.WobAmp = 0;
        s.Layer.Arms = Math.Max(2, s.Layer.Arms);
        s.Layer.Colors = Threads(pal, 1 + rng.Next(3), rng, first: true);
        s.Layer2.Colors = Threads(pal, 1, rng, first: false);
        return s;
    }

    /// <summary>Two spirals read as the same one: same weave style, arms and turns.</summary>
    public static bool SameShape(LoomSpec a, LoomSpec b)
        => a.Layer.Style == b.Layer.Style && a.Layer.Arms == b.Layer.Arms && Math.Abs(a.Layer.Turns - b.Layer.Turns) < 0.01;

    private static uint[] Threads(uint[] pal, int count, Random rng, bool first)
    {
        var pool = (uint[])pal.Clone();
        for (int i = pool.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }
        if (first)
        {
            // the accent always leads, so a back reads as this mod's
            int at = Array.IndexOf(pool, pal[0]);
            if (at > 0) (pool[0], pool[at]) = (pool[at], pool[0]);
        }
        return pool[..Math.Clamp(count, 1, pool.Length)];
    }

    private static uint Lighten(uint rgb, double k)
    {
        uint r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
        static uint L(uint c, double k) => (uint)Math.Round(c + (255 - c) * k);
        return (L(r, k) << 16) | (L(g, k) << 8) | L(b, k);
    }

    private static uint Scale(uint rgb, double k)
    {
        uint r = (rgb >> 16) & 255, g = (rgb >> 8) & 255, b = rgb & 255;
        static uint S(uint c, double k) => (uint)Math.Round(c * k);
        return (S(r, k) << 16) | (S(g, k) << 8) | S(b, k);
    }

    /// <summary>The slot's cubic-bezier(.2,.7,.2,1): quick off the mark, a long soft arrival.</summary>
    public static double ZoomEase(double u)
    {
        u = Math.Clamp(u, 0, 1);
        // solve the bezier's x for t (Newton), then read y
        double t = u;
        for (int i = 0; i < 6; i++)
        {
            double x = Bez(t, 0.2, 0.2) - u, dx = BezD(t, 0.2, 0.2);
            if (Math.Abs(dx) < 1e-6) break;
            t = Math.Clamp(t - x / dx, 0, 1);
        }
        return Bez(t, 0.7, 1);
    }

    private static double Bez(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
    private static double BezD(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * p1 + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (1 - p2);

    /// <summary>A smooth 0..1..0 bump centred on <paramref name="at"/>, <paramref name="width"/> wide each side.</summary>
    private static double Bump(double u, double at, double width)
    {
        double d = Math.Abs(u - at) / width;
        return d >= 1 ? 0 : 0.5 + 0.5 * Math.Cos(Math.PI * d);
    }

    private static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    private static double EaseOut(double x) { x = Math.Clamp(x, 0, 1); return 1 - (1 - x) * (1 - x); }
}
