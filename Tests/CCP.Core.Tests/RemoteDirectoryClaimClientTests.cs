using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Core.Tests;

public class RemoteDirectoryClaimClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };
    private static RemoteDirectoryClaimClient Client(Handler handler, Func<(string, string)?>? identity = null) =>
        new(new HttpClient(handler), identity ?? (() => ("caller", "token")), "https://example.test");

    [Fact]
    public async Task SendsAuthenticatedSubjectAndReturnsTransientUrl()
    {
        var client = Client(new(async req => {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://example.test/v2/directory/claim", req.RequestUri!.ToString());
            Assert.Equal("token", Assert.Single(req.Headers.GetValues("X-Auth-Token")));
            Assert.Equal("caller", Assert.Single(req.Headers.GetValues("X-Caller-Unified-Id")));
            Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("subject", Newtonsoft.Json.Linq.JObject.Parse(await req.Content.ReadAsStringAsync()).Value<string>("unified_id"));
            return Reply(HttpStatusCode.OK, "{\"session_url\":\"https://example.test/#pin=1234\"}");
        }));
        var result = await client.ClaimAsync("subject");
        Assert.Equal(RemoteClaimStatus.Success, result.Status);
        Assert.Equal("https://example.test/#pin=1234", result.SessionUrl);
        Assert.DoesNotContain("1234", result.ToString()!);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "{}", RemoteClaimStatus.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, "{\"session_url\":\"secret\"}", RemoteClaimStatus.Error)]
    [InlineData(HttpStatusCode.OK, "{}", RemoteClaimStatus.Error)]
    [InlineData(HttpStatusCode.OK, "invalid", RemoteClaimStatus.Error)]
    public async Task RefusalsAndMalformedRepliesDoNotReturnSecrets(HttpStatusCode status, string body, RemoteClaimStatus expected)
    {
        var result = await Client(new(_ => Task.FromResult(Reply(status, body)))).ClaimAsync("subject");
        Assert.Equal(expected, result.Status);
        Assert.Null(result.SessionUrl);
    }

    [Fact]
    public async Task MissingAuthOrSubjectDoesNotSendAndNetworkFailureIsAnError()
    {
        var calls = 0;
        var handler = new Handler(_ => { calls++; throw new HttpRequestException("secret"); });
        Assert.Equal(RemoteClaimStatus.Error, (await Client(handler, () => ("", "token")).ClaimAsync("subject")).Status);
        Assert.Equal(RemoteClaimStatus.Error, (await Client(handler).ClaimAsync("")).Status);
        Assert.Equal(0, calls);
        Assert.Equal(RemoteClaimStatus.Error, (await Client(handler).ClaimAsync("subject")).Status);
        Assert.Equal(1, calls);
    }
}
