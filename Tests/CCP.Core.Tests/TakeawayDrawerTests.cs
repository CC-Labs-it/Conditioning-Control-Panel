using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Core.Tests;

public sealed class TakeawayDrawerTests : IDisposable
{
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly string? _identity = CoreAccount.UnifiedUserId;
    private readonly HttpClient _http = JustDropOrdersService.Http;
    private string? _token = "test-device-token";
    private const string Body = "{\"orders\":[{\"id\":\"o1\",\"code\":\"receipt\",\"name\":\"Kept drop\",\"sizeId\":\"M\",\"at\":1786492800000,\"paidOut\":true}]}";

    public TakeawayDrawerTests()
    {
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? _token : null;
        CoreAccount.UnifiedUserId = "user one";
        JustDropOrdersService.Invalidate();
    }
    public void Dispose()
    {
        JustDropOrdersService.Http = _http;
        JustDropOrdersService.Invalidate();
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreAccount.UnifiedUserId = _identity;
    }
    private sealed class Feed(Func<Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        internal int Calls;
        internal string? Path, Token;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Path = request.RequestUri!.PathAndQuery;
            Token = string.Join("", request.Headers.GetValues("X-Auth-Token"));
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(HttpMethod.Get, request.Method);
            return reply();
        }
    }
    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent(Body) };

    [Fact]
    public async Task SandboxRefusesTheRealDrawerEvenWhenACacheWasPrimedByATestClient()
    {
        Assert.True(ConditioningControlPanel.Services.SandboxNet.Active);
        using var feed = new Feed(() => Task.FromResult(Ok()));
        using var client = new HttpClient(feed);
        JustDropOrdersService.Http = client;
        Assert.Single(await JustDropOrdersService.FetchAsync());
        JustDropOrdersService.Http = _http;
        Assert.Empty(await JustDropOrdersService.FetchAsync());
        Assert.Equal(1, feed.Calls);
    }

    [Fact]
    public async Task DeviceDoorCacheIsBoundToTheSignedInIdentityAndCanBeInvalidated()
    {
        using var feed = new Feed(() => Task.FromResult(Ok()));
        using var client = new HttpClient(feed); JustDropOrdersService.Http = client;
        Assert.Single(await JustDropOrdersService.FetchAsync());
        Assert.Equal("/v2/justdrop/orders?unified_id=user%20one", feed.Path);
        Assert.Equal(_token, feed.Token);
        Assert.Single(await JustDropOrdersService.FetchAsync()); Assert.Equal(1, feed.Calls);
        _token = null;
        Assert.Empty(await JustDropOrdersService.FetchAsync()); Assert.Equal(1, feed.Calls);
        _token = "second-device-token"; CoreAccount.UnifiedUserId = "other-user";
        Assert.Single(await JustDropOrdersService.FetchAsync()); Assert.Equal(2, feed.Calls);
        JustDropOrdersService.Invalidate();
        Assert.Single(await JustDropOrdersService.FetchAsync()); Assert.Equal(3, feed.Calls);
    }

    [Fact]
    public async Task AResponseFromThePreviousAccountIsDropped()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var feed = new Feed(() => response.Task);
        using var client = new HttpClient(feed); JustDropOrdersService.Http = client;
        var pending = JustDropOrdersService.FetchAsync();
        CoreAccount.UnifiedUserId = "next-user";
        response.SetResult(Ok());
        Assert.Empty(await pending);
    }

    [Fact]
    public void ParsingKeepsServerOrderAndUnknownSizesHaveNoGuessedDuration()
    {
        var rows = JustDropOrdersService.Parse(Body);
        var order = Assert.Single(rows);
        Assert.Equal("receipt", order.Code); Assert.Equal(15, order.Minutes); Assert.True(order.PaidOut);
        Assert.Empty(JustDropOrdersService.Parse("not json"));
        var valid = JustDropOrdersService.Parse("{\"orders\":[null,{\"code\":\" \"},{\"code\":\"new\",\"sizeId\":\"future\"},{\"code\":\"old\",\"sizeId\":\"XXL\"}]}");
        Assert.Equal(2, valid.Count); Assert.Equal("new", valid[0].Code); Assert.Equal(0, valid[0].Minutes);
        Assert.Equal(60, valid[1].Minutes);
    }

    [Fact]
    public void ReceiptTextUsesLocalisedAgeAndNeverPrintsZeroMinutes()
    {
        var loc = LocalizationManager.Instance; var language = loc.CurrentLanguage;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            loc.SetLanguage("en");
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            var at = new DateTimeOffset(2026, 8, 12, 0, 0, 0, TimeSpan.Zero);
            var unknown = new JustDropOrdersService.Order { At = at };
            Assert.Equal(Loc.Get("sd_takeaway_order_fallback"), TakeawayText.Name(unknown));
            Assert.Equal("AUG 12", TakeawayText.Meta(unknown)); Assert.Equal("", TakeawayText.Minutes(unknown));
            Assert.Equal("today", TakeawayText.Age(unknown, at.AddDays(-2)));
            Assert.Equal("1 day ago", TakeawayText.Age(unknown, at.AddHours(47)));
            Assert.Equal("2 days ago", TakeawayText.Age(unknown, at.AddDays(2)));
            Assert.Equal("+7 more  ▾", TakeawayText.More(7, false));
            Assert.Equal("+7 more  ▴", TakeawayText.More(7, true));
        }
        finally { loc.SetLanguage(language); System.Globalization.CultureInfo.CurrentCulture = culture; }
    }
}
