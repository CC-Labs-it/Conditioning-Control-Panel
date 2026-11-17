namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// The fog colours the shop sells (slot <c>fog</c>, and <c>fog2</c> for two-tone): which colour a
/// worn row paints, which second colour the mix takes, and how long a change takes to show. Steam
/// and the pink filter's tint both wear it. Pink is the free row and means "the filter's own colour"
/// (the user's pick, else the mod's, else hot pink), so wearing nothing draws exactly today's look.
/// Colour values from the passed mockup's <c>FOGS</c> table.
/// </summary>
public static class FogLooks
{
    public const string Pink = "fx.fog.pink", Lilac = "fx.fog.lilac", Mint = "fx.fog.mint", Amber = "fx.fog.amber";

    /// <summary>The colour a worn row paints, or null for "the filter's own colour" (Pink, nothing
    /// worn, or a row this build does not know).</summary>
    public static (byte R, byte G, byte B)? ColourOf(string? row) => row switch
    {
        Lilac => (0xA9, 0x8B, 0xFF),
        Mint => (0x5F, 0xE0, 0xB8),
        Amber => (0xFF, 0xB3, 0x47),
        _ => null,
    };

    public static bool Known(string? row) => row is Pink or Lilac or Mint or Amber;

    /// <summary>The colour shown for <paramref name="row"/> over the filter's <paramref name="own"/> colour.</summary>
    public static (byte R, byte G, byte B) Wear(string? row, (byte R, byte G, byte B) own) => ColourOf(row) ?? own;

    /// <summary>The second colour of the mix: <paramref name="fog2"/>, unless it is the same row as the
    /// first colour (then pink against lilac, as the mockup's <c>secondFog</c>), so a mix never draws one colour.</summary>
    public static string SecondRow(string? fog, string? fog2)
    {
        string first = Known(fog) ? fog! : Pink;
        string second = Known(fog2) ? fog2! : Lilac;
        if (second != first) return second;
        return second == Pink ? Lilac : Pink;
    }

    /// <summary>Seconds a worn colour change takes to cross-fade in. A whole-screen colour never jumps;
    /// under Gentle visuals it takes at least the user's own fade.</summary>
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
