using System;
using ConditioningControlPanel.Services.Funnel.Portable;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class FunnelPageMessageTests : IDisposable
{
    public FunnelPageMessageTests() => FunnelPageMessage.Reset();
    public void Dispose() => FunnelPageMessage.Reset();

    [Theory]
    [InlineData("card", "prime", "patreon")]
    [InlineData("", "basic", "patreon")]
    [InlineData("patreon", "basic", "patreon")]
    [InlineData("substar", "basic", "substar")]
    [InlineData("web", "prime", "web")]
    public void CountedRailMatchesTheCheckout(string rail, string plan, string expectedRail)
    {
        var request = FunnelPageMessage.Read(Message(rail, plan), PlanCatalog.Fallback)!;
        var info = PlanCatalog.Fallback.ForKey(plan)!;
        var expectedUrl = expectedRail switch
        {
            "web" => PlanCatalog.WebUrlFor(info),
            "substar" => info.SubstarUrl,
            _ => info.PatreonUrl,
        };
        Assert.Equal(expectedRail, request.B);
        Assert.Equal(expectedUrl, request.Url);
        Assert.Equal("cta_clicked|panel|dtrh|" + expectedRail,
            FunnelBeacon.Body(request.Event, request.A, request.B));
    }

    [Fact]
    public void UnknownPlanNeverOpensAPageSuppliedUrl()
    {
        var message = Message("card", "unknown");
        message["url"] = "https://example.com";
        Assert.Null(FunnelPageMessage.Read(message)!.Url);
        Assert.Null(FunnelPageMessage.Read(new JObject { ["type"] = "funnel", ["event"] = "unknown" }));
    }

    [Fact]
    public void OpensAreThrottledAndSuppressedDuringARun()
    {
        var now = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(FunnelPageMessage.TakeOpenSlot(now));
        Assert.False(FunnelPageMessage.TakeOpenSlot(now.AddSeconds(1)));
        Assert.True(FunnelPageMessage.TakeOpenSlot(now.AddSeconds(3)));
        FunnelPageMessage.Reset();
        var before = FunnelBeacon.BaseUrlOverride;
        FunnelBeacon.BaseUrlOverride = "";
        try
        {
            int opened = 0;
            FunnelPageMessage.Handle(Message("card", "prime"), _ => opened++, mayOpen: false);
            Assert.Equal(0, opened);
            FunnelPageMessage.Handle(Message("card", "prime"), _ => opened++);
            FunnelPageMessage.Handle(Message("card", "prime"), _ => opened++);
            Assert.Equal(1, opened);
        }
        finally { FunnelBeacon.BaseUrlOverride = before; }
    }

    [Fact]
    public void OfflineBeaconNeverSends()
    {
        var provider = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var send = FunnelBeacon.SendOverride;
        var root = FunnelBeacon.BaseUrlOverride;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        settings.Current.OfflineMode = true;
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => settings;
        using var sent = new System.Threading.ManualResetEventSlim();
        FunnelBeacon.SendOverride = (_, _) => { sent.Set(); return System.Threading.Tasks.Task.CompletedTask; };
        FunnelBeacon.BaseUrlOverride = "https://example.com";
        try
        {
            FunnelBeacon.Post("cta_clicked", "dtrh", "patreon");
            Assert.False(sent.Wait(TimeSpan.FromMilliseconds(250)));
        }
        finally
        {
            ConditioningControlPanel.CoreSettings.ServiceProvider = provider;
            FunnelBeacon.SendOverride = send;
            FunnelBeacon.BaseUrlOverride = root;
        }
    }

    private static JObject Message(string rail, string plan) => new()
    {
        ["type"] = "funnel", ["event"] = "cta_clicked", ["a"] = "dtrh", ["b"] = rail, ["plan"] = plan,
    };
}
