using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserConstructionRetryTests
{
    [Fact]
    public Task FailedConstructionRetainsTheOriginalSiteUrl() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var attempts = 0;
        var host = new WebHost(true, () =>
        {
            if (++attempts == 1) throw new InvalidOperationException("initialization failed");
            return new NativeWebView();
        }) { RetryFailedConstruction = true };
        Assert.False(host.HasEngine);
        var site = new Uri("https://hypnotube.com/");
        host.Navigate(site);
        Assert.Equal(2, attempts);
        Assert.True(host.HasEngine);
        Assert.Equal(site, host.Source);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task FailedConstructionIsRetriedWithTheRequestedSite(int failures) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var attempts = 0;
        NativeWebView? replacement = null;
        var host = new WebHost(true, () =>
        {
            if (++attempts <= failures) throw new InvalidOperationException("initialization failed");
            return replacement = new NativeWebView();
        }) { RetryFailedConstruction = true };
        Assert.False(host.HasEngine);
        var report = host.NewScrolllerEngine();
        if (failures == 2)
        {
            var first = new Uri("http://127.0.0.1/first");
            host.Navigate(first);
            Assert.Equal(2, attempts);
            Assert.False(host.HasEngine);
            Assert.True(host.FindControl<Border>("Fallback")!.IsVisible);
            Assert.Equal(first, host.Source);
        }
        var site = new Uri("http://127.0.0.1/requested");
        host.Navigate(site);
        Assert.Equal(failures + 1, attempts);
        Assert.True(host.HasEngine);
        Assert.NotNull(replacement);
        Assert.Equal(site, host.Source);
        Assert.NotEqual(site, replacement.Source);
        report(true);
        Assert.Equal(site, replacement.Source);
        return Task.CompletedTask;
    });
}
