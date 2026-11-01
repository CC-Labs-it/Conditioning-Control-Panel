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
