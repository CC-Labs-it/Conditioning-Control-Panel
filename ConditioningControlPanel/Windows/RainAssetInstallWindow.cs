using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The wardrobe door while Rain's big files are missing (lane R12): one plain native card, never a broken
    /// page. Install, a percent bar, Cancel; success opens the dressing room, failure says why and offers
    /// Try again. Closing the window (title X, panic through GameSurfaces <c>rain-wardrobe</c>) cancels the
    /// download. Code-only on purpose; depth from Resources/Theme/Depth.xaml (floating card, sunken bar).
    /// </summary>
    internal sealed class RainAssetInstallWindow : Window
    {
        private static RainAssetInstallWindow? _open;
        public static bool IsOpen => _open != null;

        private readonly RainAssetInstaller _installer;
        private readonly Action _onInstalled;
        private readonly TextBlock _line, _percent;
        private readonly ProgressBar _bar;
        private readonly Button _go, _cancel;
        private CancellationTokenSource? _cts;
        private bool _installed;

        /// <summary>Shows the card (one at a time, focused rather than reopened).</summary>
        public static void ShowFor(RainAssetInstaller installer, Action onInstalled)
        {
            if (_open == null) { _open = new RainAssetInstallWindow(installer, onInstalled); _open.Show(); }
            _open.Activate();
        }

        /// <summary>The panic funnel's half: closes the card, which cancels any download.</summary>
        public static void CloseActive() { try { _open?.Close(); } catch (Exception ex) { Diag.Swallowed(ex); } }

        private RainAssetInstallWindow(RainAssetInstaller installer, Action onInstalled)
        {
            _installer = installer;
            _onInstalled = onInstalled;
            Title = Loc.Get("rain_assets_title");
            Width = 440;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            Background = Res("DarkerBgBrush", Brushes.Black);
            var owner = App.MainWindowRef;
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var muted = Res("TextMutedBrush", Brushes.Gainsboro);
            _line = new TextBlock { Text = Loc.Get("rain_assets_line"), TextWrapping = TextWrapping.Wrap, Foreground = muted, Margin = new Thickness(0, 0, 0, 14) };
            // Sunken well for the bar; a plain fill, no shimmer (Gentle Visuals asks for nothing here).
            _bar = new ProgressBar { Maximum = 100, Height = 10, BorderThickness = new Thickness(0), Background = Res("DepthWellFloorBrush", Brushes.Black), Foreground = Res("PinkBrush", Brushes.HotPink) };
            _percent = new TextBlock { Foreground = muted, Margin = new Thickness(0, 6, 0, 0) };
            var progress = Stack(Orientation.Vertical, new Border { BorderBrush = Res("DepthWellTop", Brushes.Black), BorderThickness = new Thickness(1, 1, 0, 0), CornerRadius = new CornerRadius(5), Child = _bar }, _percent);
            progress.Visibility = Visibility.Collapsed;
            progress.Margin = new Thickness(0, 0, 0, 14);
            _go = new Button { Content = Loc.Get("rain_assets_install"), MinWidth = 110, Padding = new Thickness(14, 6, 14, 6), Style = TryFindResource("PinkButton") as Style };
            _cancel = new Button { Content = Loc.Get("btn_cancel"), MinWidth = 90, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(10, 0, 0, 0), Visibility = Visibility.Collapsed, Style = TryFindResource("SecondaryButton") as Style };
            var row = Stack(Orientation.Horizontal, _go, _cancel);
            row.HorizontalAlignment = HorizontalAlignment.Right;
            var title = new TextBlock { Text = Loc.Get("rain_assets_title"), FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Res("TextLightBrush", Brushes.White), Margin = new Thickness(0, 0, 0, 8) };

            _go.Click += async (_, _) =>
            {
                _cts = new CancellationTokenSource();
                Swap(progress, true);
                _line.Text = Loc.Get("rain_assets_line");
                ShowPercent(0);
                var result = await _installer.InstallAsync(new Progress<double>(ShowPercent), _cts.Token);
                _cts.Dispose();
                _cts = null;
                if (!IsLoaded) return;
                if (result == RainInstallResult.Installed) { _installed = true; Close(); return; }
                App.Logger?.Information("RainAssets: install ended {Result}", result);
                Swap(progress, false);
                _go.Content = Loc.Get("rain_assets_retry");
                _line.Text = Loc.Get(ReasonKey(result));
            };
            _cancel.Click += (_, _) => _cts?.Cancel();

            // Floating card: one lamp top-left, the rim from the depth brushes.
            Content = new Border
            {
                Background = Res("PanelBgBrush", Brushes.Black), BorderBrush = Res("DepthFloatRim", Brushes.Gray), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(22, 18, 22, 18), Margin = new Thickness(14),
                Child = Stack(Orientation.Vertical, title, _line, progress, row),
            };
            if (_installer.State == RainAssetState.NotAvailable) _line.Text = Loc.Get("rain_assets_not_available");
        }

        private void Swap(UIElement progress, bool running)
        {
            progress.Visibility = _cancel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            _go.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        }

        private static StackPanel Stack(Orientation o, params UIElement[] children)
        {
            var s = new StackPanel { Orientation = o };
            foreach (var c in children) s.Children.Add(c);
            return s;
        }

        /// <summary>The short reason line for a failed install.</summary>
        internal static string ReasonKey(RainInstallResult result) => result switch
        {
            RainInstallResult.NotAvailable => "rain_assets_not_available",
            RainInstallResult.Cancelled => "rain_assets_cancelled",
            RainInstallResult.SizeMismatch or RainInstallResult.HashMismatch or RainInstallResult.BadArchive => "rain_assets_failed_check",
            RainInstallResult.Disk => "rain_assets_failed_disk",
            _ => "rain_assets_failed_network",
        };

        private void ShowPercent(double fraction)
        {
            var pct = Math.Clamp((int)Math.Round(fraction * 100), 0, 100);
            _bar.Value = pct;
            _percent.Text = string.Format(Loc.Get("rain_assets_progress"), pct);
        }

        private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

        protected override void OnClosed(EventArgs e)
        {
            try { _cts?.Cancel(); } catch (Exception ex) { Diag.Swallowed(ex); }
            if (ReferenceEquals(_open, this)) _open = null;
            base.OnClosed(e);
            if (_installed) _onInstalled();
        }
    }
}
