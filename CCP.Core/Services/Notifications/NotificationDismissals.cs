using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services;

/// <summary>Sticky-toast keys retain their exact spelling in the shared settings file.</summary>
public static class NotificationDismissals
{
    public static bool CanShow(string? key, ICollection<string>? dismissed) =>
        !string.IsNullOrWhiteSpace(key) && dismissed?.Contains(key) != true;

    /// <returns>True only for a new user dismissal, when settings need saving.</returns>
    public static bool Remember(ICollection<string> dismissed, string key)
    {
        ArgumentNullException.ThrowIfNull(dismissed);
        if (!CanShow(key, dismissed)) return false;
        dismissed.Add(key);
        return true;
    }
}
