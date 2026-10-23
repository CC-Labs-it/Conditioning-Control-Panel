using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The "LUCKY! 10x XP!" toast: WPF MainWindow.OnLuckyProc / EnsureLuckyToast / PlaceLuckyToast
    /// (ConditioningControlPanel/MainWindow/MainWindow.Enhancements.cs:2173-2333). ONE window, built on
    /// the first proc and reused (ccp-bugs #1312, Core <see cref="LuckyToastRule"/>): a proc while it is
    /// up refreshes the text and the hold, a game surface owning the screen skips it. Fades in 200 ms,
    /// holds 3 s after the LAST proc, fades out 300 ms and hides; closed for real at shell exit.
    /// </summary>
    internal sealed class LuckyToastWindow : Window
    {
        private static LuckyToastWindow? _live;
        private readonly Border _border;
        private readonly TextBlock _title, _multiplier;
        private readonly DispatcherTimer _hold;
        private BeatLoop? _fade;
        private Action? _fadeDone;

        /// <summary>The reused toast, or null before the first proc (test seam).</summary>
        internal static LuckyToastWindow? Live => _live;

        /// <summary>WPF AnyGameSurfaceOwnsTheScreen (tests swap it).</summary>
        internal static Func<bool> GameOwnsScreen = PanicSurfaces.AnyOwnsTheScreen;

        public LuckyToastWindow()
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            CanResize = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            _title = new TextBlock { Text = "LUCKY!", FontWeight = FontWeight.Bold, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center };
            _multiplier = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB6, 0xC1)), FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0),
            };
            _border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x15, 0x15, 0x30)),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(20, 12, 20, 12),
                Child = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = { _title, _multiplier } },
            };
            Content = _border;
            SizeChanged += (_, _) => Place();
            _hold = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _hold.Tick += (_, _) => HoldElapsed();
        }

        /// <summary>WPF OnLuckyProc: <paramref name="procType"/> is the (mod-aware) proc label; a label
        /// with "Flash" in it wears gold, anything else pink, exactly as WPF decides it.</summary>
        internal static void OnLuckyProc(string procType, int multiplier)
        {
            if (CoreSettings.Current.SuppressPerkNotifications) return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var decision = LuckyToastRule.Decide(CoreSettings.Current.SuppressPerkNotifications,
                        GameOwnsScreen(), _live?.IsVisible == true);
                    if (decision == LuckyToastRule.Decision.Skip) return;
                    (_live ??= new LuckyToastWindow()).Refresh(procType, multiplier, decision == LuckyToastRule.Decision.Show);
                }
                catch (Exception ex) { Log.Debug("Lucky proc popup failed: {E}", ex.Message); }
            });
        }

        private void Refresh(string procType, int multiplier, bool show)
        {
            var c = procType.Contains("Flash") ? Color.FromRgb(0xFF, 0xD7, 0x00) : Color.FromRgb(0xFF, 0x69, 0xB4);
            _border.BorderBrush = new SolidColorBrush(c);
            // WPF's DropShadow (blur 30, 0.8); P82: a BoxShadow, not an Effect.
            _border.BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Color = Color.FromArgb(204, c.R, c.G, c.B) });
            _title.Foreground = new SolidColorBrush(c);
            _multiplier.Text = $"{multiplier}x XP!";
            StopFade();   // a fade-out in flight is cancelled; hold at full opacity again
            if (show)
            {
                Opacity = 0;
                Show();
                FadeTo(0, 1, 0.2, null);
            }
            else Opacity = 1;
            Place();
            _hold.Stop();
            _hold.Start();
        }

        /// <summary>3 s after the last proc: fade out, then hide (never close: the window is reused).
        /// A proc that lands during the fade restarted the hold and keeps it up.</summary>
        internal void HoldElapsed()
        {
            _hold.Stop();
            FadeTo(1, 0, 0.3, () => { if (!_hold.IsEnabled) Hide(); });
        }

        internal bool Holding => _hold.IsEnabled;
        internal string MultiplierText => _multiplier.Text ?? "";
        internal IBrush? Accent => _border.BorderBrush;

        private void FadeTo(double from, double to, double seconds, Action? done)
        {
            StopFade();
            _fadeDone = done;
            var fade = new BeatLoop(this, t =>
            {
                var k = Math.Min(1, t / seconds);
                Opacity = from + (to - from) * k;
                if (k >= 1) CompleteFade();
            });
            _fade = fade;
            fade.Start();
        }

        /// <summary>Ends the running fade at once, running what follows it (tests use it).</summary>
        internal void CompleteFade()
        {
            var done = _fadeDone;
            StopFade();
            done?.Invoke();
        }

        private void StopFade()
        {
            _fade?.Stop();
            _fade = null;
            _fadeDone = null;
        }

        /// <summary>Top-centre of the primary work area, 40 DIPs down.</summary>
        private void Place()
        {
            if (Screens.Primary is not { } screen) return;
            var wa = screen.WorkingArea;
            var k = screen.Scaling > 0 ? screen.Scaling : 1;
            var x = Bounds.Width > 0 ? wa.X + (int)((wa.Width - Bounds.Width * k) / 2) : Position.X;
            Position = new PixelPoint(x, wa.Y + (int)(40 * k));
        }

        /// <summary>WPF CloseLuckyToast: the real close at shell exit (a hidden window still counts
        /// against OnLastWindowClose).</summary>
        internal static void CloseLive()
        {
            if (_live is not { } w) return;
            _live = null;
            w._hold.Stop();
            w.StopFade();
            try { w.Close(); } catch (Exception ex) { Log.Debug("Lucky toast close failed: {E}", ex.Message); }
        }
    }
}
