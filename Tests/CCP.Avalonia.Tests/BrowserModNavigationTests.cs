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

public sealed class BrowserModNavigationTests
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
    public Task ShellConsumesPopupsAndUsesTheLivePartnerPage() => Run((shell, page, host, mod) =>
    {
        Assert.True(host.OpenPopupsInPlace);
        host.OnNavigationCompleted(new Uri("https://bambicloud.com/"));
        var requests = host.NavigationRequests;
        Assert.True(host.OnNewWindowRequested(new Uri("https://exoclick.com/")));
        Assert.True(host.OnNewWindowRequested(null));
        Assert.Equal(requests, host.NavigationRequests);
        var local = new Uri("http://127.0.0.1:9/watch");
        Assert.True(host.OnNewWindowRequested(local));
        Assert.Equal(local, host.Source);
        Assert.Equal(requests + 1, host.NavigationRequests);
        host.OnNavigationCompleted(new Uri("https://www.hypnotube.com/watch"));
        Assert.True(host.OnNewWindowRequested(new Uri("http://127.0.0.1:9/ads/watch")));
        Assert.Equal(requests + 2, host.NavigationRequests);
        // Partner bypass cannot escape the host's navigation fence or sandbox.
        host.AllowNavigation = _ => false;
        host.OnNewWindowRequested(local);
        Assert.Equal(requests + 2, host.NavigationRequests);
        host.AllowNavigation = null;
        host.OnNewWindowRequested(new Uri("https://example.test/"));
        Assert.Equal(requests + 2, host.NavigationRequests);
        host.OpenPopupsInPlace = false;
        Assert.False(host.OnNewWindowRequested(local));
    });

    [Fact]
    public Task MissingEngineFallsBackForHttpsLinksButHonoursOfflineAndRejectsOtherSchemes() => Run((shell, page, host, mod) =>
    {
        Assert.False(host.HasEngine);
        var opened = new System.Collections.Generic.List<string>();
        shell.OpenBrowserFallback = url => { opened.Add(url); return true; };
        const string link = "https://example.test/watch";
        Assert.True(shell.NavigateToUrlInBrowser(link));
        Assert.Equal(new[] { link }, opened);
        Assert.True(shell.NavigateToUrlInBrowser("http://example.test/watch"));
        Assert.Single(opened);
        Assert.True(shell.NavigateToUrlInBrowser("file:///neutral.html"));
        Assert.Single(opened);
        CoreSettings.Current.OfflineMode = true;
        Assert.False(shell.NavigateToUrlInBrowser(link, userInitiated: false));
        Assert.Single(opened);
        Assert.False(shell.NavigateToUrlInBrowser(""));
    });

    private static void ModChanged()
    {
        CoreMods.RaiseModChanged(null, AvApp.Mods!.ActiveMod);
        Dispatcher.UIThread.RunJobs();
    }

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
    public Task ModSwitchHonoursTheModsSiteEvenWhenTheOverrideOffersBambiCloud() => Run((shell, page, host, mod) =>
    {
        CoreSettings.Current.ForceShowBambiCloud = true;
        page.SyncBrowserCard();
        Assert.True(page.FindControl<RadioButton>("RbBambiCloud")!.IsVisible);
        shell.NavigateToUrlInBrowser("https://bambicloud.com/list");
        host.OnNavigationCompleted(host.Source);
        ModChanged();
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Equal(mod.DefaultUrl, host.Source!.AbsoluteUri);
    });

    [Fact]
    public Task AModOfferingBambiCloudPreservesTheLiveSiteButSyncsAnExternalPage() => Run((shell, page, host, mod) =>
    {
        mod.ShowBambiCloudOption = true;
        mod.DefaultUrl = "https://bambicloud.com/mod-home";
        shell.NavigateToUrlInBrowser("https://hypnotube.com/video/example");
        host.OnNavigationCompleted(host.Source);
        var requests = host.NavigationRequests;
        ModChanged();
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Equal(requests, host.NavigationRequests);
        host.OnNavigationCompleted(new Uri("https://example.com/"));
        ModChanged();
        Assert.True(page.FindControl<RadioButton>("RbBambiCloud")!.IsChecked);
        Assert.Equal(requests, host.NavigationRequests);
    });

    [Fact]
    public Task OfflineModSwitchAndPromptNeverNavigate() => Run((shell, page, host, mod) =>
    {
        shell.NavigateToUrlInBrowser("https://bambicloud.com/list");
        CoreSettings.Current.OfflineMode = true;
        var requests = host.NavigationRequests;
        ModChanged();
        page.OnBrowserPromptClick();
        Assert.Equal(requests, host.NavigationRequests);
    });

    [Fact]
    public Task TurningOffTheOverrideMovesALoadedBambiPageHome() => Run((shell, page, host, mod) =>
    {
        var check = page.FindControl<CheckBox>("ChkForceShowBambiCloud")!;
        check.IsChecked = true;
        shell.NavigateToUrlInBrowser("https://bambicloud.com/list");
        host.OnNavigationCompleted(host.Source);
        check.IsChecked = false;
        Assert.False(page.FindControl<RadioButton>("RbBambiCloud")!.IsVisible);
        Assert.True(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Equal("https://hypnotube.com/", host.Source!.AbsoluteUri);
    });
}
