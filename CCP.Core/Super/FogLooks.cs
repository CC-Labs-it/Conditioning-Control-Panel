namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// The fog look the shop sells: Two-tone only (owner 2026-11-17: "The fog colours already follow the pink
/// filter color (they can pick) so we cant sell that... only sell the two colors combined one"). Colour one
/// is always the filter's own colour (the user's pick, else the mod's, else hot pink), exactly as before the
/// shop; Two-tone adds a second colour, picked free from a small fixed palette (slot <c>fog2</c>), that moves
/// through the first. A second colour too close to the user's own would make the swirl vanish, so the next
/// palette colour stands in. Steam and the pink filter's tint both wear it. Colours are the passed mockup's
/// <c>FOGS</c> table plus sky and peach.
/// </summary>
public static class FogLooks
{
    /// <summary>The one fog row on the shelf (the <c>mix</c> switch).</summary>
    public const string Twotone = "fx.fog.twotone";

    /// <summary>Second-colour ids (values of slot <c>fog2</c>, never shop rows).</summary>
    public const string Pink = "fx.fog.pink", Lilac = "fx.fog.lilac", Mint = "fx.fog.mint", Amber = "fx.fog.amber",
        Sky = "fx.fog.sky", Peach = "fx.fog.peach";

    /// <summary>The second colour when none is picked.</summary>
    public const string DefaultSecond = Lilac;

    /// <summary>The palette in swatch order. Order matters: a colour too close to the user's own hands over to the next.</summary>
    public static readonly IReadOnlyList<(string Row, (byte R, byte G, byte B) Rgb)> Palette = new[]
    {
        (Pink, ((byte)0xFF, (byte)0x5F, (byte)0xA8)),
        (Lilac, ((byte)0xA9, (byte)0x8B, (byte)0xFF)),
        (Mint, ((byte)0x5F, (byte)0xE0, (byte)0xB8)),
        (Amber, ((byte)0xFF, (byte)0xB3, (byte)0x47)),
        (Sky, ((byte)0x5F, (byte)0xC0, (byte)0xFF)),
        (Peach, ((byte)0xFF, (byte)0x93, (byte)0x7A)),
    };

    /// <summary>Two colours closer than this (straight distance in RGB, 0 to 441) would swirl into one.</summary>
    public const double CloseDistance = 48;

    public static bool IsPalette(string? row) => ColourOf(row) != null;

    /// <summary>The user's own second colour, written <c>#RRGGBB</c>; null for anything else.</summary>
    public static (byte R, byte G, byte B)? TryHex(string? s)
    {
        if (s == null || s.Length != 7 || s[0] != '#') return null;
        return int.TryParse(s.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int v)
            ? ((byte)(v >> 16), (byte)(v >> 8), (byte)v) : null;
    }

    /// <summary>A palette colour, or null for anything else (old rows, unknown ids).</summary>
    public static (byte R, byte G, byte B)? ColourOf(string? row)
    {
        foreach (var p in Palette) if (p.Row == row) return p.Rgb;
        return null;
    }

    public static bool Close((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        double dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
        return Math.Sqrt(dr * dr + dg * dg + db * db) < CloseDistance;
    }

    /// <summary>The second colour actually shown for the picked <paramref name="fog2"/> over the user's
    /// <paramref name="own"/> colour: the pick (lilac when none or unknown), or, when it sits too close to
    /// <paramref name="own"/>, the next palette colour that does not.</summary>
    public static string SecondRow(string? fog2, (byte R, byte G, byte B) own)
    {
        int start = 0;
        string want = IsPalette(fog2) ? fog2! : DefaultSecond;
        for (int i = 0; i < Palette.Count; i++) if (Palette[i].Row == want) start = i;
        for (int k = 0; k < Palette.Count; k++)
        {
            var p = Palette[(start + k) % Palette.Count];
            if (!Close(p.Rgb, own)) return p.Row;
        }
        return want;
    }

    public static (byte R, byte G, byte B) SecondColour(string? fog2, (byte R, byte G, byte B) own) => ColourOf(SecondRow(fog2, own))!.Value;

    /// <summary>Seconds a change (two-tone on or off, a new second colour) takes to cross-fade in. A
    /// whole-screen colour never jumps; under Gentle visuals it takes at least the user's own fade.</summary>
    public static double EaseSeconds(bool gentle, double gentleFadeSeconds)
        => gentle ? Math.Max(1.2, gentleFadeSeconds) : 1.2;

    /// <summary>Smooth 0..1 progress of an ease that started <paramref name="age"/> seconds ago.</summary>
    public static double EaseAt(double age, double seconds)
    {
        if (seconds <= 0 || age >= seconds) return 1;
        if (age <= 0) return 0;
        double k = age / seconds;
        return k * k * (3 - 2 * k);
    }

    public static (byte R, byte G, byte B) Lerp((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double k)
    {
        if (k <= 0) return a;
        if (k >= 1) return b;
        return ((byte)Math.Round(a.R + (b.R - a.R) * k), (byte)Math.Round(a.G + (b.G - a.G) * k), (byte)Math.Round(a.B + (b.B - a.B) * k));
    }
}
