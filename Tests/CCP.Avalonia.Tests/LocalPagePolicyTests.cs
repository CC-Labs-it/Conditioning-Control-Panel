using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LocalPagePolicyTests
{
    [Fact]
    public void OnlyAssetOriginSuppressesTheMenuOutsideHome()
    {
        using var server = new WebAssetServer(AppContext.BaseDirectory);
        var local = new Uri(server.Url("game/index.html"));
        Assert.True(LocalPagePolicy.SuppressContextMenu(local, server, false));
        Assert.False(LocalPagePolicy.SuppressContextMenu(local, server, true));
        Assert.False(LocalPagePolicy.IsLocalPage(new Uri(server.RelayOrigin), server));
        Assert.False(LocalPagePolicy.IsLocalPage(new Uri("http://example.com/"), server));
        Assert.False(LocalPagePolicy.IsLocalPage(new Uri($"https://127.0.0.1:{server.Port}/"), server));
        Assert.False(LocalPagePolicy.IsLocalPage(new Uri($"http://localhost:{server.Port}/"), server));
        Assert.False(LocalPagePolicy.IsLocalPage(null, server));
    }
}
