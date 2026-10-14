using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>WPF trigger editor normalization and hand-typed phrase custody, shared by both heads.</summary>
public static class TriggerPhraseEdits
{
    public static Dictionary<string, bool> ForEditor(IEnumerable<string>? triggers) =>
        (triggers ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(t => t, _ => true);

    public static void Apply(AppSettings settings, IReadOnlyDictionary<string, bool> original,
        IReadOnlyDictionary<string, bool> edited, IEnumerable<string>? modDefaults)
    {
        var newTriggers = edited.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
        var defaults = new HashSet<string>(modDefaults ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var oldTriggers = new HashSet<string>(original.Keys, StringComparer.OrdinalIgnoreCase);
        var newSet = new HashSet<string>(newTriggers, StringComparer.OrdinalIgnoreCase);
        foreach (var t in newTriggers)
            if (!oldTriggers.Contains(t) && !defaults.Contains(t)) settings.UserAddedCustomTriggers.Add(t);
        foreach (var t in oldTriggers)
            if (!newSet.Contains(t)) settings.UserAddedCustomTriggers.Remove(t);
        settings.CustomTriggers = newTriggers;
    }
}
