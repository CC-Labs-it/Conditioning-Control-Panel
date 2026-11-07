using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class Sync7VideoPolicyTests
{
    [Fact]
    public async Task OfflinePolicyDoesNotFetch_AndCanFetchAfterGoingOnline()
    {
        var s = CoreSettings.Current;
        var saved = s.OfflineMode;
        var fetch = ScrolllerPolicyService.FetchOverride;
        int calls = 0;
        try
        {
            ScrolllerPolicyService.FetchOverride = () =>
            {
                calls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            };
            s.OfflineMode = true;
            await ScrolllerPolicyService.RefreshAsync();
            Assert.Equal(0, calls);
            s.OfflineMode = false;
            await ScrolllerPolicyService.RefreshAsync();
            Assert.Equal(1, calls);
        }
        finally { s.OfflineMode = saved; ScrolllerPolicyService.FetchOverride = fetch; }
    }

    [Theory]
    [InlineData("https://v.scrolller.com/a.mp4")]
    [InlineData("http://v.scrolller.com/a.webm")]
    public void RemoteClipsIdentifyTheApp(string path)
    {
        var options = RemoteVideoOptions.For(path);
        Assert.Contains(":http-user-agent=" + ScrolllerSource.HonestUserAgent, options);
        Assert.Contains(":http-reconnect", options);
        Assert.Empty(RemoteVideoOptions.For("/tmp/a.mp4"));
    }
}
