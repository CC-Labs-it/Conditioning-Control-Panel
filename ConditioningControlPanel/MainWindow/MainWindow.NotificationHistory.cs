using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// ccp-bugs #1688: the title bar bell opens the last in-app toasts (time, kind, text).
    /// Collapsed until something has shown, so a fresh launch has no new glyph to explain.
    /// Opening it never raises anything; the list lives in memory only.
    /// </summary>
    public partial class MainWindow
    {
        private Popup? _notifHistoryPopup;

        private void WireNotificationHistory()
        {
            var history = App.Notifications?.History;
            if (history == null) return;
            SyncNotificationHistoryButton();
            history.Changed += () => Dispatcher.BeginInvoke(new Action(SyncNotificationHistoryButton));
        }

        private void SyncNotificationHistoryButton()
        {
            var count = App.Notifications?.History.Count ?? 0;
            BtnNotificationHistory.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnNotificationHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_notifHistoryPopup?.IsOpen == true) { _notifHistoryPopup.IsOpen = false; return; }
                var history = App.Notifications?.History;
                if (history == null) return;

                // AllowsTransparency false for the same reason as the Inbox popup (layered popup
                // against the avatar tube's render thread).
                _notifHistoryPopup = new Popup
                {
                    Child = BuildNotificationHistoryList(history),
                    PlacementTarget = BtnNotificationHistory,
                    Placement = PlacementMode.Bottom,
                    HorizontalOffset = -300,
                    VerticalOffset = 6,
                    StaysOpen = false,
                    AllowsTransparency = false,
                    PopupAnimation = PopupAnimation.None,
                };
                _notifHistoryPopup.Closed += (_, _) => _notifHistoryPopup = null;
                _notifHistoryPopup.IsOpen = true;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Notification history could not be opened"); }
        }

        private static FrameworkElement BuildNotificationHistoryList(NotificationHistory history)
        {
            var list = new StackPanel();
            list.Children.Add(new TextBlock
            {
                Text = Loc.Get("notif_history_title"),
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
            });

            foreach (var entry in history.Snapshot())
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var stamp = new TextBlock
                {
                    Text = entry.At.ToString("HH:mm"),
                    Foreground = AccentFor(entry.Type),
                    FontSize = 11,
                    Margin = new Thickness(0, 1, 10, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                };
                var text = new TextBlock
                {
                    Text = entry.Text,
                    Foreground = (Brush)new BrushConverter().ConvertFromString("#D6D6E8")!,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetColumn(text, 1);
                row.Children.Add(stamp);
                row.Children.Add(text);
                list.Children.Add(row);
            }

            return new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#252542")!,
                BorderBrush = (Brush)new BrushConverter().ConvertFromString("#3D3D60")!,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 4),
                Width = 340,
                Child = new ScrollViewer
                {
                    MaxHeight = 420,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = list,
                },
            };
        }

        private static Brush AccentFor(NotificationType type) => (Brush)new BrushConverter().ConvertFromString(type switch
        {
            NotificationType.Success => "#4CAF50",
            NotificationType.Warning => "#FFB347",
            NotificationType.Error => "#FF6B6B",
            _ => "#FF69B4",
        })!;
    }
}
