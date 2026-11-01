using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private Window? _browserPopoutWindow;
    internal Window? BrowserPopoutWindow => _browserPopoutWindow;

    // WPF MainWindow.Browser.cs:2704-2811: initialize on first click, move the same browser,
    // focus an existing window, and return the browser when that window closes.
    internal void PopOutBrowser()
    {
        if (BrowserBlockedOffline()) return;
        if (_browserPopoutWindow != null) { FocusBrowserPopout(); return; }
        var page = BrowserPage;
        var browser = BrowserView;
        var container = page?.FindControl<Grid>("BrowserContainer");
        if (page == null || browser == null || container == null) return;

        // WPF InitializeBrowserAsync starts the default site if the browser has not loaded yet.
        if (browser.Source == null) NavigateBrowser(SiteHomeUrl());
        RevealDashboardBrowser("browser-init");
        Window? window = null;
        try
        {
            window = new Window
            {
                Title = Loc.Get("title_browser_window"), Width = 1024, Height = 768,
                MinWidth = 400, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x2e))
            };
            _browserPopoutWindow = window;
            window.Closing += (_, _) => ReturnBrowserFromPopout(window, browser, container);
            window.Closed += (_, _) =>
            {
                _browserPopoutWindow = null;
                page.SetBrowserPoppedOut(false);
                RevealDashboardBrowser("popout-closed");
            };
            using (browser.BeginReparenting())
            {
                container.Children.Remove(browser);
                window.Content = browser;
                window.Show();
            }
            page.SetBrowserPoppedOut(true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Browser pop-out failed");
            if (window != null)
            {
                ReturnBrowserFromPopout(window, browser, container);
                try { window.Close(); } catch (Exception closeError) { Log.Debug(closeError, "Browser pop-out close failed"); }
            }
            _browserPopoutWindow = null;
            page.SetBrowserPoppedOut(false);
        }
    }

    private static void ReturnBrowserFromPopout(Window window, Controls.WebHost browser, Grid container)
    {
        using (browser.BeginReparenting())
        {
            if (window.Content == browser) window.Content = null;
            if (browser.Parent == null && !container.Children.Contains(browser)) container.Children.Insert(0, browser);
        }
    }

    private void FocusBrowserPopout()
    {
        if (_browserPopoutWindow is not { } window) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        window.Focus();
    }

    // WPF MainWindow.Browser.cs:326-359: navigation focuses the surface carrying the browser.
    private void FocusBrowserSurface()
    {
        if (_browserPopoutWindow != null) { FocusBrowserPopout(); return; }
        ShowTab("settings");
        RevealDashboardBrowser("focus-surface");
        Activate();
        Focus();
    }
}
