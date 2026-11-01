using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserPopoutTests
{
    private static void Setup()
    {
        AmbientBeatLoopsTests.Setup();
    }

    [Fact]
    public Task ButtonMovesTheSameBrowserAndCloseReturnsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var button = page.FindControl<Button>("BtnPopOutBrowser")!;
            var label = page.FindControl<TextBlock>("TxtPopOutBrowser")!;
            var placeholder = page.FindControl<TextBlock>("BrowserLoadingText")!;
            var container = page.FindControl<Grid>("BrowserContainer")!;
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            Assert.True(button.IsEnabled);
            Assert.Null(browser.Source);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var popout = Assert.IsType<Window>(shell.BrowserPopoutWindow);
            Assert.Same(browser, popout.Content);
            Assert.DoesNotContain(browser, container.Children);
            Assert.Equal("https://bambicloud.com/", browser.Source!.ToString());
            Assert.Equal(1024, popout.Width);
            Assert.Equal(768, popout.Height);
            Assert.True(placeholder.IsVisible);
            Assert.Equal(Loc.Get("label_browser_popped_out_nclick_to_focus_window"), placeholder.Text);
            Assert.Equal(Loc.Get("btn_focus"), label.Text);
            Assert.Equal(Loc.Get("tooltip_browser_is_popped_out_click_to_focus"), ToolTip.GetTip(button));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(popout, shell.BrowserPopoutWindow);
            popout.Close();
            Assert.Null(shell.BrowserPopoutWindow);
            Assert.Contains(browser, container.Children);
            Assert.False(placeholder.IsVisible);
            Assert.Equal(Loc.Get("btn_pop_out"), label.Text);
            Assert.Equal(Loc.Get("tooltip_pop_out_browser_to_resizable_window"), ToolTip.GetTip(button));
        }
        finally { shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NavigationFocusesTheExistingPopoutAndPreservesThePage() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            browser.Navigate(new Uri("https://hypnotube.com/"));
            int requests = browser.NavigationRequests;
            shell.PopOutBrowser();
            var popout = shell.BrowserPopoutWindow!;
            Assert.Equal(requests, browser.NavigationRequests);
            popout.WindowState = WindowState.Minimized;
            Assert.True(shell.NavigateToUrlInBrowser("https://hypnotube.com/video"));
            Assert.Same(popout, shell.BrowserPopoutWindow);
            Assert.Equal(WindowState.Normal, popout.WindowState);
            Assert.Equal("https://hypnotube.com/video", browser.Source!.ToString());
            Assert.True(page.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
        }
        finally { shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OfflineBlocksPopout() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            settings.Current.OfflineMode = true;
            shell.PopOutBrowser();
            Assert.Null(shell.BrowserPopoutWindow);
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            Assert.Null(browser.Source);
            settings.Current.OfflineMode = false;
            shell.PopOutBrowser();
            var popout = shell.BrowserPopoutWindow!;
            settings.Current.OfflineMode = true;
            shell.SyncOfflineModeState();
            var placeholder = page.FindControl<TextBlock>("BrowserLoadingText")!;
            Assert.Equal(Loc.Get("label_browser_disabled_in_offline_mode"), placeholder.Text);
            popout.Close();
            Assert.True(placeholder.IsVisible);
            Assert.False(browser.IsVisible);
            Assert.Equal(Loc.Get("label_browser_disabled_in_offline_mode"), placeholder.Text);
            settings.Current.OfflineMode = false;
            shell.SyncOfflineModeState();
            Assert.False(placeholder.IsVisible);
            Assert.True(browser.IsVisible);
        }
        finally { shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task ShellCloseClosesThePopout() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.PopOutBrowser();
            var popout = shell.BrowserPopoutWindow!;
            Assert.True(popout.IsVisible);
            shell.Close();
            Assert.False(popout.IsVisible);
            Assert.Null(shell.BrowserPopoutWindow);
        }
        finally { shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });
}
