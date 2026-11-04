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
    public Task FailedConstructionIsRetriedWithTheRequestedSite() => AvaloniaTestDispatcher.RunAsync(() =>
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
}
