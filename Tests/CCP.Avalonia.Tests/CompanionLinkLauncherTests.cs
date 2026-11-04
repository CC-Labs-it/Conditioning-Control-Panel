using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionLinkLauncherTests
{
    [Theory]
    [InlineData("https://127.0.0.1/watch")]
    [InlineData("http://127.0.0.1/watch")]
    public Task WatchLinkUsesEmbeddedBrowser(string url) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var app = Application.Current!;
        var field = typeof(Application).GetField("_applicationLifetime",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var oldLifetime = field.GetValue(app);
        var oldOffline = CoreSettings.Current.OfflineMode;
        var oldOpen = AppUpdater.OpenUrl;
        var oldRelay = RemoteControlTabView.Relay;
        var lifetime = new ClassicDesktopStyleApplicationLifetime();
        using var relay = new RemoteRelay(() => null, () => null, "test", (_, _) => null, _ => { });
        var shell = new MainShellWindow();
        var external = new List<string>();
        try
        {
            field.SetValue(app, lifetime);
            lifetime.MainWindow = shell;
            CoreSettings.Current.OfflineMode = false;
            RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
            AppUpdater.OpenUrl = (_, value) => { external.Add(value); return Task.CompletedTask; };
            shell.Show();
            var tab = shell.Named<SettingsTabView>("SettingsTab")!;
            var host = tab.FindControl<WebHost>("BrowserWebHost")!;
            var requests = host.NavigationRequests;
            await CompanionLinkLauncher.OpenAsync(url);
            Assert.Equal(requests + 1, host.NavigationRequests);
            Assert.Equal(new Uri(url), host.Source);
            Assert.Empty(external);
        }
        finally
        {
            shell.Close();
            field.SetValue(app, oldLifetime);
            CoreSettings.Current.OfflineMode = oldOffline;
            AppUpdater.OpenUrl = oldOpen;
            RemoteControlTabView.Relay = oldRelay;
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Theory]
    [InlineData(false, true, "https://127.0.0.1/video", 1, 0)]
    [InlineData(false, false, "https://127.0.0.1/video", 1, 1)]
    [InlineData(false, false, "http://127.0.0.1/video", 1, 0)]
    [InlineData(true, true, "https://127.0.0.1/video", 0, 0)]
    [InlineData(false, true, "file:///tmp/video.mp4", 0, 0)]
    public Task BrowserPreferenceHttpsFallbackAndConnectedControllerGuard(bool connected, bool browserAvailable,
        string url, int expectedNavigations, int expectedExternal) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var previousNavigate = CompanionLinkLauncher.Navigate;
        var previousOpen = AppUpdater.OpenUrl;
        var previousRelay = RemoteControlTabView.Relay;
        using var relay = new RemoteRelay(() => null, () => null, "test", (_, _) => null, _ => { });
        // No HTTP or native desktop: set the real relay's current connection state directly.
        typeof(RemoteRelay).GetProperty(nameof(RemoteRelay.ControllerConnected))!.SetValue(relay, connected);
        var navigations = new List<string>();
        var external = new List<string>();
        try
        {
            RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
            _ = RemoteControlTabView.Relay.Value;
            CompanionLinkLauncher.Navigate = value => { navigations.Add(value); return browserAvailable; };
            AppUpdater.OpenUrl = (_, value) => { external.Add(value); return Task.CompletedTask; };
            await CompanionLinkLauncher.OpenAsync(url);
            Assert.Equal(expectedNavigations, navigations.Count);
            Assert.Equal(expectedExternal, external.Count);
            Assert.All(navigations, value => Assert.Equal(url, value));
            Assert.All(external, value => Assert.Equal(url, value));
        }
        finally
        {
            CompanionLinkLauncher.Navigate = previousNavigate;
            AppUpdater.OpenUrl = previousOpen;
            RemoteControlTabView.Relay = previousRelay;
        }
    });
}
