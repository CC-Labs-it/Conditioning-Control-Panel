using System;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>This host is a CCP_USERDATA_DIR sandbox (TestUserDataProfile): the always-navigate path must refuse
/// a real site exactly as the Source setter does, and still hand loopback on.</summary>
public sealed class WebHostSandboxTests
{
    [Fact]
    public async Task NavigateRefusesARealSiteInASandbox() =>
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var web = new WebHost();
            web.NewScrolllerEngine()(true);
            Assert.False(web.Navigate(new Uri("https://hypnotube.com/")));
            Assert.Equal(1, web.RefusedNavigations);
            Assert.False(web.Navigate(new Uri("http://127.0.0.1:3001/"))); // no native engine
            Assert.Equal(1, web.RefusedNavigations);
            Assert.Equal(2, web.NavigationRequests);
            return Task.CompletedTask;
        });
}
