using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionLinkLauncherTests
{
    [Fact]
    public Task OfflineRejectionCannotFallThroughToExternalBrowser() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var oldOffline = ConditioningControlPanel.CoreSettings.Current.OfflineMode;
        var oldNavigate = CompanionLinkLauncher.Navigate;
        var oldOpen = AppUpdater.OpenUrl;
        var oldRelay = RemoteControlTabView.Relay;
        var opens = 0;
        try
        {
            ConditioningControlPanel.CoreSettings.Current.OfflineMode = true;
            RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => oldRelay.Value);
            CompanionLinkLauncher.Navigate = _ => false;
            AppUpdater.OpenUrl = (_, _) => { opens++; return Task.CompletedTask; };
            await CompanionLinkLauncher.OpenAsync("https://127.0.0.1/video");
            Assert.Equal(0, opens);
        }
        finally
        {
            ConditioningControlPanel.CoreSettings.Current.OfflineMode = oldOffline;
            CompanionLinkLauncher.Navigate = oldNavigate;
            AppUpdater.OpenUrl = oldOpen;
            RemoteControlTabView.Relay = oldRelay;
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
