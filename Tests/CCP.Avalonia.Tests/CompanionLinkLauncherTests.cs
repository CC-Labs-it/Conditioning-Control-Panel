using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls;
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
            CompanionLinkLauncher.Navigate = _ => Task.FromResult<bool?>(false);
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
    [InlineData(true, "https://127.0.0.1/video", 0)]
    [InlineData(false, "https://127.0.0.1/video", 1)]
    [InlineData(true, "http://localhost/video", 0)]
    [InlineData(false, "http://localhost/video", 0)]
    public Task PendingGateFinishesTheWatchRequest(bool allowed, string url, int expectedExternal) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var web = new WebHost();
        var navigations = new List<Uri>();
        web.NavigationInvoker = uri => { navigations.Add(uri); return true; };
        var report = web.NewScrolllerEngine();
        var oldNavigate = CompanionLinkLauncher.Navigate;
        var oldOpen = AppUpdater.OpenUrl;
        var oldRelay = RemoteControlTabView.Relay;
        var external = new List<string>();
        try
        {
            RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => throw new InvalidOperationException());
            CompanionLinkLauncher.Navigate = url => web.NavigateWhenReadyAsync(new Uri(url));
            AppUpdater.OpenUrl = (_, url) => { external.Add(url); return Task.CompletedTask; };
            var opening = CompanionLinkLauncher.OpenAsync(url);
            Assert.False(opening.IsCompleted);
            Assert.Empty(navigations);
            Assert.Empty(external);
            report(allowed);
            await opening;
            Assert.Equal(allowed ? 1 : 0, navigations.Count);
            Assert.Equal(expectedExternal, external.Count);
            if (allowed) Assert.Equal(url, navigations[0].AbsoluteUri);
            if (expectedExternal != 0) Assert.Equal(url, external[0]);
        }
        finally
        {
            CompanionLinkLauncher.Navigate = oldNavigate;
            AppUpdater.OpenUrl = oldOpen;
            RemoteControlTabView.Relay = oldRelay;
        }
    });

    [Fact]
    public Task NewWatchRequestCancelsThePendingOne() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var web = new WebHost();
        var navigations = new List<Uri>();
        web.NavigationInvoker = uri => { navigations.Add(uri); return true; };
        var report = web.NewScrolllerEngine();
        var oldNavigate = CompanionLinkLauncher.Navigate;
        var oldOpen = AppUpdater.OpenUrl;
        var oldRelay = RemoteControlTabView.Relay;
        var external = new List<string>();
        try
        {
            RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => throw new InvalidOperationException());
            CompanionLinkLauncher.Navigate = url => web.NavigateWhenReadyAsync(new Uri(url));
            AppUpdater.OpenUrl = (_, url) => { external.Add(url); return Task.CompletedTask; };
            var first = CompanionLinkLauncher.OpenAsync("https://127.0.0.1/first");
            var second = CompanionLinkLauncher.OpenAsync("https://127.0.0.1/second");
            await first;
            Assert.Empty(external);
            report(true);
            await second;
            Assert.Single(navigations);
            Assert.Equal("https://127.0.0.1/second", navigations[0].AbsoluteUri);
            Assert.Empty(external);
        }
        finally
        {
            CompanionLinkLauncher.Navigate = oldNavigate;
            AppUpdater.OpenUrl = oldOpen;
            RemoteControlTabView.Relay = oldRelay;
        }
    });

    [Fact]
    public Task RecreatedEngineDoesNotUseThePreviousGateReport() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var web = new WebHost();
        var navigations = 0;
        web.NavigationInvoker = _ => { navigations++; return true; };
        var firstReport = web.NewScrolllerEngine();
        var opening = web.NavigateWhenReadyAsync(new Uri("https://127.0.0.1/video"));
        firstReport(true);
        var secondReport = web.NewScrolllerEngine();
        await Task.Yield();
        Assert.False(opening.IsCompleted);
        secondReport(false);
        Assert.False(await opening);
        Assert.Equal(0, navigations);
        Assert.Null(web.Source);
    });

    [Fact]
    public Task ThrowingNavigationRestoresSourceBeforeFallback() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var web = new WebHost();
        var previous = new Uri("http://127.0.0.1/previous");
        web.Source = previous;
        web.NavigationInvoker = _ => throw new InvalidOperationException();
        web.NewScrolllerEngine()(true);
        var oldNavigate = CompanionLinkLauncher.Navigate;
        var oldOpen = AppUpdater.OpenUrl;
        var oldRelay = RemoteControlTabView.Relay;
        var external = new List<string>();
        try
        {
            RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => throw new InvalidOperationException());
            CompanionLinkLauncher.Navigate = url => web.NavigateWhenReadyAsync(new Uri(url));
            AppUpdater.OpenUrl = (_, url) => { external.Add(url); return Task.CompletedTask; };
            const string url = "https://127.0.0.1/video";
            await CompanionLinkLauncher.OpenAsync(url);
            Assert.Equal(new[] { url }, external);
            Assert.Equal(previous, web.Source);
            web.NewScrolllerEngine()(true);
            Assert.Equal(previous, web.Source);
        }
        finally
        {
            CompanionLinkLauncher.Navigate = oldNavigate;
            AppUpdater.OpenUrl = oldOpen;
            RemoteControlTabView.Relay = oldRelay;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RefusedGateFallsBackWithoutRetainingTheRequest(bool reportBeforeClick) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            ChatThresholdLinksTests.Setup();
            var web = new WebHost();
            var navigations = 0;
            web.NavigationInvoker = _ => { navigations++; return true; };
            var report = web.NewScrolllerEngine();
            if (reportBeforeClick) report(false);
            var oldNavigate = CompanionLinkLauncher.Navigate;
            var oldOpen = AppUpdater.OpenUrl;
            var oldRelay = RemoteControlTabView.Relay;
            var external = new List<string>();
            try
            {
                RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => throw new InvalidOperationException());
                CompanionLinkLauncher.Navigate = url => web.NavigateWhenReadyAsync(new Uri(url));
                AppUpdater.OpenUrl = (_, url) => { external.Add(url); return Task.CompletedTask; };
                const string url = "https://127.0.0.1/video";
                var opening = CompanionLinkLauncher.OpenAsync(url);
                if (!reportBeforeClick) { Assert.False(opening.IsCompleted); report(false); }
                await opening;
                Assert.Equal(new[] { url }, external);
                Assert.Equal(0, navigations);
                Assert.Null(web.Source);
                report(false);
                Assert.Null(web.Source);
            }
            finally
            {
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
            CompanionLinkLauncher.Navigate = value => { navigations.Add(value); return Task.FromResult<bool?>(browserAvailable); };
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
