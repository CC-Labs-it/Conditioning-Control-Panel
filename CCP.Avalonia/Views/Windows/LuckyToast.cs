using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// WPF MainWindow.Enhancements.cs OnLuckyProc / EnsureLuckyToast / PlaceLuckyToast / CloseLuckyToast (:2131-2289):
    /// the "LUCKY! Nx XP!" toast. ONE window, built on the first proc and reused (ccp-bugs #1312, Core
    /// <see cref="LuckyToastRule"/>): a proc while it is up only refreshes text and hold; a game owning the screen or the
    /// perk opt-out skips it. Fades in 200 ms, holds 3 s after the LAST proc, fades out 300 ms and hides; closed for
    /// real when the shell closes. Raised where this head rolls the proc (FlashOverlay, WPF RollLuckyFlash).
    /// </summary>
    internal static class LuckyToast
    {
        private static Window? _popup;
        private static Border? _border;
        private static TextBlock? _title, _multiplier;
        private static DoubleTransition? _fade;
        private static DispatcherTimer? _hold, _fadeOut;

        /// <summary>Test seams: the reused window, whether the hold runs; the game-surface probe.</summary>
        internal static Window? Popup => _popup;
        internal static bool Holding => _hold?.IsEnabled == true;
        internal static Func<bool> GameOwnsScreen = PanicSurfaces.AnyOwnsTheScreen;

        /// <summary>WPF OnLuckyProc. <paramref name="procType"/> is the mod-aware label; "Flash" in it means gold.</summary>
        internal static void Proc(string procType, int multiplier)
        {
            if (CoreSettings.Current.SuppressPerkNotifications) return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var decision = LuckyToastRule.Decide(CoreSettings.Current.SuppressPerkNotifications, GameOwnsScreen(), _popup?.IsVisible == true);
                    if (decision == LuckyToastRule.Decision.Skip) return;
                    var popup = Ensure();
                    var glow = procType.Contains("Flash") ? Color.FromRgb(0xFF, 0xD7, 0x00) : Color.FromRgb(0xFF, 0x69, 0xB4);
                    _border!.BorderBrush = new SolidColorBrush(glow);
                    _border.BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Color = Color.FromArgb(204, glow.R, glow.G, glow.B) });
                    _title!.Foreground = new SolidColorBrush(glow);
                    _multiplier!.Text = $"{multiplier}x XP!";

                    _fadeOut!.Stop();   // cancel a fade-out in flight
                    if (decision == LuckyToastRule.Decision.Show)
                    {
                        _fade!.Duration = TimeSpan.FromMilliseconds(200);
                        popup.Opacity = 0;
                        popup.Show();
                        popup.Opacity = 1;
                    }
                    else popup.Opacity = 1;
                    Place(popup);
                    _hold!.Stop();
                    _hold.Start();
                }
                catch (Exception ex) { Log.Debug("Lucky proc popup failed: {Error}", ex.Message); }
            });
        }

        private static Window Ensure()
        {
            if (_popup != null) return _popup;
            _title = new TextBlock { Text = "LUCKY!", FontWeight = FontWeight.Bold, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center };
            _multiplier = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB6, 0xC1)), FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
            };
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(_title);
            stack.Children.Add(_multiplier);
            // The glow is the Border's own BoxShadow (WPF DropShadowEffect, P82 rule 3); the margin leaves it room.
            _border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x15, 0x15, 0x30)), CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2), Padding = new Thickness(20, 12, 20, 12), Margin = new Thickness(30), Child = stack,
            };
            _fade = new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) };
            var popup = new Window
            {
                Title = "CCP - Lucky", WindowDecorations = WindowDecorations.None, Background = Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }, CanResize = false, ShowInTaskbar = false,
                ShowActivated = false, Topmost = true, Focusable = false, IsHitTestVisible = false,
                SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual,
                Content = _border, Transitions = new Transitions { _fade },
            };
            popup.SizeChanged += (_, _) => Place(popup);

            // Hold 3 s after the LAST proc, then fade out and HIDE (never close: the window is reused).
            _hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hold.Tick += (_, _) => HoldElapsed();
            _fadeOut = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _fadeOut.Tick += (_, _) => FadeOutDone();
            _popup = popup;
            return popup;
        }

        /// <summary>The hold timer's tick (tests call it instead of waiting).</summary>
        internal static void HoldElapsed()
        {
            _hold?.Stop();
            if (_popup == null) return;
            _fade!.Duration = TimeSpan.FromMilliseconds(300);
            _popup.Opacity = 0;
            _fadeOut!.Start();
        }

        /// <summary>The fade-out's end: hide, unless a proc landed during the fade and restarted the hold.</summary>
        internal static void FadeOutDone()
        {
            _fadeOut?.Stop();
            if (Holding) return;
            try { _popup?.Hide(); } catch { }
        }

        /// <summary>WPF PlaceLuckyToast: top-centre of the primary work area (physical pixels).</summary>
        private static void Place(Window popup)
        {
            try
            {
                if (popup.Screens.Primary is not { } screen) return;
                var wa = screen.WorkingArea;
                var width = (int)(popup.Bounds.Width * screen.Scaling);
                popup.Position = new PixelPoint(wa.X + (width > 0 ? (wa.Width - width) / 2 : wa.Width / 2), wa.Y + (int)((40 - 30) * screen.Scaling));   // the card 40 below, net of the glow margin
            }
            catch { }
        }

        /// <summary>WPF CloseLuckyToast: a real close from the shell's shutdown (a hidden window still counts
        /// against OnLastWindowClose).</summary>
        internal static void Close()
        {
            _hold?.Stop();
            _fadeOut?.Stop();
            try { _popup?.Close(); } catch { }
            _popup = null;
        }
    }
}
