using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserFullscreenTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task FullscreenReturnsBrowserAndStopRejectsLatePageEntry(bool popout) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
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
            var container = page.FindControl<Grid>("BrowserContainer")!;
            if (popout) shell.PopOutBrowser();
            browser.OnWebMessage("ccp_fullscreen_on");
            var host = Assert.IsType<Window>(shell.BrowserFullscreenWindow);
            Assert.Same(browser, host.Content);
            browser.OnWebMessage("ccp_exit_fullscreen");
            Assert.Null(shell.BrowserFullscreenWindow);
            if (popout) Assert.Same(browser, shell.BrowserPopoutWindow!.Content);
            else Assert.Contains(browser, container.Children);
            browser.OnWebMessage("ccp_fullscreen_on");
            shell.StopBrowserFullscreen();
            Assert.Null(shell.BrowserFullscreenWindow);
            browser.OnWebMessage("ccp_fullscreen_on");
            Assert.Null(shell.BrowserFullscreenWindow);
            browser.OnNavigationCompleted(new Uri("https://example.com/"));
            browser.OnWebMessage("ccp_fullscreen_on");
            Assert.NotNull(shell.BrowserFullscreenWindow);
            browser.OnWebMessage("ccp_key:F11:0:0");
            Assert.Null(shell.BrowserFullscreenWindow);
        }
        finally { shell.ExitBrowserFullscreen(); shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.F11)]
    public Task WindowKeysAndCloseReturnTheBrowser(Key key) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            var container = page.FindControl<Grid>("BrowserContainer")!;
            browser.OnWebMessage("ccp_fullscreen_on");
            var host = Assert.IsType<Window>(shell.BrowserFullscreenWindow);
            host.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            Assert.Null(shell.BrowserFullscreenWindow);
            Assert.Contains(browser, container.Children);
            browser.OnWebMessage("ccp_fullscreen_on");
            host = Assert.IsType<Window>(shell.BrowserFullscreenWindow);
            host.Close();
            Assert.Null(shell.BrowserFullscreenWindow);
            Assert.Contains(browser, container.Children);
        }
        finally { shell.ExitBrowserFullscreen(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

}
