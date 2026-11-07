using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class BrowserInitialHomeTests
{
    private static Task Run(Action<MainShellWindow, SettingsTabView, WebHost, ModBrowser> check) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia().WithInterFont()
                    .With(AppFonts.Options(systemEmoji: false))
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            using var snapshot = new CoreModsSnapshot();
            var oldSettings = CoreSettings.ServiceProvider;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.OfflineMode = false;
            settings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
            AvApp.StartMods();
            var manifest = AvApp.Mods!.ActiveMod.Manifest;
            var oldBrowser = manifest.Browser;
            manifest.Browser = new ModBrowser { DefaultUrl = "https://hypnotube.com/mod-home", ShowBambiCloudOption = false };
            MainShellWindow? shell = null;
            try
            {
                shell = new MainShellWindow();
                shell.Show();
                shell.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                var page = shell.Named<SettingsTabView>("SettingsTab")!;
                check(shell, page, page.FindControl<WebHost>("BrowserWebHost")!, manifest.Browser);
            }
            finally
            {
                shell?.Close();
                Dispatcher.UIThread.RunJobs();
                manifest.Browser = oldBrowser;
                settings.SaveImmediate();
                settings.SealForReset();
                CoreSettings.ServiceProvider = oldSettings;
                AvApp.ResetReleaseContent();
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task FirstPromptUsesTheModsExactDefaultWhileReloadUsesTheSelectedSiteHome() => Run((shell, page, host, mod) =>
    {
        Assert.Null(host.Source); // synchronization alone never loads the browser
        page.OnBrowserPromptClick();
        Assert.Equal(mod.DefaultUrl, host.Source!.AbsoluteUri);
        var requests = host.NavigationRequests;
        page.OnBrowserPromptClick();
        Assert.Equal("https://hypnotube.com/", host.Source!.AbsoluteUri);
        Assert.Equal(requests + 1, host.NavigationRequests);
    });

    [Fact]
    public Task FirstPopoutUsesTheModsExactDefaultWithoutASecondNavigation() => Run((shell, page, host, mod) =>
    {
        shell.BtnPopOutBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        Assert.Equal(mod.DefaultUrl, host.Source!.AbsoluteUri);
        Assert.Equal(1, host.NavigationRequests);
    });

    [Fact]
    public Task OfflineFirstPromptKeepsTheHostUnloaded() => Run((shell, page, host, mod) =>
    {
        CoreSettings.Current.OfflineMode = true;
        page.OnBrowserPromptClick();
        Assert.Null(host.Source);
        Assert.Equal(0, host.NavigationRequests);
    });

    [Theory]
    [InlineData("label_click_to_load_browser")]
    [InlineData("label_browser_disabled_in_offline_mode")]
    [InlineData("label_browser_popped_out_nclick_to_focus_window")]
    public Task PromptUsesBareCopyAndKeepsItsLanguageBinding(string key) => Run((shell, page, host, mod) =>
    {
        if (key == "label_browser_disabled_in_offline_mode") CoreSettings.Current.OfflineMode = true;
        if (key == "label_browser_popped_out_nclick_to_focus_window")
            shell.BtnPopOutBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        page.SyncBrowserPrompt();
        var prompt = page.FindControl<TextBlock>("BrowserLoadingText")!;
        var loc = LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("en");
            Assert.Equal(IconText.BareLoc(key), prompt.Text);
            loc.SetLanguage("de");
            Assert.Equal(IconText.BareLoc(key), prompt.Text);
        }
        finally { loc.SetLanguage(language); }
    });

    [Fact]
    public Task OfflineRoundTripBeforeFirstLoadRestoresTheLoadPrompt() => Run((shell, page, host, mod) =>
    {
        host.ScriptInvoker = _ => Task.FromResult<string?>(null); // an available, not-yet-loaded engine
        page.SyncBrowserPrompt();
        var prompt = page.FindControl<TextBlock>("BrowserLoadingText")!;
        Assert.True(prompt.IsVisible);
        CoreSettings.Current.OfflineMode = true;
        shell.SyncOfflineModeState();
        CoreSettings.Current.OfflineMode = false;
        shell.SyncOfflineModeState();
        Assert.Null(host.Source);
        Assert.True(prompt.IsVisible);
        Assert.False(host.IsVisible);
        Assert.Equal(IconText.BareLoc("label_click_to_load_browser"), prompt.Text);
        page.OnBrowserPromptClick();
        Assert.Equal(mod.DefaultUrl, host.Source!.AbsoluteUri);
    });

}
