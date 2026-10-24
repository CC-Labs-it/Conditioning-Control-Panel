using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class DisplayNameAvailabilityTests
{
    private sealed class Wire(string body, HttpStatusCode status) : HttpMessageHandler
    {
        public string? Query;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Assert.Equal("/v2/auth/check-name", r.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.Null(r.Headers.Authorization);
            Query = r.RequestUri.Query;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Theory]
    [InlineData("{\"available\":true}", 200, true)]
    [InlineData("{\"available\":false}", 200, false)]
    [InlineData("{}", 200, null)]
    [InlineData("bad json", 200, null)]
    [InlineData("{}", 503, null)]
    public async Task Check_is_unauthenticated_and_failure_leaves_final_decision_to_rename(string body, int status, bool? expected)
    {
        var wire = new Wire(body, (HttpStatusCode)status);
        var api = new V2AuthService(handler: wire);
        Assert.Equal(expected, await api.IsDisplayNameAvailableAsync("Name & Space"));
        Assert.Equal("?name=Name%20%26%20Space", wire.Query);
    }
}
