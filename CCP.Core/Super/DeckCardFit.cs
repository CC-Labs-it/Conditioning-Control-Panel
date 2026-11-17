namespace ConditioningControlPanel.SuperCore;

/// <summary>A card's box in world px: left, top, width, height.</summary>
public readonly record struct CardBox(double X, double Y, double W, double H)
{
    public double Cx => X + W / 2;
    public double Cy => Y + H / 2;
}

/// <summary>
/// The Deck card takes its picture's shape (owner 2026-11-17: "we are always creating a rectangle rn,
/// leaving the sides often empty, especially if the image has vertical proportions"). A flash spawns
/// at its first picture's shape, but a flip used to keep that box for every picture after it, so a
/// tall picture flipped onto a wide card sat in black side bars. Now each new face reshapes the card:
/// same area (a card neither grows nor shrinks as you flip), the new picture's aspect, about the same
/// centre, kept on its monitor. The change runs while the new face opens, so it never pops. Pure maths.
/// </summary>
public static class DeckCardFit
{
    /// <summary>The card never gets a side shorter than this (the flash spawn's own floor).</summary>
    public const double MinSide = 50;
    /// <summary>A reshaped card stays inside this share of its monitor each way.</summary>
    public const double MaxShare = 0.9;

    /// <summary>
    /// The picture area's new size for a picture <paramref name="aspect"/> (width / height) wide, from
    /// an area <paramref name="w"/> by <paramref name="h"/>: the same area, held inside
    /// <paramref name="maxW"/> by <paramref name="maxH"/> (0 = no limit) and never under <see cref="MinSide"/>.
    /// </summary>
    public static (double W, double H) Reshape(double w, double h, double aspect, double maxW = 0, double maxH = 0)
    {
        if (!(aspect > 0) || double.IsInfinity(aspect) || w <= 0 || h <= 0) return (w, h);
        double area = w * h;
        double nw = Math.Sqrt(area * aspect), nh = Math.Sqrt(area / aspect);
        if (maxW > 0 && nw > maxW) { nw = maxW; nh = nw / aspect; }
        if (maxH > 0 && nh > maxH) { nh = maxH; nw = nh * aspect; }
        if (nw < MinSide) { nw = MinSide; nh = Math.Max(nh, nw / aspect); }
        if (nh < MinSide) { nh = MinSide; nw = Math.Max(nw, nh * aspect); }
        return (nw, nh);
    }

    /// <summary>
    /// The whole card box for a new picture: <paramref name="pad"/> px of glow inset on each side stays
    /// as it was, the picture area reshapes (<see cref="Reshape"/>), the centre stays put unless that would
    /// push the card off its monitor <paramref name="bounds"/> (a zero-size bounds = no monitor known).
    /// </summary>
    public static CardBox Refit(CardBox card, double pad, double aspect, CardBox bounds)
    {
        double iw = Math.Max(1, card.W - 2 * pad), ih = Math.Max(1, card.H - 2 * pad);
        bool known = bounds.W > 0 && bounds.H > 0;
        double maxW = known ? bounds.W * MaxShare - 2 * pad : 0, maxH = known ? bounds.H * MaxShare - 2 * pad : 0;
        var (nw, nh) = Reshape(iw, ih, aspect, Math.Max(0, maxW), Math.Max(0, maxH));
        double w = nw + 2 * pad, h = nh + 2 * pad;
        double x = card.Cx - w / 2, y = card.Cy - h / 2;
        if (known)
        {
            x = Math.Clamp(x, bounds.X, Math.Max(bounds.X, bounds.X + bounds.W - w));
            y = Math.Clamp(y, bounds.Y, Math.Max(bounds.Y, bounds.Y + bounds.H - h));
        }
        return new CardBox(x, y, w, h);
    }

    /// <summary>
    /// The box to draw <paramref name="k"/> of the way (0..1, eased here) from the old shape to the new.
    /// Size and centre move together, so the card turns into its new shape rather than jumping to it.
    /// </summary>
    public static CardBox Between(CardBox from, CardBox to, double k)
    {
        k = Math.Clamp(k, 0, 1);
        k = k * k * (3 - 2 * k);
        double w = from.W + (to.W - from.W) * k, h = from.H + (to.H - from.H) * k;
        double cx = from.Cx + (to.Cx - from.Cx) * k, cy = from.Cy + (to.Cy - from.Cy) * k;
        return new CardBox(cx - w / 2, cy - h / 2, w, h);
    }

    /// <summary>How far the reshape has run <paramref name="sinceSwap"/> seconds after the new face landed
    /// over a reshape of <paramref name="span"/> seconds (the opening half of the flip, or the Off cross-fade).</summary>
    public static double Progress(double sinceSwap, double span) => span <= 0 ? 1 : Math.Clamp(sinceSwap / span, 0, 1);
}
