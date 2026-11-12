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
    [Fact]
    public Task MissingEngineUsesTheCompanionFallbackOnce() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var offline = CoreSettings.Current.OfflineMode;
        var open = AppUpdater.OpenUrl;
        var shellOpen = ExternalOpener.Shell;
        var calls = 0;
        var shellCalls = 0;
        var shell = new MainShellWindow();
        var lifetime = Application.Current!.ApplicationLifetime;
        var field = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType));
        using var desktop = new ClassicDesktopStyleApplicationLifetime { MainWindow = shell, ShutdownMode = ShutdownMode.OnExplicitShutdown };
        field.SetValue(Application.Current, desktop);
        try
        {
            CoreSettings.Current.OfflineMode = false;
            AppUpdater.OpenUrl = (_, _) => { calls++; return Task.CompletedTask; };
            ExternalOpener.Shell = _ => { shellCalls++; return true; };
            shell.Show();
            Assert.False(shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!.HasEngine);
            Assert.False(shell.NavigateToUrlInBrowser("https://127.0.0.1/watch", autoPlayFullscreen: true));
            await CompanionLinkLauncher.OpenAsync("https://127.0.0.1/watch");
            Assert.Equal(1, calls);
            Assert.Equal(0, shellCalls);
        }
        finally
        {
            shell.Close();
            field.SetValue(Application.Current, lifetime);
            CoreSettings.Current.OfflineMode = offline;
            AppUpdater.OpenUrl = open;
            ExternalOpener.Shell = shellOpen;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicClearsNavigationHeldForTheBrowserGate(bool gateTimedOut) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var shell = new MainShellWindow();
        var settings = CoreSettings.Current;
        var oldStrict = settings.StrictLockEnabled;
        var oldLockdown = LockdownService.Current;
        using var lockdown = new LockdownService();
        try
        {
            LockdownService.Current = lockdown;
            lockdown.Activate(TimeSpan.FromMinutes(30));
            settings.StrictLockEnabled = true;
            var host = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var ready = host.NewScrolllerEngine();
            host.Navigate(new Uri("https://127.0.0.1/watch"));
            if (gateTimedOut) ready(false);
            PanicSurfaces.All.Single(surface => surface.Id == "browser-media").Stop(shell);
            Assert.Null(host.Source);
            ready(true);
            Assert.Null(host.Source);
            Assert.True(lockdown.IsActive);
            Assert.True(settings.StrictLockEnabled);
        }
        finally
        {
            lockdown.Deactivate();
            LockdownService.Current = oldLockdown;
            settings.StrictLockEnabled = oldStrict;
            shell.Close();
        }
        return Task.CompletedTask;
    });
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
