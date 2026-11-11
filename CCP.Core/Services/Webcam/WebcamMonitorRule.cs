using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services;

internal static class WebcamMonitorRule
{
    internal readonly record struct Choice(int Index, bool TemporaryFallback);

    internal static Choice Select(string? chosenName, IReadOnlyList<string> deviceNames, int primaryIndex)
    {
        bool named = !string.IsNullOrEmpty(chosenName);
        if (named)
            for (int i = 0; i < deviceNames.Count; i++)
                if (string.Equals(chosenName, deviceNames[i], StringComparison.OrdinalIgnoreCase))
                    return new(i, false);
        int fallback = deviceNames.Count == 0 ? -1
            : primaryIndex >= 0 && primaryIndex < deviceNames.Count ? primaryIndex : 0;
        return new(fallback, named);
    }
}
