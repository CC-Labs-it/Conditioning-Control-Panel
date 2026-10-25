using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

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
    public Task UncheckingOverrideOnHidingModMovesToHypnoTubeOnlyWhenOnline(bool offline, int expectedNavigations) =>
        WithMod(BuiltInMods.SissyHypnoId, forceShow: true, offline, (shell, tab, host, mods) =>
    {
        Assert.True(tab.FindControl<RadioButton>("RbBambiCloud")!.IsVisible); // the override reveals it
        tab.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
        tab.FindControl<CheckBox>("ChkForceShowBambiCloud")!.IsChecked = false; // the user's click
        Assert.False(CoreSettings.Current.ForceShowBambiCloud);
        Assert.False(tab.FindControl<RadioButton>("RbBambiCloud")!.IsVisible);
        Assert.True(tab.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Equal(expectedNavigations, host.NavigationRequests);
        tab.SyncBrowserCard();
        Assert.Equal(expectedNavigations, host.NavigationRequests); // equal sync is not reload
    });

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public Task SwitchingToModWithoutBambiCloudSendsOpenPageToItsDefault(bool offline, int expectedNavigations) =>
        WithMod(BuiltInMods.BambiSleepId, forceShow: true, offline, (shell, tab, host, mods) =>
    {
        host.Navigate(new Uri("https://bambicloud.com/")); // a page is open
        tab.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
        mods.ActivateMod(BuiltInMods.SissyHypnoId);         // WPF MainWindow.xaml.cs:3254
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.FindControl<RadioButton>("RbHypnoTube")!.IsChecked);
        Assert.Equal(expectedNavigations, host.NavigationRequests);
        if (!offline) Assert.Equal(mods.GetDefaultBrowserUrl(), host.Source!.AbsoluteUri);
    });

    private static Task WithMod(string modId, bool forceShow, bool offline,
        Action<MainShellWindow, SettingsTabView, WebHost, ModService> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var (oldOffline, oldForce) = (s.OfflineMode, s.ForceShowBambiCloud);
        using var coreMods = new CoreModsSnapshot();
        var oldMods = AvApp.Mods;
        if (oldMods is null) AvApp.StartMods();
        var mods = AvApp.Mods!;
        var startId = mods.ActiveModId;
        mods.ActivateMod(modId);
        Dispatcher.UIThread.RunJobs();
        var shell = new MainShellWindow();
        try
        {
            s.OfflineMode = offline;
            s.ForceShowBambiCloud = forceShow;
            shell.Show(); // the card subscribes to ModChanged and reaches its shell through the visual tree
            Dispatcher.UIThread.RunJobs();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            body(shell, tab, tab.FindControl<WebHost>("BrowserWebHost")!, mods);
        }
        finally
        {
            shell.Close();
            if (mods.ActiveModId != startId) mods.ActivateMod(startId);
            Dispatcher.UIThread.RunJobs();
            typeof(AvApp).GetProperty(nameof(AvApp.Mods), BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, oldMods);
            (s.OfflineMode, s.ForceShowBambiCloud) = (oldOffline, oldForce);
        }
        return Task.CompletedTask;
    });
}
