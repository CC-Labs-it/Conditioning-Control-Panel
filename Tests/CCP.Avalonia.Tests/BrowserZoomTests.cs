using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserZoomTests
{
    private static Task WithBrowser(Action<MainShellWindow, WebHost, List<double>> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        settings.Current.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var browser = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var calls = new List<double>();
            browser.ZoomInvoker = calls.Add;
            check(shell, browser, calls);
        }
        finally { shell.ExitBrowserFullscreen(); shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact] public Task SiteClickAppliesHalf() => WithBrowser((shell, browser, calls) =>
    {
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        Assert.Equal(0.5, Assert.Single(calls));
    });

    [Fact] public Task LinkAppliesHalf() => WithBrowser((shell, browser, calls) =>
    {
        Assert.True(shell.NavigateToUrlInBrowser("https://example.com/"));
        Assert.Equal(0.5, Assert.Single(calls));
    });

    [Theory] [InlineData(false)] [InlineData(true)]
    public Task FullscreenRestoresZoom(bool popout) => WithBrowser((shell, browser, calls) =>
    {
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        if (popout) shell.PopOutBrowser();
        calls.Clear();
        browser.OnWebMessage("ccp_fullscreen_on");
        Assert.Equal(1.0, calls[^1]);
        browser.OnWebMessage("ccp_fullscreen_off");
        Assert.Equal(0.5, calls[^1]);
    });

    [Fact] public Task NavigationDuringFullscreenKeepsFullscreenAndRestoresPrevious() => WithBrowser((shell, browser, calls) =>
    {
        browser.SetZoom(0.8);
        browser.OnWebMessage("ccp_fullscreen_on");
        Assert.Equal(new[] { 0.8, 1.0 }, calls);
        shell.NavigateToUrlInBrowser("https://example.com/");
        Assert.Equal(1.0, browser.Zoom);
        browser.OnWebMessage("ccp_fullscreen_off");
        Assert.Equal(0.8, calls[^1]);
    });

    [Fact] public Task RebuiltEngineReappliesZoom() => WithBrowser((shell, browser, calls) =>
    {
        shell.BrowserSiteToggle_Click(null, new RoutedEventArgs());
        calls.Clear();
        browser.ZoomEngineCreated(null);
        Assert.Equal(0.5, Assert.Single(calls));
    });
}
