using System;

namespace ConditioningControlPanel.Services.Possession;

/// <summary>WPF MainWindow.Possession.cs display names, shared so both heads name a victim alike.</summary>
public static class PossessionTargetNames
{
    public static string For(PossessionRole role, string? explicitName, params string?[] candidates)
    {
        if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
        string? text = null;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var tidy = candidate.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            while (tidy.Contains("  ", StringComparison.Ordinal)) tidy = tidy.Replace("  ", " ", StringComparison.Ordinal);
            if (tidy.Length is < 2 or > 32) continue;
            text = tidy;
            break;
        }
        if (text == null) return role switch
        {
            PossessionRole.Button => "that button", PossessionRole.Card => "that card",
            PossessionRole.Toggle => "that toggle", PossessionRole.Title => "the title",
            PossessionRole.Label => "that label", PossessionRole.TabHeader => "that tab",
            PossessionRole.Timer => "the timer", PossessionRole.Slider => "that slider",
            PossessionRole.Combo => "that dropdown", PossessionRole.Image => "that picture",
            PossessionRole.Scroll => "that list", PossessionRole.Progress => "that bar",
            PossessionRole.TextBox => "that box", _ => "something",
        };
        var noun = role switch
        {
            PossessionRole.Button => " button", PossessionRole.Toggle => " toggle",
            PossessionRole.TabHeader => " tab", PossessionRole.Slider => " slider",
            PossessionRole.Combo => " dropdown", PossessionRole.Card => " card",
            PossessionRole.Title => " heading", PossessionRole.Label => " label", _ => "",
        };
        return "the " + text + noun;
    }
}
