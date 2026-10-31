using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AwarenessTabView
{
    // WPF MainWindow.Awareness.cs:575-642. Existing label is reused, no new copy.
    private void RefreshSeenApps()
    {
        AwarenessSeenAppsPanel.Children.Clear();
        var listed = CoreSettings.Current.KeywordTriggerApps;
        var seen = Platform.KeywordTriggerHead.RecentApps.Snapshot()
            .Where(a => !AwarenessAppScopeRules.MatchesAppList(listed, a)).ToArray();
        TxtAwarenessSeenAppsLabel.IsVisible = seen.Length > 0;
        foreach (var app in seen)
        {
            var chip = new Button
            {
                Content = "+ " + app, Tag = app, FontSize = 10,
                Padding = new Thickness(7, 3), Margin = new Thickness(0, 0, 5, 5),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)),
                BorderThickness = new Thickness(1),
            };
            chip.Click += (_, _) =>
            {
                var s = CoreSettings.Current;
                var apps = (s.KeywordTriggerApps ?? new()).ToList();
                if (!AwarenessAppScopeRules.MatchesAppList(apps, app)) apps.Add(app);
                s.KeywordTriggerApps = apps;
                CoreSettings.Save();
                TxtAwarenessAppList.Text = string.Join(", ", apps);
                RefreshSeenApps();
            };
            AwarenessSeenAppsPanel.Children.Add(chip);
        }
    }
}
