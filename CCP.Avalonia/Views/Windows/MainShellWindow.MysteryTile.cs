// WPF MainWindow.Presets.cs:1182: repaint the daily gift on the dashboard funnel.
using Avalonia.Controls;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal void RefreshMysteryTile()
        => RefreshMysteryTile(App.DailyFree?.TodayKey, CoreEntitlement.HasPremium);

    internal void RefreshMysteryTile(string? key, bool premium)
    {
        if (SettingsPage is not { } dash) return;
        dash.CardMystery.Title = Loc.Get("mosaic_free_today");
        dash.CardMystery.TierBadge = null;
        dash.MysteryRevealName.Text = MysteryName(key) ?? Loc.Get("mosaic_daily_gift");
        var badge = dash.MysteryRevealBadge;
        badge.Tier = !premium && MysteryName(key) != null ? 1 : 0;
        badge.FreeToday = badge.Tier > 0;
    }

    internal static string? MysteryName(string? key) => key switch
    {
        "takeover" => Loc.Get("tab_takeover"),
        "awareness" => Loc.Get("tab_awareness"),
        "haptics" => Loc.Get("tab_haptics"),
        "voice" => Loc.Get("tab_shelistening"),
        "fyp" => Loc.Get("tab_fyp"),
        "remote" => Loc.Get("tab_remote_control"),
        "dtrh" => "Down the Rabbit Hole",
        _ => null,
    };
}
