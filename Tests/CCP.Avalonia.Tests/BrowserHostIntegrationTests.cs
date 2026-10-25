using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserHostIntegrationTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task UnavailableEngineOpensOnlyHttpsAndHonoursOfflineGate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldOpen = ExternalOpener.Shell;
        var oldOffline = CoreSettings.Current.OfflineMode;
        var opened = new List<string>();
        ExternalOpener.Shell = url => { opened.Add(url); return true; };
        var shell = new MainShellWindow();
        try
        {
            CoreSettings.Current.OfflineMode = false;
            Assert.False(WebHost.IsAvailable); // headless: no native desktop or browser
            Assert.True(shell.NavigateToUrlInBrowser("https://127.0.0.1/watch"));
            Assert.Equal(new[] { "https://127.0.0.1/watch" }, opened);
            shell.NavigateToUrlInBrowser("http://127.0.0.1/watch");
            shell.NavigateToUrlInBrowser("javascript:alert(1)");
            CoreSettings.Current.OfflineMode = true;
            Assert.False(shell.NavigateToUrlInBrowser("https://127.0.0.1/offline", userInitiated: false));
            Assert.Single(opened);
        }
        finally
        {
            shell.Close();
            ExternalOpener.Shell = oldOpen;
            CoreSettings.Current.OfflineMode = oldOffline;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LinkRestoresHiddenAndMinimizedShell() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var offline = CoreSettings.Current.OfflineMode;
        var shell = new MainShellWindow();
        try
        {
            CoreSettings.Current.OfflineMode = false;
            shell.Show();
            shell.Hide();
            shell.NavigateToUrlInBrowser("http://127.0.0.1/watch");
            Assert.True(shell.IsVisible);
            shell.WindowState = WindowState.Minimized;
            shell.NavigateToUrlInBrowser("http://127.0.0.1/next");
            Assert.Equal(WindowState.Normal, shell.WindowState);
        }
        finally { shell.Close(); CoreSettings.Current.OfflineMode = offline; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true, "https://hypnotube.com/", null, false)]
    [InlineData(true, "https://bambicloud.com/", null, true)]
    [InlineData(false, "https://bambicloud.com/", null, false)]
    [InlineData(true, "https://bambicloud.com/", "https://hypnotube.com/watch", false)]
    [InlineData(false, "https://bambicloud.com/", "https://bambicloud.com/watch", false)]
    public Task SiteSelectionFollowsModAndPreservesKnownPage(bool offered, string home, string? live, bool bambi) =>
        AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var tab = new SettingsTabView();
        tab.FindControl<RadioButton>("RbHypnoTube")!.IsChecked = true;
        var host = tab.FindControl<WebHost>("BrowserWebHost")!;
        if (live != null) host.OnNavigationCompleted(new Uri(live));
        tab.SyncBrowserSite(offered, home, navigateIfChanged: false);
        Assert.Equal(bambi, tab.FindControl<RadioButton>("RbBambiCloud")!.IsChecked);
        Assert.Null(host.Source); // selection alone never navigates
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public Task HidingSelectedBambiNavigatesOnlyWhenOnline(bool offline, int expectedNavigations) =>
        AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreSettings.Current.OfflineMode;
        var shell = new MainShellWindow();
        try
        {
            CoreSettings.Current.OfflineMode = offline;
            shell.Show(); // the card forwards to its shell through the attached visual tree
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var host = tab.FindControl<WebHost>("BrowserWebHost")!;
            tab.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
            tab.SyncBrowserSite(false, "https://hypnotube.com/", navigateIfChanged: true);
            Assert.True(tab.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
            Assert.Equal(expectedNavigations, host.NavigationRequests);
            tab.SyncBrowserSite(false, "https://hypnotube.com/", navigateIfChanged: true);
            Assert.Equal(expectedNavigations, host.NavigationRequests); // equal sync is not reload
        }
        finally { shell.Close(); CoreSettings.Current.OfflineMode = old; }
        return Task.CompletedTask;
    });
}
