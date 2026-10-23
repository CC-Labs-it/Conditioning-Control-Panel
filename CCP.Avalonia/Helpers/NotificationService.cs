using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    public enum NotificationType { Info, Success, Warning, Error }

    /// <summary>
    /// In-app corner toasts, ported from ConditioningControlPanel/Services/Notifications/
    /// NotificationService.cs: same colours, sizes, 5 s default, action button and × dismiss,
    /// newest stacked below in MainShellWindow's NotificationHost. Toasts fired before the host
    /// attaches are replayed on attach, as WPF does.
    /// Sticky notices survive until dismissed and remember user dismissal in shared settings.
    /// </summary>
    public sealed class NotificationService
    {
        private Panel? _host;
        private readonly List<(string message, NotificationType type, TimeSpan? duration, string? key,
            string? actionLabel, Action? action, string? secondaryLabel, Action? secondaryAction)> _pending = new();
        private readonly Dictionary<string, Border> _stickyByKey = new();
        // Test seam: runs the post-fade removal; tests pass (a, _) => a() instead of waiting 220 ms.
        internal Action<Action, TimeSpan> RemoveLater { get; set; } = (a, d) => DispatcherTimer.RunOnce(a, d);

        public void AttachHost(Panel host)
        {
            _host = host;
            var queue = _pending.ToArray();
            _pending.Clear();
            foreach (var (msg, type, dur, key, label, action, secondaryLabel, secondaryAction) in queue)
            {
                if (key != null) ShowStickyInternal(key, msg, type, label, action);
                else Show(msg, type, dur, label, action, secondaryLabel, secondaryAction);
            }
        }

        public void Show(string message, NotificationType type = NotificationType.Info, TimeSpan? duration = null,
            string? actionLabel = null, Action? action = null,
            string? secondaryActionLabel = null, Action? secondaryAction = null)
        {
            if (_host == null) { _pending.Add((message, type, duration, null, actionLabel, action, secondaryActionLabel, secondaryAction)); return; }

            var border = BuildToast(message, type, null, actionLabel, action, secondaryActionLabel, secondaryAction);
            _host.Children.Add(border);
            border.Opacity = 1; // fades in through the Opacity transition (WPF AnimateIn, 180 ms)

            var timer = new DispatcherTimer { Interval = duration ?? TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timer.Stop(); FadeOutAndRemove(border); };
            border.Tag = timer;
            timer.Start();
        }

        public void ShowSticky(string key, string message, NotificationType type = NotificationType.Info,
            string? actionLabel = null, Action? action = null)
        {
            if (!NotificationDismissals.CanShow(key, CoreSettings.Current.DismissedNotificationKeys)) return;
            if (_host == null) { _pending.Add((message, type, null, key, actionLabel, action, null, null)); return; }
            ShowStickyInternal(key, message, type, actionLabel, action);
        }

        public void Dismiss(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (_stickyByKey.Remove(key, out var border)) FadeOutAndRemove(border);
            _pending.RemoveAll(p => p.key == key);
        }

        private void ShowStickyInternal(string key, string message, NotificationType type, string? label, Action? action)
        {
            if (_stickyByKey.ContainsKey(key)) return;
            var border = BuildToast(message, type, key, label, action);
            _stickyByKey.Add(key, border);
            _host!.Children.Add(border);
            border.Opacity = 1;
        }

        private Border BuildToast(string message, NotificationType type, string? stickyKey,
            string? actionLabel, Action? action, string? secondaryActionLabel = null, Action? secondaryAction = null)
        {
            var accent = new SolidColorBrush(Color.Parse(type switch
            {
                NotificationType.Success => "#4CAF50",
                NotificationType.Warning => "#FFB347",
                NotificationType.Error => "#FF6B6B",
                _ => "#FF69B4",
            }));

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#252542")),
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 10, 10),
                Margin = new Thickness(0, 0, 0, 10),
                MaxWidth = 360,
                Opacity = 0,
                BoxShadow = BoxShadows.Parse("0 2 16 0 #66000000"),
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new QuadraticEaseOut() },
                },
            };

            // Secondary row: the empty-library notices (MainShellWindow.StartStop.cs WarnIf*LibraryEmpty, WPF StartStop.cs:236-274).
            bool hasSecondary = !string.IsNullOrWhiteSpace(secondaryActionLabel) && secondaryAction != null;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                RowDefinitions = new RowDefinitions(hasSecondary ? "Auto,Auto" : "Auto") };
            grid.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 13,
                LineHeight = 20,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });

            Panel buttons = hasSecondary
                ? new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top }
                : new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            if (hasSecondary)
            {
                Grid.SetRow(buttons, 1);
                Grid.SetColumnSpan(buttons, 2);
                buttons.Margin = new Thickness(0, 12, 0, 0);
            }
            else Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            void AddAction(string? label, Action? callback, bool primary)
            {
                if (string.IsNullOrWhiteSpace(label) || callback == null) return;
                // TextBlock content: Avalonia's Button would read '_' in a label as an access key.
                var actionBtn = new Button
                {
                    Name = primary ? "ToastAction" : "ToastSecondaryAction",
                    Content = new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeight.SemiBold,
                        Foreground = primary ? Brushes.Black : Brushes.White },
                    Padding = new Thickness(10, 4),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = primary ? accent : Brushes.Transparent,
                    BorderBrush = accent,
                    BorderThickness = new Thickness(1),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                actionBtn.Click += (_, _) =>
                {
                    (border.Tag as DispatcherTimer)?.Stop();
                    try { callback(); }
                    catch (Exception ex) { Log.Warning(ex, "NotificationService: action button handler failed"); }
                    if (stickyKey != null) _stickyByKey.Remove(stickyKey);
                    FadeOutAndRemove(border);
                };
                buttons.Children.Add(actionBtn);
            }

            AddAction(actionLabel, action, primary: true);
            AddAction(secondaryActionLabel, secondaryAction, primary: false);

            var dismissBtn = new Button
            {
                Name = "ToastDismiss",
                Content = new ConditioningControlPanel.Avalonia.Controls.IconGlyph { Kind = IconKind.Dismiss, Size = 14, Foreground = new SolidColorBrush(Color.Parse("#B0B0C0")) },
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Top,
            };
            global::Avalonia.Automation.AutomationProperties.SetName(dismissBtn, ConditioningControlPanel.Localization.Loc.Get("btn_close"));   // icon-only: name it
            dismissBtn.Click += (_, _) =>
            {
                if (stickyKey != null)
                {
                    if (NotificationDismissals.Remember(CoreSettings.Current.DismissedNotificationKeys, stickyKey))
                        CoreSettings.Save();
                    _stickyByKey.Remove(stickyKey);
                }
                FadeOutAndRemove(border);
            };
            if (hasSecondary)
            {
                Grid.SetColumn(dismissBtn, 1);
                grid.Children.Add(dismissBtn);
            }
            else buttons.Children.Add(dismissBtn);

            border.Child = grid;
            return border;
        }

        private void FadeOutAndRemove(Border border)
        {
            if (!border.IsHitTestVisible) return; // a fading toast cannot run its action twice
            (border.Tag as DispatcherTimer)?.Stop();
            border.IsHitTestVisible = false;
            // WPF FadeOutAndRemove: 220 ms ease-in (the fade-in transition is 180 ms ease-out).
            border.Transitions = new Transitions
            {
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new QuadraticEaseIn() },
            };
            border.Opacity = 0;
            var host = border.Parent as Panel;
            RemoveLater(() => host?.Children.Remove(border), TimeSpan.FromMilliseconds(220));
        }
    }
}
