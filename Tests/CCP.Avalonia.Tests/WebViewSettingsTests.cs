using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebViewSettingsTests
{
    [Fact]
    public Task ZoomIsAppliedOncePerEngineBeforeNavigation() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var host = new WebHost { ZoomFactor = 0.5 };
        var calls = new List<double>();
        var pending = new TaskCompletionSource<bool>();
        host.NativeZoomInvoker = zoom => { calls.Add(zoom); return pending.Task; };
        var first = host.NewScrolllerEngine();
        first(true);
        first(true);
        Assert.Null(host.ScrolllerGate);
        Assert.Equal(new[] { 0.5 }, calls);
        var second = host.NewScrolllerEngine();
        pending.SetResult(true);
        await Task.Yield();
        Assert.Null(host.ScrolllerGate);
        second(true);
        Assert.True(host.ScrolllerGate);
        Assert.Equal(new[] { 0.5, 0.5 }, calls);
    });

    [Fact]
    public Task HomeRequestsHalfZoom() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var host = new SettingsTabView().FindControl<WebHost>("BrowserWebHost")!;
        double? requested = null;
        host.NativeZoomInvoker = zoom => { requested = zoom; return Task.FromResult(true); };
        host.NewScrolllerEngine()(true);
        Assert.Equal(0.5, requested);
        return Task.CompletedTask;
    });

    [Fact]
    public Task HomeResetsChangedZoomBeforeExplicitNavigations() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var home = new SettingsTabView().FindControl<WebHost>("BrowserWebHost")!;
        var host = new WebHost(true, () => new NativeWebView())
        {
            ZoomFactor = home.ZoomFactor
        };
        double currentZoom = 1.25;
        var seen = new List<(Uri Url, double Zoom)>();
        host.NativeNavigateInvoker = url => seen.Add((url, currentZoom));
        host.NativeZoomInvoker = zoom => { currentZoom = zoom; return Task.FromResult(true); };
        var report = host.NewScrolllerEngine();
        report(true);
        currentZoom = 1.25; // the user changed the engine zoom
        var site = new Uri("http://127.0.0.1/site");
        var pending = new TaskCompletionSource<bool>();
        host.NativeZoomInvoker = async zoom => { await pending.Task; currentZoom = zoom; return true; };
        host.Navigate(site, resetZoom: true); // the site toggle
        Assert.Empty(seen);
        report(true); // a late gate report cannot load the held URL first
        Assert.Empty(seen);
        pending.SetResult(true);
        await Task.Yield();
        Assert.Equal((site, 0.5), Assert.Single(seen));

        currentZoom = 1.75;
        host.NativeZoomInvoker = zoom => { currentZoom = zoom; return Task.FromResult(true); };
        var url = new Uri("http://127.0.0.1/video");
        host.Navigate(url, resetZoom: true); // the app-initiated URL path
        Assert.Equal((url, 0.5), seen[1]);
        currentZoom = 2.0;
        host.Navigate(url, resetZoom: true); // re-clicking the same site still resets zoom
        Assert.Equal((url, 0.5), seen[2]);
    });

    [Fact]
    public Task NewerNavigationAndEngineReplacementDiscardPendingZoomNavigation() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var host = new WebHost(true, () => new NativeWebView());
        var seen = new List<Uri>();
        host.NativeNavigateInvoker = seen.Add;
        host.NativeZoomInvoker = _ => Task.FromResult(true);
        host.NewScrolllerEngine()(true);
        var pending = new TaskCompletionSource<bool>();
        host.NativeZoomInvoker = _ => pending.Task;
        host.Navigate(new Uri("http://127.0.0.1/old"), resetZoom: true);
        var latest = new Uri("http://127.0.0.1/new");
        host.NativeZoomInvoker = _ => Task.FromResult(true);
        host.Navigate(latest, resetZoom: true);
        pending.SetResult(true);
        await Task.Yield();
        Assert.Equal(latest, Assert.Single(seen));
        pending = new TaskCompletionSource<bool>();
        host.NativeZoomInvoker = _ => pending.Task;
        host.Navigate(new Uri("http://127.0.0.1/stale-engine"), resetZoom: true);
        host.NewScrolllerEngine();
        pending.SetResult(true);
        await Task.Yield();
        Assert.Equal(latest, Assert.Single(seen));
    });

    [Fact]
    public Task ReloadPreservesChangedZoomWhileSiteAndUrlResetIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var oldOffline = ConditioningControlPanel.CoreSettings.Current.OfflineMode;
        ConditioningControlPanel.CoreSettings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        try
        {
            var host = new WebHost(true, () => new NativeWebView()) { ZoomFactor = 0.5 };
            double currentZoom = 0.5;
            var navigatedAt = new List<double>();
            host.NativeZoomInvoker = zoom => { currentZoom = zoom; return Task.FromResult(true); };
            host.NativeNavigateInvoker = _ => navigatedAt.Add(currentZoom);
            host.NewScrolllerEngine()(true);
            // Redirect the shell's requests to a loopback page inside the test sandbox.
            shell.BrowserNavigationInvoker = (_, reset) => host.Navigate(new Uri("http://127.0.0.1/"), reset);
            currentZoom = 1.25;
            shell.BtnReloadBrowser_Click(shell, new global::Avalonia.Interactivity.RoutedEventArgs());
            Assert.Equal(1.25, Assert.Single(navigatedAt));
            currentZoom = 1.5;
            shell.BrowserSiteToggle_Click(shell, new global::Avalonia.Interactivity.RoutedEventArgs());
            Assert.Equal(0.5, navigatedAt[1]);
            currentZoom = 1.75;
            Assert.True(shell.NavigateToUrlInBrowser("http://127.0.0.1/video"));
            Assert.Equal(0.5, navigatedAt[2]);
            currentZoom = 1.6;
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            tab.FindControl<RadioButton>("RbBambiCloud")!.IsChecked = true;
            tab.SyncBrowserSite(false, "https://hypnotube.com/", navigateIfChanged: true);
            Assert.Equal(1.6, navigatedAt[3]);
        }
        finally { shell.Close(); ConditioningControlPanel.CoreSettings.Current.OfflineMode = oldOffline; }
        return Task.CompletedTask;
    });

}
