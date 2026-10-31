using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    // Route the existing host seam to the real shell navigation, not a boolean fake.
    [Theory]
    [InlineData(false, "https://127.0.0.1/video", 1)]
    [InlineData(false, "http://127.0.0.1/video", 0)]
    [InlineData(true, "https://127.0.0.1/video", 0)]
    public Task MissingNativeEngineReachesOnlyTheHttpsFallback(bool hasEngine, string url, int expectedExternal)
        => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification,
            settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
        using var relay = new RemoteRelay(() => null, () => null, "test", (_, _) => null, _ => { });
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        var previousOpen = AppUpdater.OpenUrl;
        var previousNavigate = CompanionLinkLauncher.Navigate;
        var external = new List<string>();
        var shell = new MainShellWindow();
        try
        {
            CompanionLinkLauncher.Navigate = value => shell.NavigateToUrlInBrowser(value, autoPlayFullscreen: true);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            if (hasEngine) host.ScriptInvoker = _ => Task.FromResult<string?>(null);
            Assert.Equal(hasEngine, host.HasEngine);
            AppUpdater.OpenUrl = (_, value) => { external.Add(value); return Task.CompletedTask; };
            await CompanionLinkLauncher.OpenAsync(url);
            Assert.Equal(new Uri(url), host.Source);
            Assert.Equal(expectedExternal, external.Count);
            Assert.All(external, value => Assert.Equal(url, value));
        }
        finally
        {
            AppUpdater.OpenUrl = previousOpen;
            CompanionLinkLauncher.Navigate = previousNavigate;
            shell.Close();
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
