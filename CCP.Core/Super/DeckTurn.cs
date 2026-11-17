namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// How a Deck card turns over (owner 2026-11-17: "the flip animation might be smoother"). The card
/// rotates about its own upright axis instead of being squeezed flat: the closing face eases in from
/// rest and is quickest as it goes edge-on, the new face comes round from the other side and swings a
/// few degrees past flat before it settles. The face swaps exactly edge-on (half way through), the new
/// face is never mirrored. Width is the cosine of the angle; a light perspective makes the far edge
/// shorter than the near one, and the face darkens a little as it turns away. Pure maths, no WPF, no
/// Skia: the flash layer turns these numbers into a matrix.
/// </summary>
public static class DeckTurn
{
    private const double HalfPi = Math.PI / 2;

    /// <summary>How far the new face swings past flat before it settles (a share of a quarter turn at full
    /// motion: about 5 degrees), over the last part of the flip from <see cref="SettleFrom"/>; the face is
    /// flat by <see cref="OpenBy"/> of its opening half.</summary>
    public const double Settle = 0.025, SettleFrom = 0.6, OpenBy = 0.85;
    /// <summary>The perspective at full strength: the near edge grows and the far edge shrinks by about this share at a quarter turn.</summary>
    public const double Perspective = 0.2;
    /// <summary>The darkest the face gets as it turns away (edge-on), 0..1 of black.</summary>
    public const double ShadeMax = 0.42;
    /// <summary>Motion Off: the old face fades out over the new one over this long (Gentle: never shorter than <see cref="GentleFadeSec"/>).</summary>
    public const double FadeSec = 0.28, GentleFadeSec = 0.5;

    /// <summary>
    /// The face's angle at <paramref name="p"/> (0..1 through the flip), in radians. 0 to +pi/2 while the
    /// old face closes, then -pi/2 back to 0 as the new face opens from the other side, swinging slightly
    /// past 0 on the way (<paramref name="amp"/> 1 full, 0.5 Reduced: half the swing). Exactly +-pi/2 at p = 0.5.
    /// </summary>
    public static double Angle(double p, double amp = 1)
    {
        p = Math.Clamp(p, 0, 1);
        if (p <= 0.5)
        {
            // Sine in: still at rest, quickest edge-on.
            double u = p / 0.5;
            return HalfPi * (1 - Math.Cos(u * HalfPi));
        }
        double v = (p - 0.5) / 0.5;
        // Sine out, flat by three quarters of the way; then a swell that starts and ends still takes it a
        // few degrees past flat and home again: the settle.
        double g = Math.Sin(Math.Min(1, v / OpenBy) * HalfPi);
        double s = Math.Clamp((v - SettleFrom) / (1 - SettleFrom), 0, 1), bump = Math.Sin(Math.PI * s);
        return -HalfPi * (1 - g - Settle * amp * bump * bump);
    }

    /// <summary>The face's drawn width at this angle (never mirrored, never thinner than <paramref name="min"/>).</summary>
    public static double Width(double angle, double min) => Math.Max(min, Math.Abs(Math.Cos(angle)));

    /// <summary>
    /// The perspective term for a card <paramref name="halfWidth"/> px from centre to edge: the matrix's
    /// x-perspective entry, so a point at x maps to x cos / (1 + x * this). Positive = the right edge is
    /// the far one. Reduced motion halves it; Off has no turn at all.
    /// </summary>
    public static double PerspectiveTerm(double angle, double halfWidth, double amp = 1)
        => halfWidth <= 0 ? 0 : Math.Sin(angle) * Perspective * amp / halfWidth;

    /// <summary>How dark the face is at this angle, 0 at rest to <see cref="ShadeMax"/> edge-on (times <paramref name="amp"/>).</summary>
    public static double Shade(double angle, double amp = 1) => ShadeMax * amp * Math.Abs(Math.Sin(angle));

    /// <summary>Motion Off: the old face's opacity <paramref name="sinceSwap"/> seconds after the swap (1 = all old face).</summary>
    public static double FadeOut(double sinceSwap, bool gentle)
    {
        double span = gentle ? GentleFadeSec : FadeSec;
        if (sinceSwap <= 0) return 1;
        if (sinceSwap >= span) return 0;
        double k = sinceSwap / span;
        return 1 - k * k * (3 - 2 * k);
    }

    /// <summary>The fade's length, for callers that keep the old face until it is done.</summary>
    public static double FadeLength(bool gentle) => gentle ? GentleFadeSec : FadeSec;
}
