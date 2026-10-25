using System;
using System.Collections.Generic;
using Serilog;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private static readonly List<Window> BrowserPopouts = new();
    internal static void CloseAllBrowserPopouts()
    {
        foreach (var window in BrowserPopouts.ToArray()) window.Close();
    }

    internal Window? BrowserPopout { get; private set; }
    private Views.Controls.WebHost? _poppedBrowser;

    // BrowserPage cannot find a host while it lives in the popout. Keep one shared instance.
    internal void PopOutBrowser()
    {
        if (BrowserBlockedOffline()) return;
        if (BrowserPopout is { } open) { open.Activate(); return; }
        var web = BrowserView;
        var slot = BrowserPage?.FindControl<Grid>("BrowserContainer");
        if (web == null || slot == null) return;
        if (web.Source == null) NavigateBrowser(SiteHomeUrl());
        var window = new Window
        {
            Title = Loc.Get("title_browser_window"), Width = 1024, Height = 768,
            MinWidth = 400, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E))
        };
        try
        {
            using (web.BeginReparenting())
            {
                slot.Children.Remove(web);
                window.Content = web;
                _poppedBrowser = web;
                BrowserPopout = window;
                BrowserPopouts.Add(window);
                window.Show();
            }
            window.Closing += (_, _) => ReturnBrowserToCard(window, slot, web);
            Closed += CloseBrowserPopoutOnShellClosed;
            BrowserPage?.SyncBrowserPrompt();
        }
        catch (Exception ex)
        {
            ReturnBrowserToCard(window, slot, web);
            window.Close();
            Log.Warning(ex, "Browser popout failed");
        }
    }

    private void ReturnBrowserToCard(Window window, Grid slot, Views.Controls.WebHost web)
    {
        using (web.BeginReparenting())
        {
            window.Content = null;
            if (!slot.Children.Contains(web)) slot.Children.Add(web);
        }
        BrowserPopouts.Remove(window);
        BrowserPopout = null;
        _poppedBrowser = null;
        Closed -= CloseBrowserPopoutOnShellClosed;
        BrowserPage?.SyncBrowserPrompt();
        RevealDashboardBrowser("popout-closed");
    }

    private void CloseBrowserPopoutOnShellClosed(object? sender, EventArgs e) => CloseBrowserPopout();
    internal void CloseBrowserPopout() => BrowserPopout?.Close();
}
