using System;

namespace ConditioningControlPanel.Services.UI;

/// <summary>
/// Declutter wave 1 (2026-11-15), WPF BannerFxRules.ShowsOnTab: the header banner belongs to Home.
/// It shows on the dashboard tab ("settings") and its permanent alias "progression", and is
/// collapsed on every other tab so the header row breathes. Case is not significant; a null or
/// empty key means no navigation has happened yet, which is the dashboard the app lands on.
/// </summary>
public static class HeaderBannerRule
{
    public static bool ShowsOnTab(string? tabKey) =>
        string.IsNullOrEmpty(tabKey)
        || string.Equals(tabKey, "settings", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tabKey, "progression", StringComparison.OrdinalIgnoreCase);
}
