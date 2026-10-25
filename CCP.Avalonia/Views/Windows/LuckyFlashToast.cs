using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

// WPF MainWindow.Enhancements OnLuckyProc/EnsureLuckyToast/PlaceLuckyToast.
internal static class LuckyFlashToast
{
    internal static Window? Popup { get; private set; }
    private static DispatcherTimer? _timer;
    private static long _shown;
    private static bool _refresh;
    internal static TimeProvider Clock = TimeProvider.System;

    internal static void Show()
    {
        var decision = LuckyToastRule.Decide(CoreSettings.Current.SuppressPerkNotifications,
            PanicSurfaces.AnyOwnsTheScreen(), Popup?.IsVisible == true);
        if (decision == LuckyToastRule.Decision.Skip) return;
        if (Popup == null)
        {
            var gold = new SolidColorBrush(Color.FromRgb(255, 215, 0));
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(224, 21, 21, 48)),
                CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = gold,
                Padding = new Thickness(20, 12),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "LUCKY!", FontSize = 22, FontWeight = FontWeight.Bold,
                            Foreground = gold, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center },
                        new TextBlock { Text = "10x XP!", FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(255, 182, 193)),
                            Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center },
                    },
                },
            };
            // Sibling shadow, never an Effect on the card.
            var shadow = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(12),
                BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Color = Color.FromArgb(204, 255, 215, 0) }) };
            Popup = new Window
            {
                WindowDecorations = WindowDecorations.None, Background = Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = new Panel { Margin = new Thickness(15), Children = { shadow, card } },
            };
            Popup.SizeChanged += (_, _) => Place();
        }
        _refresh = decision == LuckyToastRule.Decision.Refresh;
        _shown = Clock.GetTimestamp();
        Popup.Opacity = _refresh ? 1 : 0;
        if (!_refresh) Popup.Show();
        Place();
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        _timer.Start();
    }

    private static void Place()
    {
        if (Popup is not { } popup || popup.Screens.Primary is not { } screen) return;
        var area = screen.WorkingArea;
        var k = screen.Scaling;
        popup.Position = new PixelPoint(area.X + (int)((area.Width - popup.Bounds.Width * k) / 2), area.Y + (int)(40 * k));
    }

    internal static void Tick()
    {
        if (Popup is not { } popup) return;
        var ms = Clock.GetElapsedTime(_shown).TotalMilliseconds;
        if (ms >= 3300) { popup.Hide(); _timer?.Stop(); return; }
        popup.Opacity = Math.Clamp(ms >= 3000 ? 1 - (ms - 3000) / 300 : _refresh ? 1 : ms / 200, 0, 1);
    }

    internal static void Close()
    {
        _timer?.Stop();
        _timer = null;
        Popup?.Close();
        Popup = null;
    }
}
