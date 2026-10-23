// Adapted from parity/trunk 119ec005fcfa4c05a3ced4bd390c84ecf2497a03, WPF MainWindow.Awareness.cs.
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AwarenessTabView
{
    private void RefreshAwarenessSeenAppChips()
    {
        AwarenessSeenAppsPanel.Children.Clear();
        var listed = CoreSettings.Current.KeywordTriggerApps;
        var seen = Platform.KeywordTriggerHead.Engine.RecentForegroundApps
            .Where(a => !AwarenessAppList.MatchesAppList(listed, a)).ToArray();
        TxtAwarenessSeenAppsLabel.IsVisible = seen.Length > 0;
        foreach (var app in seen)
        {
            var chip = new Button
            {
                Content = "+ " + app, Tag = app, FontSize = 10,
                Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 5, 5),
                Cursor = new Cursor(StandardCursorType.Hand), Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x44)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x5A)), BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(chip, $"Add '{app}' to the list");
            chip.Click += (_, _) =>
            {
                var s = CoreSettings.Current;
                var list = new List<string>(s.KeywordTriggerApps ?? new());
                if (!AwarenessAppList.MatchesAppList(list, app)) list.Add(app);
                s.KeywordTriggerApps = list;
                CoreSettings.Save();
                SyncAwarenessTabUi();
            };
            AwarenessSeenAppsPanel.Children.Add(chip);
        }
    }
}
