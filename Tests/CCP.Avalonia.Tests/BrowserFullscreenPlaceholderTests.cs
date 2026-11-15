using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserFullscreenPlaceholderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EmbeddedFullscreenShowsPlaceholderAndRestoresIt(bool closeHost) => AvaloniaTestDispatcher.RunAsync(() =>
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
            var label = page.FindControl<TextBlock>("BrowserLoadingText")!;
            browser.Source = new Uri("https://example.com/");
            Assert.False(label.IsVisible);
            browser.OnWebMessage("ccp_fullscreen_on");
            Assert.True(label.IsVisible);
            Assert.Equal(IconText.BareLoc("label_browser_in_fullscreen"), label.Text);
            page.OnBrowserPromptClick();
            Assert.Equal(new Uri("https://example.com/"), browser.Source);
            if (closeHost) shell.BrowserFullscreenWindow!.Close();
            else browser.OnWebMessage("ccp_fullscreen_off");
            Assert.False(label.IsVisible);
        }
        finally { shell.ExitBrowserFullscreen(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PopoutFullscreenKeepsPopoutPlaceholder() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            var label = page.FindControl<TextBlock>("BrowserLoadingText")!;
            shell.PopOutBrowser();
            browser.OnWebMessage("ccp_fullscreen_on");
            Assert.True(label.IsVisible);
            Assert.Equal(IconText.BareLoc("label_browser_popped_out_nclick_to_focus_window"), label.Text);
            browser.OnWebMessage("ccp_fullscreen_off");
            Assert.True(label.IsVisible);
            Assert.Equal(IconText.BareLoc("label_browser_popped_out_nclick_to_focus_window"), label.Text);
        }
        finally { shell.ExitBrowserFullscreen(); shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });
}
