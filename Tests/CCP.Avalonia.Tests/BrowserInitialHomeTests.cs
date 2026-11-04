using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
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
                var host = page.FindControl<WebHost>("BrowserWebHost")!;
                host.DocumentUrlReader = () => Task.FromResult(host.CurrentUrl?.AbsoluteUri);
                check(shell, page, host, manifest.Browser);
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

    [Fact]
    public Task SiteSyncUsesModDefaultBeforeLoadAndDoesNotNavigate() => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        page.SyncBrowserCard();
        Assert.True(page.FindControl<RadioButton>("RbBambiCloud")!.IsVisible);
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Null(host.Source);
        Assert.Equal(0, host.NavigationRequests);
        mod.DefaultUrl = "https://bambicloud.com/mod-home";
        page.SyncBrowserCard();
        Assert.True(page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked);
        Assert.Equal(0, host.NavigationRequests);
    });

    [Fact]
    public Task SiteSyncPreservesKnownLivePageButReselectsAfterExternalPage() => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        Assert.True(shell.NavigateToUrlInBrowser("https://bambicloud.com/video"));
        host.OnNavigationCompleted(new Uri("https://bambicloud.com/video"));
        int requests = host.NavigationRequests;
        page.SyncBrowserCard();
        Assert.True(page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked);
        Assert.Equal(requests, host.NavigationRequests);

        Assert.True(shell.NavigateToUrlInBrowser("https://example.com/"));
        host.OnNavigationCompleted(new Uri("https://example.com/"));
        requests = host.NavigationRequests;
        page.SyncBrowserCard();
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Equal(requests, host.NavigationRequests);
    });

    [Theory]
    [InlineData("https://example.com/old", "https://bambicloud.com/loading", true)]
    [InlineData("https://bambicloud.com/old", "https://example.com/loading", false)]
    public Task SiteSyncReadsNativeDocumentBeforeNavigationCompletes(string completed, string native, bool bambi) => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        host.OnNavigationCompleted(new Uri(completed));
        Assert.True(shell.NavigateToUrlInBrowser(native));
        host.DocumentUrlReader = () => Task.FromResult<string?>(native);
        page.SyncBrowserCard();
        Assert.Equal(bambi, page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked == true);
        Assert.Equal(!bambi, page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked == true);
        shell.BtnReloadBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        Assert.Equal(bambi ? "https://bambicloud.com/" : "https://hypnotube.com/", host.Source!.AbsoluteUri);
    });

    [Fact]
    public Task FirstLoadKeepsTheModSiteWhenTheNativeReadIsDelayed() => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
        var pending = new TaskCompletionSource<string?>();
        int reads = 0;
        host.DocumentUrlReader = () => { reads++; return pending.Task; };
        page.SyncBrowserCard();
        Assert.Null(host.Source);
        Assert.False(pending.Task.IsCompleted);

        page.OnBrowserPromptClick();
        Assert.Equal(mod.DefaultUrl, host.Source!.AbsoluteUri);
        pending.SetResult("about:blank");
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Equal(0, reads); // an unopened browser has no live page to read
        shell.BtnReloadBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        Assert.Equal("https://hypnotube.com/", host.Source!.AbsoluteUri);
    });

    [Fact]
    public Task ReloadWaitsForPendingSiteSelection() => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        host.Navigate(new Uri("https://example.com/old"));
        page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
        var pending = new TaskCompletionSource<string?>();
        host.DocumentUrlReader = () => pending.Task;
        page.SyncBrowserCard();
        int requests = host.NavigationRequests;
        shell.BtnReloadBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        Assert.Equal(requests, host.NavigationRequests);
        pending.SetResult("https://example.com/old");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("https://hypnotube.com/", host.Source!.AbsoluteUri);
    });

    [Fact]
    public Task InPageNavigationReconcilesPendingSiteSelection() => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        host.Navigate(new Uri("https://example.com/old"));
        page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
        var pending = new TaskCompletionSource<string?>();
        host.DocumentUrlReader = () => pending.Task;
        page.SyncBrowserCard();
        host.NewScrolllerEngine()(true);
        var navigation = new WebViewNavigationStartingEventArgs { Request = new Uri("http://127.0.0.1/new") };
        host.OnNavigationStarted(null, navigation);
        Assert.False(navigation.Cancel);
        host.OnNavigationCompleted(navigation.Request);
        pending.SetResult("https://example.com/old");
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingSyncUsesPageBeforeInPageNavigation(bool knownBefore) => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        var before = knownBefore ? "https://bambicloud.com/video" : "https://example.com/old";
        host.OnNavigationCompleted(new Uri("https://example.com/previous"));
        Assert.True(shell.NavigateToUrlInBrowser(before)); // the known page may still be loading
        var pending = new TaskCompletionSource<string?>();
        host.DocumentUrlReader = () => pending.Task;
        page.SyncBrowserCard();
        host.NewScrolllerEngine()(true);
        var destination = knownBefore ? "http://127.0.0.1/external" : "http://127.0.0.1/bambicloud.com";
        var navigation = new WebViewNavigationStartingEventArgs { Request = new Uri(destination) };
        host.OnNavigationStarted(null, navigation);
        Assert.False(navigation.Cancel);
        pending.SetResult(destination); // a delayed GTK read can observe the new navigation
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(knownBefore, page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked == true);
        Assert.Equal(!knownBefore, page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked == true);
        shell.BtnReloadBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        Assert.Equal(knownBefore ? "https://bambicloud.com/" : "https://hypnotube.com/", host.Source!.AbsoluteUri);
    });

    [Fact]
    public Task EqualUrlRequestInvalidatesAPendingNativeRead() => Run((shell, page, host, mod) =>
    {
        const string url = "https://bambicloud.com/video";
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        Assert.True(shell.NavigateToUrlInBrowser(url));
        var pending = new TaskCompletionSource<string?>();
        host.DocumentUrlReader = () => pending.Task;
        page.SyncBrowserCard();
        int requests = host.NavigationRequests;

        Assert.True(shell.NavigateToUrlInBrowser(url));
        Assert.Equal(requests + 1, host.NavigationRequests);
        pending.SetResult("https://example.com/old");
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked);
        shell.BtnReloadBrowser_Click(page, new global::Avalonia.Interactivity.RoutedEventArgs());
        Assert.Equal("https://bambicloud.com/", host.Source!.AbsoluteUri);
    });

    [Fact]
    public Task DeferredNativeReadDoesNotOverrideANewerNavigation() => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://hypnotube.com/mod-home";
        host.Navigate(new Uri("https://example.com/old")); // an open browser makes the native read asynchronous
        var pending = new TaskCompletionSource<string?>();
        int reads = 0;
        host.DocumentUrlReader = () => { reads++; return pending.Task; };
        page.SyncBrowserCard();
        Assert.Equal(1, reads);
        Assert.True(shell.NavigateToUrlInBrowser("https://bambicloud.com/user"));
        int requests = host.NavigationRequests;
        pending.SetResult("https://example.com/old");
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked);
        Assert.Equal(requests, host.NavigationRequests);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ModSwitchMovesOnlyAnOpenOnlineBrowserToExactDefault(bool loaded) => Run((shell, page, host, mod) =>
    {
        var target = AvApp.Mods!.InstalledMods[BuiltInMods.DronificationId].Manifest;
        var previous = target.Browser;
        target.Browser = new ModBrowser { DefaultUrl = "https://hypnotube.com/new-mod", ShowBambiCloudOption = false };
        try
        {
            CoreSettings.Current.ForceShowBambiCloud = true;
            page.SyncBrowserCard();
            if (loaded) host.Navigate(new Uri("https://bambicloud.com/video"));
            page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
            int requests = host.NavigationRequests;
            shell.SwitchActiveModFromLauncher(BuiltInMods.DronificationId);
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.FindControl<RadioButton>("RbBambiCloud")!.IsVisible);
            Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
            Assert.Equal(requests + (loaded ? 1 : 0), host.NavigationRequests);
            Assert.Equal(loaded ? "https://hypnotube.com/new-mod" : null, host.Source?.AbsoluteUri);
        }
        finally { target.Browser = previous; }
    });

    [Fact]
    public Task OfflineModSwitchKeepsOpenBrowserParked() => Run((shell, page, host, mod) =>
    {
        var target = AvApp.Mods!.InstalledMods[BuiltInMods.DronificationId].Manifest;
        var previous = target.Browser;
        target.Browser = new ModBrowser { DefaultUrl = "https://hypnotube.com/new-mod", ShowBambiCloudOption = false };
        try
        {
            host.Navigate(new Uri("about:blank"));
            CoreSettings.Current.OfflineMode = true;
            int requests = host.NavigationRequests;
            shell.SwitchActiveModFromLauncher(BuiltInMods.DronificationId);
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
            Assert.Equal(requests, host.NavigationRequests);
            Assert.Equal("about:blank", host.Source!.AbsoluteUri);
        }
        finally { target.Browser = previous; }
    });

    [Fact]
    public Task OverrideSyncDoesNotLoadAnUnopenedBrowser() => Run((shell, page, host, mod) =>
    {
        var check = page.FindControl<CheckBox>("ChkForceShowBambiCloud")!;
        check.IsChecked = true;
        page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
        check.IsChecked = false;
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Null(host.Source);
        Assert.Equal(0, host.NavigationRequests);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OverrideSyncMovesSelectionAndNavigatesOnlyOnline(bool offline) => Run((shell, page, host, mod) =>
    {
        var check = page.FindControl<CheckBox>("ChkForceShowBambiCloud")!;
        check.IsChecked = true;
        page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
        host.Navigate(new Uri("https://bambicloud.com/video"));
        CoreSettings.Current.OfflineMode = offline;
        int requests = host.NavigationRequests;
        check.IsChecked = false;
        Assert.False(page.FindControl<RadioButton>("RbBambiCloud")!.IsVisible);
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Equal(requests + (offline ? 0 : 1), host.NavigationRequests);
        Assert.Equal(offline ? "https://bambicloud.com/video" : "https://hypnotube.com/", host.Source!.AbsoluteUri);
    });

}
