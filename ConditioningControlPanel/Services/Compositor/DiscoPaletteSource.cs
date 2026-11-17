using ConditioningControlPanel.BubbleCore;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// The Disco ball's hues from the active mod: its accent (<see cref="ModService.GetAccentColorHex"/>, which a live
/// event may override) and its secondary (<see cref="ModService.GetSecondaryColorHex"/>), made into a party by
/// <see cref="DiscoPalette.FromMod"/>. Asked once per new Disco bubble, so a mod switch reaches the next bubbles;
/// the palette is rebuilt only when the two colours change. No mod service (tests, early start) = the fallback.
/// </summary>
internal static class DiscoPaletteSource
{
    private sealed record Cached(string Accent, string Secondary, DiscoPalette Palette);

    private static Cached? s_last;

    public static DiscoPalette Current()
    {
        string? accent = null, secondary = null;
        try
        {
            var mods = App.Mods;
            if (mods != null) { accent = mods.GetAccentColorHex(); secondary = mods.GetSecondaryColorHex(); }
        }
        catch (Exception ex) { App.Logger?.Debug("Disco palette: mod colours unavailable: {E}", ex.Message); }
        return For(accent, secondary);
    }

    /// <summary>The palette for two mod colours as hex ("#RRGGBB"); unreadable ones are skipped.</summary>
    internal static DiscoPalette For(string? accentHex, string? secondaryHex)
    {
        if (accentHex == null && secondaryHex == null) return DiscoPalette.Fallback;
        var last = s_last;
        if (last != null && last.Accent == (accentHex ?? "") && last.Secondary == (secondaryHex ?? "")) return last.Palette;
        uint? a = DiscoPalette.TryParseHex(accentHex, out var av) ? av : null;
        uint? s = DiscoPalette.TryParseHex(secondaryHex, out var sv) ? sv : null;
        var palette = DiscoPalette.FromMod(a, s);
        s_last = new Cached(accentHex ?? "", secondaryHex ?? "", palette);
        return palette;
    }
}
