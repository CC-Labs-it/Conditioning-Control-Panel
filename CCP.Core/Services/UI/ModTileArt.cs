using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.UI;

/// <summary>WPF MainWindow.ModTileVariant: themed built-in face first, then the base art.
/// Heads decode each candidate and fall through on a missing image.</summary>
public static class ModTileArt
{
    public static IEnumerable<string> Candidates(string baseName, string? modId)
    {
        var suffix = modId switch
        {
            BuiltInMods.BambiSleepId => "_bambi",
            BuiltInMods.SissyHypnoId => "_sissy",
            BuiltInMods.DronificationId => "_drone",
            BuiltInMods.LockedId => "_locked",
            _ => null,
        };
        if (suffix != null) yield return $"features/{baseName}{suffix}.png";
        yield return $"features/{baseName}.png";
    }
}
