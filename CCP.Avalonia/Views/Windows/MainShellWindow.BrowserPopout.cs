// WPF MainWindow.Browser.cs:2620-2724. Keep the one WebHost and its native adapter alive
// through both moves: NativeWebView.BeginReparenting is available in Avalonia 12.1.
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private Window? _browserPopoutWindow;
        internal bool IsBrowserPoppedOut => _browserPopoutWindow != null;

        private void InitializeBrowserSurfaceLifecycle() => Closed += (_, _) => StopBrowserForPanic();

        internal void BtnPopOutBrowser_Click(object? sender, RoutedEventArgs e)
        {
            if (BrowserBlockedOffline()) return;
            if (_browserPopoutWindow is { } existing)
            {
                existing.Activate();
                return;
            }
            var page = BrowserPage;
            var host = BrowserView;
            var container = page?.FindControl<Grid>("BrowserContainer");
            if (page == null || host == null || container == null) return;

            // The scope delays native teardown until the host has a new visual parent.
            // Moving the UserControl without this scope would discard the playing document.
            using var reparent = host.BeginReparenting();
            var popout = new Window
            {
                Title = Loc.Get("title_browser_window"),
                Width = 1024, Height = 768, MinWidth = 400, MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
            };
            try
            {
                _browserPopoutWindow = popout;
                container.Children.Remove(host);
                popout.Content = host;
                popout.Closing += (_, _) =>
                {
                    using var returning = host.BeginReparenting();
                    popout.Content = null;
                    if (!container.Children.Contains(host)) container.Children.Add(host);
                    _browserPopoutWindow = null;
                    page.SyncBrowserPopout();
                    RevealDashboardBrowser("popout-closed");
                };
                page.SyncBrowserPopout();
                popout.Show(this);
                // WPF's first pop-out lazily initializes the browser and selected homepage.
                // Existing pages are moved without a reload, preserving media position.
                if (host.Source == null) NavigateBrowser(SiteHomeUrl());
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Browser pop-out failed");
                popout.Content = null;
                if (!container.Children.Contains(host)) container.Children.Add(host);
                _browserPopoutWindow = null;
                popout.Close();
                page.SyncBrowserPopout();
            }
        }

        /// <summary>A panic must end the document even if its scripts are hung or reject a pause.
        /// ponytail: unload to about:blank; native media pause/fullscreen signaling remains a follow-up.</summary>
        internal void StopBrowserForPanic()
        {
            if (BrowserView is { Source: not null } host) host.Navigate(new Uri("about:blank"));
            _browserPopoutWindow?.Close();
        }

        private void FocusBrowserSurface()
        {
            if (_browserPopoutWindow is { } popout) { popout.Activate(); return; }
            ShowTab("settings");
            RevealDashboardBrowser("focus-surface");
            Activate();
        }
    }
}
