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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task CrashShowsPromptAndNavigationRestoresHost(int action) => Run((shell, page, host, mod) =>
    {
        host.NativeViewFactory = () => new NativeWebView();
        Assert.True(host.RecreateNativeView());
        var failedView = host.NativeView;
        page.OnBrowserPromptClick();
        host.OnBrowserProcessFailed();
        var prompt = page.FindControl<TextBlock>("BrowserLoadingText")!;
        var status = page.FindControl<TextBlock>("TxtBrowserStatus")!;
        Assert.True(prompt.IsVisible);
        Assert.Equal(IconText.BareLoc("label_browser_crashed_restart"), prompt.Text);
        Assert.Equal(IconText.BareLoc("label_disconnected"), IconText.Bare(status.Text));
        Assert.Equal(global::Avalonia.Media.Color.FromRgb(255, 107, 107), ((global::Avalonia.Media.SolidColorBrush)status.Foreground!).Color);
        Assert.False(host.IsVisible);
        Dispatcher.UIThread.RunJobs();
        using var frame = shell.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.DoesNotContain("label_", prompt.Text!);
        var requests = host.NavigationRequests;
        if (action == 0) shell.BrowserSiteToggle_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        else if (action == 1) shell.BtnReloadBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        else shell.NavigateToUrlInBrowser("https://hypnotube.com/test");
        Assert.Equal(requests + 1, host.NavigationRequests);
        Assert.NotNull(host.NativeView);
        Assert.NotSame(failedView, host.NativeView);
        Assert.True(host.IsVisible);
        Assert.False(prompt.IsVisible);
    });

    [Fact]
    public Task FailedReplacementKeepsCrashPromptAndCanRetry() => Run((shell, page, host, mod) =>
    {
        host.NativeViewFactory = () => new NativeWebView();
        Assert.True(host.RecreateNativeView());
        page.OnBrowserPromptClick();
        host.OnBrowserProcessFailed();
        var requests = host.NavigationRequests;
        host.NativeViewFactory = () => throw new InvalidOperationException("test creation failure");
        shell.BtnReloadBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        var prompt = page.FindControl<TextBlock>("BrowserLoadingText")!;
        Assert.Null(host.NativeView);
        Assert.Equal(requests, host.NavigationRequests);
        Assert.True(prompt.IsVisible);
        Assert.Equal(IconText.BareLoc("label_browser_crashed_restart"), prompt.Text);
        Assert.False(host.IsVisible);
        host.NativeViewFactory = () => new NativeWebView();
        shell.BtnReloadBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        Assert.NotNull(host.NativeView);
        Assert.Equal(requests + 1, host.NavigationRequests);
        Assert.True(host.IsVisible);
        host.OnBrowserProcessFailed();
        Assert.True(prompt.IsVisible); // the card remains subscribed after replacement
    });

    [Fact]
    public Task RetryLoadsOnReplacementAdapter() => Run((shell, page, host, mod) =>
    {
        host.NativeViewFactory = () => new NativeWebView();
        var failures = new System.Collections.Generic.List<Action>();
        var disposed = 0;
        host.FailureWatchFactory = (_, failed) => { failures.Add(failed); return new Watch(() => disposed++); };
        Assert.True(host.RecreateNativeView());
        var oldView = host.NativeView!;
        var oldAdapter = new WebHostPanicAdapterTests.Adapter();
        WebHostPanicAdapterTests.BindControl(oldView, () => oldAdapter.Proxy);
        WebHostPanicAdapterTests.NativeEvent(oldView, "ControlHostImplOnAdapterCreated", oldAdapter.Proxy);
        host.NewScrolllerEngine()(true);
        var initial = new Uri("http://127.0.0.1:5555/initial");
        host.Navigate(initial);
        Assert.Equal(initial, oldAdapter.Current);
        Assert.Single(failures);
        failures[0]();

        var retry = new Uri("http://127.0.0.1:5555/retry");
        Assert.True(shell.NavigateToUrlInBrowser(retry.AbsoluteUri));
        var newView = host.NativeView!;
        Assert.NotSame(oldView, newView);
        var newAdapter = new WebHostPanicAdapterTests.Adapter();
        WebHostPanicAdapterTests.BindControl(newView, () => newAdapter.Proxy);
        WebHostPanicAdapterTests.NativeEvent(newView, "ControlHostImplOnAdapterCreated", newAdapter.Proxy);
        host.NewScrolllerEngine()(true);
        Assert.Equal(initial, oldAdapter.Current);
        Assert.Equal(retry, newAdapter.Current);
        Assert.Equal(1, disposed);
        Assert.Equal(2, failures.Count);
        failures[0](); // a stale callback cannot fail the replacement
        Assert.True(host.IsVisible);
        failures[1]();
        Assert.False(host.IsVisible);
    });

    private sealed class Watch(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

}
