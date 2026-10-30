// WPF MainWindow.Presets.cs:1163-1283; called by RefreshDashboardRail (:107).
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal void RefreshMysteryTile()
    {
        if (SettingsPage is not { } dash) return;
        var key = SettingsTabView.TodayFreeKey();
        var name = key switch
        {
            "takeover" => Loc.Get("tab_takeover"),
            "awareness" => Loc.Get("tab_awareness"),
            "haptics" => Loc.Get("tab_haptics"),
            "voice" => Loc.Get("tab_shelistening"),
            "fyp" => Loc.Get("tab_fyp"),
            "remote" => Loc.Get("tab_remote_control"),
            "dtrh" => "Down the Rabbit Hole",
            _ => Loc.Get("mosaic_daily_gift"),
        };
        // WPF :1177-1189: the unopened face says the offer, only the reveal names the feature.
        dash.CardMystery.Title = Loc.Get("mosaic_free_today");
        dash.CardMystery.TierBadge = null;
        dash.FindControl<TextBlock>("MysteryRevealName")!.Text = name;
        var path = key switch
        {
            "takeover" => "features/takeover.png",
            "awareness" => "features/awareness.png",
            "haptics" => "features/vibe.png",
            "fyp" => "features/fyp.png",
            "remote" => "features/remote_control.png",
            "dtrh" => "features/dtrh.png",
            _ => "features/mysterybox.png",
        };
        // WPF :1192-1196: mod-aware art, decoded at 512, with the box as fallback.
        var face = dash.FindControl<Border>("MysteryRevealFace")!;
        var artHost = ((Grid)face.Child!).Children.OfType<Border>().First();
        if (artHost.Background is ImageBrush brush)
            brush.Source = ModArt.FirstOf(new[] { path }.Concat(Services.UI.ModTileArt.Candidates("mysterybox", CoreMods.ActiveModId)), 512);
        // WPF :1207-1211: premium owns the pool, so its reveal carries no price sign.
        var tier = CoreEntitlement.HasPremium ? 0 : key switch
        {
            "dtrh" => 2,
            "takeover" or "awareness" or "haptics" or "voice" or "fyp" or "remote" => 1,
            _ => 0,
        };
        var badge = dash.FindControl<TierBadge>("MysteryRevealBadge")!;
        badge.Tier = tier;
        badge.FreeToday = tier > 0;
    }
}
