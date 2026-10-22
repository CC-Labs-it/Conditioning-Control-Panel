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
        Assert.Equal("First", orders[0].DisplayName);
        Assert.Equal(0, orders[1].Minutes);
        Assert.Equal(orders[1].DateLabel, orders[1].Meta);
        Assert.Equal("", orders[1].MinutesLabel);
        Assert.Empty(JustDropOrdersService.Parse("invalid"));
        Assert.Empty(JustDropOrdersService.Parse("{}"));
    }

    [Fact]
    public async Task DeviceDoorUsesHeaderAndCachesOnlyTheCurrentAccountUntilInvalidated()
    {
        var previous = (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, JustDropOrdersService.HttpOverride);
        string? token = "test-token";
        var handler = new Handler();
        using var client = new HttpClient(handler);
        CoreAccount.UnifiedUserId = "u1";
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? token : null;
        JustDropOrdersService.HttpOverride = client;
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
            (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, JustDropOrdersService.HttpOverride) = previous;
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
        Assert.Equal(Loc.GetF("sd_takeaway_meta", minutes, order.DateLabel), order.Meta);
        Assert.Equal(Loc.GetF("takeaway_day_ago", 1), order.AgeLabel(now));
        Assert.Equal(Loc.Get("takeaway_today"), order.AgeLabel(now.AddDays(-2)));
        Assert.Equal(Loc.GetF("takeaway_days_ago", 3), order.AgeLabel(now.AddDays(2)));
        Assert.Equal(Loc.Get("sd_takeaway_order_fallback"), order.DisplayName);
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
