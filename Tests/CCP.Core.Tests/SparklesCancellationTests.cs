using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Sparkles;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class SparklesCancellationTests
{
    private sealed class Pending : HttpMessageHandler
    {
        public int Sends;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sends++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            { Content = new StringContent("""{"pending":true,"retryAfterMs":1500}""") });
        }
    }

    [Fact]
    public async Task CancellationStopsPendingRetries()
    {
        var handler = new Pending();
        using var stop = new CancellationTokenSource();
        var service = new SparklesClaimService(new HttpClient(handler), () => ("me", "test"),
            delay: (_, ct) => { stop.Cancel(); return Task.FromCanceled(ct); });
        var claim = SparklesClaim.Create("chess", "online", SparklesResult.ForDuel("win"), 600000);
        Assert.Equal("pending", (await service.ClaimAsync(claim, stop.Token)).Reason);
        Assert.Equal(1, handler.Sends);
        await service.ClaimAsync(claim, stop.Token);
        Assert.Equal(1, handler.Sends);
    }

    [Theory]
    [InlineData(double.MaxValue, 5000)]
    [InlineData(double.PositiveInfinity, 1500)]
    [InlineData(double.NaN, 1500)]
    [InlineData(1, 500)]
    public void UntrustedRetryDelayIsBounded(double ms, int expected) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expected),
            SparklesClaimService.RetryWait(new JObject { ["retryAfterMs"] = ms }));
}
