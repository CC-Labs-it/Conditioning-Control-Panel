using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

// Restack b175 (#4294), adapted to the trunk's NativeViewFactory seam: a failed adapter construction is
// retried on the next navigation and the requested URL reaches the replacement once its gate reports.
public sealed class BrowserConstructionRetryTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task FailedConstructionIsRetriedWithTheRequestedSite(int failures) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var attempts = 0;
        NativeWebView? replacement = null;
        var host = new WebHost { RetryFailedConstruction = true };
        host.NativeViewFactory = () =>
        {
            if (++attempts <= failures) throw new InvalidOperationException("initialization failed");
            return replacement = new NativeWebView();
        };
        Assert.False(host.HasEngine);
        var report = host.NewScrolllerEngine();
        for (var i = 1; i <= failures; i++)
        {
            var first = new Uri($"http://127.0.0.1/failed{i}");
            Assert.False(host.Navigate(first));
            Assert.Equal(i, attempts);
            Assert.False(host.HasEngine);
            Assert.True(host.FindControl<Border>("Fallback")!.IsVisible);
            Assert.Equal(first, host.Source);
        }
        var site = new Uri("http://127.0.0.1/requested");
        Assert.True(host.Navigate(site)); // held until the gate reports
        Assert.Equal(failures + 1, attempts);
        Assert.True(host.HasEngine);
        Assert.NotNull(replacement);
        Assert.Equal(site, host.Source);
        Assert.NotEqual(site, replacement!.Source);
        report(true);
        Assert.Equal(site, replacement.Source);
        return Task.CompletedTask;
    });

    [Fact]
    public Task HostsThatDidNotOptInAreNeverRebuiltByNavigation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var attempts = 0;
        var host = new WebHost { NativeViewFactory = () => { attempts++; return new NativeWebView(); } };
        Assert.False(host.Navigate(new Uri("http://127.0.0.1/page")));
        Assert.Equal(0, attempts);
        Assert.False(host.HasEngine);
        return Task.CompletedTask;
    });
}
