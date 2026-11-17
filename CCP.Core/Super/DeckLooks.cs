namespace ConditioningControlPanel.SuperCore;

/// <summary>The picture on the back of a Deck card (shop slot <c>card</c>). Plain is today's card: no back.</summary>
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
}
