using System;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Services;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionWatchRecutTests
{
    // Held companion watch requests on panic are covered by trunk CompanionLinkLauncherTests (NavigateWhenReadyAsync).
    [Fact]
    public Task GateReportCannotLoadBrowserAfterOfflineModeStarts() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var offline = CoreSettings.Current.OfflineMode;
        var shell = new MainShellWindow();
        var host = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
        try
        {
            Assert.True(host.BlockWhenOffline);
            CoreSettings.Current.OfflineMode = false;
            var ready = host.NewScrolllerEngine();
            host.Navigate(new Uri("https://127.0.0.1/watch"));
            CoreSettings.Current.OfflineMode = true;
            ready(true);
            Assert.Null(host.Source);
        }
        finally { shell.Close(); CoreSettings.Current.OfflineMode = offline; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EnteringOfflineReplacesAHeldRequestWithBlank() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var offline = CoreSettings.Current.OfflineMode;
        var shell = new MainShellWindow();
        try
        {
            CoreSettings.Current.OfflineMode = false;
            var host = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var ready = host.NewScrolllerEngine();
            host.Navigate(new Uri("https://127.0.0.1/watch"));
            CoreSettings.Current.OfflineMode = true;
            shell.SyncOfflineModeState();
            ready(true);
            Assert.Equal(new Uri("about:blank"), host.Source);
        }
        finally { shell.Close(); CoreSettings.Current.OfflineMode = offline; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClosingTheShellReplacesAHeldRequestWithBlank() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var shell = new MainShellWindow();
        shell.Show();
        var host = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
        var ready = host.NewScrolllerEngine();
        host.Navigate(new Uri("https://127.0.0.1/watch"));
        shell.Close();
        ready(true);
        Assert.Equal(new Uri("about:blank"), host.Source);
        return Task.CompletedTask;
    });
}
