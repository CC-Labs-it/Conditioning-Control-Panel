namespace ConditioningControlPanel.SuperCore;

/// <summary>A Deck card's look (shop slot <c>card</c>, sold as "Card frames"): the border round the picture,
/// and the same pattern on the back mid-flip. Plain is today's card: no frame, no back.</summary>
public enum DeckBack { Plain, Ink, Check, Lace }

/// <summary>
/// The Deck's card backs (shop rework, owner 2026-11-17, mockup prize-counter-v4 <c>drawBack</c>).
/// A card shows its back through the middle of a flip, around the edge, and while it waits there
/// for its next picture; the old picture closes onto it and the new one opens off it. The timings
/// are the card's own (FlickerDeck): a back only changes WHAT the turning card wears, never how it
/// turns. Plain (the free row) draws no back at all, so the stock card is exactly today's.
/// Pattern sizes are shares of the card's drawn width, so a big flash wears the same back as the
/// mockup's small card. Colours are ARGB.
/// </summary>
public static class DeckLooks
{
    public const string PlainRow = "fx.card.plain", InkRow = "fx.card.ink", CheckRow = "fx.card.check", LaceRow = "fx.card.lace";

    /// <summary>The back a worn row names. Anything unknown is Plain (today's card).</summary>
    public static DeckBack BackOf(string? row) => row switch
    {
        InkRow => DeckBack.Ink,
        CheckRow => DeckBack.Check,
        LaceRow => DeckBack.Lace,
        _ => DeckBack.Plain,
    };

    /// <summary>The back shows from this share of the flip to <see cref="BackTo"/> (the face is about
    /// 70% wide at either end, so the change reads as the card turning over, not as a cut).</summary>
    public const double BackFrom = 0.25, BackTo = 0.75;

    /// <summary>
    /// Does the card wear its back this frame? <paramref name="flipT"/> is seconds into the flip (-1
    /// before it starts), <paramref name="flipSec"/> the flip's length. A flip held edge-on for its
    /// picture sits at the midpoint, so it keeps the back. Motion Off flips without turning: no back.
    /// </summary>
    public static bool BackShows(DeckBack back, double flipT, double flipSec, bool motionOff)
        => back != DeckBack.Plain && !motionOff && flipSec > 0
           && flipT >= flipSec * BackFrom && flipT <= flipSec * BackTo;

    // ---- the house palette (mockup drawBack) ----
    public const uint InkBlack = 0xFF17202B, InkPaper = 0xFFF1EFE6, Pink = 0xFFFF5FA8;
    public const uint LacePaper = 0xFFF6E9F0, LaceThread = 0xFFD98BB0;
    /// <summary>The thin light rim every back wears (white at half).</summary>
    public const uint Rim = 0x80FFFFFF;

    /// <summary>The ground a back is painted on.</summary>
    public static uint Ground(DeckBack back) => back switch
    {
        DeckBack.Ink => InkPaper,
        DeckBack.Check => InkBlack,
        DeckBack.Lace => LacePaper,
        _ => Pink,
    };

    /// <summary>The colour its pattern is drawn in.</summary>
    public static uint Mark(DeckBack back) => back switch
    {
        DeckBack.Ink => InkBlack,
        DeckBack.Check => Pink,
        DeckBack.Lace => LaceThread,
        _ => Pink,
    };

    // ---- pattern geometry, shares of the card width (the mockup card is 80 wide) ----
    /// <summary>Ink: diagonal hatch, one line every 6/80 of the width, 1/80 thick.</summary>
    public const double InkStep = 0.075, InkLine = 0.0125;
    /// <summary>Check: this many squares across.</summary>
    public const int CheckCells = 6;
    /// <summary>Lace: a dot every 9/80 (odd rows shifted half a step), dots 1.8/80 round, a frame 3/80 in.</summary>
    public const double LaceStep = 0.1125, LaceDot = 0.0225, LaceInset = 0.0375, LaceFrame = 0.019;
    public const double RimWidth = 0.0125;

    /// <summary>A share of <paramref name="width"/>, never under one pixel.</summary>
    public static double Px(double share, double width) => Math.Max(1.0, share * width);

    // ---- card frames (owner 2026-11-17: "Turn them into card frames") ----
    // The back shows for a blink mid-flip, so the item read as doing nothing. The same three patterns
    // are now worn as a border round every Deck card, there the whole time the picture is up. The
    // band lies over the picture's own edge (never past the card, so the hit area is unchanged) and
    // covers a small margin only; an ink line runs along both edges of the band, cartoon style.
    // Plain draws no frame: the stock card is exactly today's. A frame is static (no motion to gate).

    /// <summary>Does this look draw a frame? Plain never does.</summary>
    public static bool FrameShows(DeckBack back) => back != DeckBack.Plain;

    /// <summary>The band is this share of the card's shorter side, held between these pixel sizes.</summary>
    public const double FrameShare = 0.055, FrameMinPx = 5, FrameMaxPx = 26;

    /// <summary>The band's width in pixels for a card <paramref name="w"/> by <paramref name="h"/>.
    /// A card too small to carry a band (under 40 px) gets none.</summary>
    public static double FrameBand(double w, double h)
    {
        double side = Math.Min(w, h);
        if (side < 40) return 0;
        return Math.Clamp(side * FrameShare, FrameMinPx, FrameMaxPx);
    }

    /// <summary>The ink line on each edge of the band: a fifth of the band, at least 1.5 px.</summary>
    public static double FrameInk(double band) => Math.Max(1.5, band * 0.2);

    /// <summary>The picture's corner inside the band: the card's own corner less the band, never sharper
    /// than half the band (a cartoon frame has round inner corners even on a square card).</summary>
    public static double FrameInnerRadius(double outerRadius, double band) => Math.Max(outerRadius - band, band * 0.5);

    /// <summary>The pattern's repeat inside the band, in pixels (one tile, built once per size and reused):
    /// Ink = one hatch line per tile, Check = two squares across the band, Lace = one dot per tile.</summary>
    public static int FrameTile(DeckBack back, double band) => back switch
    {
        DeckBack.Ink => Math.Max(3, (int)Math.Round(band * 0.5)),
        DeckBack.Check => Math.Max(2, (int)Math.Round(band * 0.5)),
        DeckBack.Lace => Math.Max(3, (int)Math.Round(band * 0.55)),
        _ => 0,
    };
}
