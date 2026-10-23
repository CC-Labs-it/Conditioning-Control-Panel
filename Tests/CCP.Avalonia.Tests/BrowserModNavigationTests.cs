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
