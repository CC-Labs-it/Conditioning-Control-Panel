using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
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
}
