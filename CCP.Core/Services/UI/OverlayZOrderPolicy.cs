using System.Collections.Generic;

namespace ConditioningControlPanel.Services.UI;

/// <summary>WPF OverlayService.cs:3301: choose the video on the target monitor.</summary>
public static class OverlayZOrderPolicy
{
    public static T? ResolveVideoAnchor<T>(IReadOnlyList<(T Window, int Monitor)> windows,
        T target, int monitor, T? fallback) where T : class
    {
        if (ReferenceEquals(target, fallback)) return null;
        foreach (var entry in windows)
            if (ReferenceEquals(entry.Window, target)) return null;
        if (monitor >= 0)
            foreach (var entry in windows)
                if (entry.Monitor == monitor) return entry.Window;
        return fallback;
    }
}
