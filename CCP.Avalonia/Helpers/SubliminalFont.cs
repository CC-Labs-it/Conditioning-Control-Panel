using System;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>WPF FontPickerHelper.Resolve for the subliminal picker, cards and color preview.</summary>
internal static class SubliminalFont
{
    internal const string BundledFredoka = "Fredoka (bundled)";

    internal static FontFamily Resolve(string? name)
    {
        name = name?.Trim();
        if (string.Equals(name, BundledFredoka, StringComparison.OrdinalIgnoreCase))
            return new FontFamily(AppFonts.Fredoka);
        // WPF ignores extra links in a stored family rather than letting them change its fallback.
        name = name?.Split(',')[0].Trim();
        return new FontFamily(string.IsNullOrWhiteSpace(name) ? "Arial" : name + ", Arial");
    }
}
