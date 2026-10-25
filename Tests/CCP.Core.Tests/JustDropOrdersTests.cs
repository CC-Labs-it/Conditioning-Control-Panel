using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.JustDrop;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class JustDropOrdersTests
{
    [Fact]
    public void ReceiptsKeepServerOrderAndRejectUnplayableRows()
    {
        var orders = JustDropOrdersService.Parse("{\"orders\":[{\"code\":\"A\",\"sizeId\":\"S\",\"name\":\"First\",\"at\":1000},{\"code\":\" \"},{\"code\":\"B\",\"sizeId\":\"new\"}]}");
        Assert.Equal(new[] { "A", "B" }, orders.Select(o => o.Code));
        Assert.Equal(5, orders[0].Minutes);
        Assert.Equal("First", TakeawayText.Name(orders[0]));
        Assert.Equal(0, orders[1].Minutes);
        Assert.Equal(TakeawayText.Date(orders[1]), TakeawayText.Meta(orders[1]));
        Assert.Equal("", TakeawayText.Minutes(orders[1]));
        Assert.Empty(JustDropOrdersService.Parse("invalid"));
        Assert.Empty(JustDropOrdersService.Parse("{}"));
    }

    [Fact]
    public async Task DeviceDoorUsesHeaderAndCachesOnlyTheCurrentAccountUntilInvalidated()
    {
        var previous = (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, JustDropOrdersService.Http);
        string? token = "test-token";
        var handler = new Handler();
        using var client = new HttpClient(handler);
        CoreAccount.UnifiedUserId = "u1";
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? token : null;
        JustDropOrdersService.Http = client;
        JustDropOrdersService.Invalidate();
        try
        {
            Assert.Single(await JustDropOrdersService.FetchAsync());
            Assert.Single(await JustDropOrdersService.FetchAsync());
            Assert.Equal(1, handler.Calls);
            Assert.Equal("test-token", handler.Token);
            Assert.Null(handler.Authorization);
            Assert.DoesNotContain("test-token", handler.Url!);
            Assert.Contains("unified_id=u1", handler.Url!);
            JustDropOrdersService.Invalidate();
            await JustDropOrdersService.FetchAsync();
            Assert.Equal(2, handler.Calls);
            CoreAccount.UnifiedUserId = "u2";
            await JustDropOrdersService.FetchAsync();
            Assert.Equal(3, handler.Calls);
            token = null;
            Assert.Empty(await JustDropOrdersService.FetchAsync());
            Assert.Equal(3, handler.Calls);
        }
        finally
        {
            (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, JustDropOrdersService.Http) = previous;
            JustDropOrdersService.Invalidate();
        }
    }

    [Theory]
    [InlineData("S", 5)]
    [InlineData("M", 15)]
    [InlineData("L", 30)]
    [InlineData("XXL", 60)]
    public void ReceiptSizeAndAgeUseTheSharedWpfLabels(string size, int minutes)
    {
        var now = DateTimeOffset.UtcNow;
        var order = new JustDropOrdersService.Order { SizeId = size, At = now.AddDays(-1) };
        Assert.Equal(minutes, order.Minutes);
        Assert.Equal(Loc.GetF("sd_takeaway_meta", minutes, TakeawayText.Date(order)), TakeawayText.Meta(order));
        Assert.Equal(Loc.Get("sd_takeaway_order_fallback"), TakeawayText.Name(order));   // ages: TakeawayDrawerTests
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls;
        internal string? Token, Authorization, Url;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Token = request.Headers.GetValues("X-Auth-Token").Single();
            Authorization = request.Headers.Authorization?.ToString();
            Url = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"orders\":[{\"code\":\"A\"}]}") });
        }
    }
}
