using System;

namespace ConditioningControlPanel.Localization;

/// <summary>WPF ModAwareLocText: changed English mod wording wins; otherwise keep localization.</summary>
public static class ModAwareLabelRules
{
    public static string Resolve(string localized, string? english, Func<string, string>? makeModAware)
    {
        if (string.IsNullOrEmpty(english) || makeModAware is null) return localized;
        var modded = makeModAware(english);
        return !string.IsNullOrEmpty(modded) && modded != english ? modded : localized;
    }
}
