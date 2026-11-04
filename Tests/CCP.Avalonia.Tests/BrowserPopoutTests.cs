using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using IconKind = FluentIcons.Common.Symbol;

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
            Assert.Equal(IconText.BareLoc("label_browser_popped_out_nclick_to_focus_window"), placeholder.Text);
            Assert.Equal(IconText.BareLoc("btn_focus"), label.Text);
            Assert.Equal(Loc.Get("tooltip_browser_is_popped_out_click_to_focus"), ToolTip.GetTip(button));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(popout, shell.BrowserPopoutWindow);
            popout.Close();
            Assert.Null(shell.BrowserPopoutWindow);
            Assert.Contains(browser, container.Children);
            Assert.False(placeholder.IsVisible);
            Assert.Equal(IconText.BareLoc("btn_pop_out"), label.Text);
            Assert.Equal(Loc.Get("tooltip_pop_out_browser_to_resizable_window"), ToolTip.GetTip(button));
        }
        finally { shell.BrowserPopoutWindow?.Close(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FirstPopoutLoadsActiveModDefaultEvenWhenBambiIsSelected() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
        var modsDir = Path.Combine(TestUserDataProfile.Root, "mods");
        var builtinsDir = Path.Combine(TestUserDataProfile.Root, "builtin_mods");
        bool hadMods = Directory.Exists(modsDir), hadBuiltins = Directory.Exists(builtinsDir);
        using var mods = new CoreModsSnapshot();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        settings.Current.ActiveModId = BuiltInMods.SissyHypnoId;
        settings.Current.ForceShowBambiCloud = true;
        MainShellWindow? shell = null;
        try
        {
            AvApp.StartMods();
            Assert.Equal("https://hypnotube.com/", AvApp.Mods!.GetDefaultBrowserUrl());
            shell = new MainShellWindow();
            shell.Show();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var bambi = page.FindControl<RadioButton>("RbBambiCloud")!;
            bambi.IsChecked = true;
            Assert.True(bambi.IsVisible);
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            Assert.Null(browser.Source);
            page.FindControl<Button>("BtnPopOutBrowser")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("https://hypnotube.com/", browser.Source!.ToString());
            Assert.False(bambi.IsChecked);
            Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
            shell.BtnReloadBrowser_Click(null, new RoutedEventArgs());
            Assert.Equal("https://hypnotube.com/", browser.Source!.ToString());
        }
        finally
        {
            shell?.BrowserPopoutWindow?.Close();
            shell?.Close();
            settings.SealForReset();
            CoreSettings.ServiceProvider = old;
            AvApp.ResetReleaseContent();
            if (!hadMods && Directory.Exists(modsDir)) Directory.Delete(modsDir, recursive: true);
            if (!hadBuiltins && Directory.Exists(builtinsDir)) Directory.Delete(builtinsDir, recursive: true);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0, "Pop Out", IconKind.Open)]
    [InlineData(1, "Focus", IconKind.Window)]
    [InlineData(2, "Pop Out", IconKind.Open)]
    public Task PopoutButtonDisplaysFluentIconAndPlainText(int state, string expectedText, IconKind expectedIcon) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var language = LocalizationManager.Instance.CurrentLanguage;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        try
        {
            LocalizationManager.Instance.SetLanguage("en");
            shell.Show();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            if (state > 0) shell.PopOutBrowser();
            if (state == 2) shell.BrowserPopoutWindow!.Close();
            var label = page.FindControl<TextBlock>("TxtPopOutBrowser")!;
            Assert.Equal(expectedText, label.Text);
            var glyph = Assert.IsType<IconGlyph>(page.FindControl<IconGlyph>("PopOutBrowserIcon"));
            Assert.True(glyph.IsVisible);
            Assert.False(glyph.IsFallback);
            Assert.Equal(expectedIcon, glyph.Kind);
        }
        finally
        {
            shell.BrowserPopoutWindow?.Close();
            shell.Close();
            CoreSettings.ServiceProvider = old;
            LocalizationManager.Instance.SetLanguage(language);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("label_browser_popped_out_nclick_to_focus_window")]
    [InlineData("btn_focus")]
    [InlineData("btn_pop_out")]
    [InlineData("label_browser_disabled_in_offline_mode")]
    [InlineData("label_click_to_load_browser")]
    public Task BrowserLabelsStayBareAcrossLanguages(string key) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.PopOutBrowser();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var target = page.FindControl<TextBlock>(key is "btn_focus" or "btn_pop_out" ? "TxtPopOutBrowser" : "BrowserLoadingText")!;
            if (key == "label_browser_disabled_in_offline_mode")
            {
                settings.Current.OfflineMode = true;
                shell.SyncOfflineModeState();
            }
            else if (key is "label_click_to_load_browser" or "btn_pop_out") shell.BrowserPopoutWindow!.Close();

            foreach (var (language, _, _) in LocalizationManager.AvailableLanguages)
            {
                LocalizationManager.Instance.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(IconText.BareLoc(key), target.Text);
            }
        }
        finally
        {
            shell.BrowserPopoutWindow?.Close();
            shell.Close();
            CoreSettings.ServiceProvider = old;
            LocalizationManager.Instance.SetLanguage(previousLanguage);
        }
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
    public Task PoppedOutPlaceholderFocusesWithoutReloading() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        settings.Current.DashboardBrowserCollapsed = false;
        settings.Current.MotionLevel = MotionLevel.Off;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("settings");
            shell.PopOutBrowser();
            var page = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var browser = page.FindControl<WebHost>("BrowserWebHost")!;
            var placeholder = page.FindControl<TextBlock>("BrowserLoadingText")!;
            var popout = shell.BrowserPopoutWindow!;
            var url = new Uri("https://hypnotube.com/video");
            browser.Navigate(url);
            var requests = browser.NavigationRequests;
            popout.WindowState = WindowState.Minimized;
            bool pressed = false;
            placeholder.PointerPressed += (_, _) => pressed = true;
            placeholder.RaiseEvent(new PointerPressedEventArgs(placeholder,
                new Pointer(1, PointerType.Mouse, true), shell, default, 0,
                new PointerPointProperties(), KeyModifiers.None, 1));
            Assert.True(pressed);
            Assert.Equal(requests, browser.NavigationRequests);
            Assert.Equal(url, browser.Source);
            Assert.Same(popout, shell.BrowserPopoutWindow);
            Assert.Equal(WindowState.Normal, popout.WindowState);
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
            Assert.Equal(IconText.BareLoc("label_browser_disabled_in_offline_mode"), placeholder.Text);
            popout.Close();
            Assert.True(placeholder.IsVisible);
            Assert.False(browser.IsVisible);
            Assert.Equal(IconText.BareLoc("label_browser_disabled_in_offline_mode"), placeholder.Text);
            settings.Current.OfflineMode = false;
            shell.SyncOfflineModeState();
            Assert.False(placeholder.IsVisible);
            Assert.True(browser.IsVisible);
            Assert.Equal(IconText.BareLoc("label_click_to_load_browser"), placeholder.Text);
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
