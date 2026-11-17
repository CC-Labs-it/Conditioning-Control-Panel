namespace ConditioningControlPanel.SuperCore;

/// <summary>Where the whisper is this frame. Offsets are shares of the card's width and height from its
/// centre; <see cref="Split"/> is how far the pink and blue ghosts stand off, in font sizes.</summary>
public readonly record struct WhisperPose(bool Visible, bool Front, double Alpha, double Scale, double Dx, double Dy, double Split);

/// <summary>
/// The whisper behind the card (shop slot <c>behind</c>, owner 2026-11-17, mockup prize-counter-v4
/// <c>drawWhisper</c>): on each flip one word from the player's own subliminal pool starts small behind
/// the card, shows through as the card turns edge-on, then comes past the card's edge toward the
/// viewer, growing, thinning into a pink and a blue ghost, and is gone within <see cref="LifeSec"/>.
/// One at a time per card: the next flip replaces it. Motion Off: none. Reduced: half the growth and
/// the ghosting, the same path (the path is what keeps it off the picture). Gentle Visuals: a slow
/// fade in front, already past the edge, no growth, no ghosts.
/// </summary>
public static class DeckWhisper
{
    public const string NoneRow = "fx.behind.none", WhisperRow = "fx.behind.whisper";

    /// <summary>Does the worn row put a word behind the card? Anything else is today's: none.</summary>
    public static bool On(string? row) => row == WhisperRow;

    /// <summary>Seconds from the flip's start until the word is gone.</summary>
    public const double LifeSec = 1.2;
    /// <summary>Behind the card until the flip ends (FlickerDeck.FlipSec), in front after.</summary>
    public const double FrontAtSec = 0.3;
    /// <summary>Fades up over this long, holds until <see cref="HoldToSec"/>, then thins out.</summary>
    public const double RiseSec = 0.2, HoldToSec = 0.7;
    /// <summary>Its brightest, 0..1 (the mockup's 0.6: a whisper, not a subliminal).</summary>
    public const double Strength = 0.6;
    /// <summary>Scale from <see cref="ScaleFrom"/> to ScaleFrom + <see cref="ScaleGrow"/> as it comes on.</summary>
    public const double ScaleFrom = 0.55, ScaleGrow = 0.95;
    /// <summary>Where the path ends: this share of the card's width out to the side and height up.</summary>
    public const double TravelX = 0.5, TravelY = 0.45;
    /// <summary>The ghosts stand off up to this many font sizes by the end.</summary>
    public const double SplitMax = 0.8;
    /// <summary>The word's size: this share of the card's height, clamped (it could get busy).</summary>
    public const double SizeShare = 0.12, SizeMinPx = 12, SizeMaxPx = 44;

    /// <summary>Gentle: the fade in (never under the 300 ms rise floor), the hold, the life, and where on the path it sits.</summary>
    public const double GentleRiseSec = 0.45, GentleHoldToSec = 0.8, GentleLifeSec = 1.5, GentleAt = 0.85;

    public static double LifeFor(bool gentle) => gentle ? GentleLifeSec : LifeSec;

    public static double FontPx(double cardHeight) => Math.Clamp(cardHeight * SizeShare, SizeMinPx, SizeMaxPx);

    /// <summary>Which way the word leaves: up and out, to the right on even flips and the left on odd ones.</summary>
    public static (double X, double Y) Direction(int flips) => (flips % 2 == 0 ? 0.8 : -0.8, -0.6);

    /// <summary>
    /// The whisper <paramref name="age"/> seconds after its flip began. <paramref name="amp"/> is the
    /// motion amount (1 Full, 0.5 Reduced).
    /// </summary>
    public static WhisperPose Sample(double age, double amp, bool motionOff, bool gentle, (double X, double Y) dir)
    {
        if (motionOff || age < 0 || age >= LifeFor(gentle)) return default;
        if (gentle)
        {
            var g = age < GentleRiseSec ? age / GentleRiseSec
                : age < GentleHoldToSec ? 1
                : 1 - (age - GentleHoldToSec) / (GentleLifeSec - GentleHoldToSec);
            return new WhisperPose(true, true, Strength * g, 1, dir.X * TravelX * GentleAt, dir.Y * TravelY * GentleAt, 0);
        }
        var a = age < RiseSec ? age / RiseSec
            : age < HoldToSec ? 1
            : 1 - (age - HoldToSec) / (LifeSec - HoldToSec);
        var e = EaseOut(age / LifeSec);
        var split = age > HoldToSec ? (age - HoldToSec) / (LifeSec - HoldToSec) * SplitMax * amp : 0;
        return new WhisperPose(true, age >= FrontAtSec, Strength * a, ScaleFrom + e * ScaleGrow * amp,
            dir.X * TravelX * e, dir.Y * TravelY * e, split);
    }

    /// <summary>
    /// The next word, from the player's own pool (ticked words only, 1 to 16 letters, lower case),
    /// never the one this card just showed while there is another. Null when the pool is empty.
    /// </summary>
    public static string? Pick(IEnumerable<KeyValuePair<string, bool>>? pool, string? last, Random rng)
    {
        if (pool == null) return null;
        var words = new List<string>();
        foreach (var kv in pool)
        {
            if (!kv.Value || string.IsNullOrWhiteSpace(kv.Key)) continue;
            var w = kv.Key.Trim().ToLowerInvariant();
            if (w.Length is > 0 and <= 16 && !words.Contains(w)) words.Add(w);
        }
        if (words.Count == 0) return null;
        if (words.Count > 1 && last != null) words.Remove(last);
        return words[rng.Next(words.Count)];
    }

    private static double EaseOut(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return 1 - (1 - x) * (1 - x) * (1 - x);
    }
}
