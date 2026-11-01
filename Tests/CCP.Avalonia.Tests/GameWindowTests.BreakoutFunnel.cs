using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF BackRoomHostService.cs:589-616, FunnelPageMessage.cs:37-77, BackRoomBridge.cs:245-249.
    [Theory]
    [InlineData("breakoutdemo")]
    [InlineData("breakout")]
    public void Breakout_EndCardGetsPlansAndOpensOnlyTheNamedCheckout(string id) => Run((_, launcher) =>
    {
        launcher.Hide();
        CoreEntitlement.HasLabProvider = () => true;
        var opened = new List<string>();
        var beacons = new List<(string Url, string Body)>();
        var oldOpen = GameWindow.FunnelOpen;
        var oldSend = GameWindow.FunnelSend;
        var oldLast = GameWindow.FunnelLastOpenUtc;
        var oldOffline = CoreSettings.Current.OfflineMode;
        try
        {
            CoreSettings.Current.OfflineMode = false;
            GameWindow.FunnelLastOpenUtc = DateTime.MinValue;
            GameWindow.FunnelOpen = opened.Add;
            GameWindow.FunnelSend = (url, body) => beacons.Add((url, body));
            var window = GameWindow.Launch(id)!;
            var frames = new List<JObject>();
            window.Posted += json => frames.Add(JObject.Parse(json));
            window.Web.OnNavigationCompleted(window.PageUrl!);
            window.Web.OnWebMessage("{\"type\":\"ready\"}");
            Assert.True(JToken.DeepEquals(PlanCatalog.ToWire(PlanCatalog.Current), frames.Last()["plans"]));

            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"gate_shown\",\"a\":\"breakout-demo\",\"b\":\"prime\"}");
            Assert.Equal("gate_shown|panel|breakout_demo|prime", beacons.Single().Body);
            Assert.EndsWith("/v2/public/funnel", beacons.Single().Url);
            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"a\":\"bad|field\",\"b\":\"other\",\"plan\":\"prime\",\"url\":\"https://evil.example\"}");
            Assert.Equal(PlanCatalog.Current.Prime.PatreonUrl, Assert.Single(opened));
            Assert.Equal("cta_clicked|panel||patreon", beacons.Last().Body);
            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"b\":\"web\",\"plan\":\"basic\"}");
            Assert.Single(opened); // double click is counted but cannot open a second tab
            Assert.Equal("cta_clicked|panel||web", beacons.Last().Body);
            GameWindow.FunnelLastOpenUtc = DateTime.MinValue;
            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"b\":\"web\",\"plan\":\"basic\"}");
            Assert.Equal(PlanCatalog.WebUrlFor(PlanCatalog.Current.Basic, "panel"), opened.Last());
            GameWindow.FunnelLastOpenUtc = DateTime.MinValue;
            GameWindow.FunnelSend = (_, _) => throw new InvalidOperationException("offline");
            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"b\":\"substar\",\"plan\":\"prime\"}");
            Assert.Equal(PlanCatalog.Current.Prime.SubstarUrl, opened.Last()); // a failed count does not block the click
            GameWindow.FunnelSend = (url, body) => beacons.Add((url, body));
            var count = beacons.Count;
            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"purchase\",\"plan\":\"prime\"}");
            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"plan\":\"unknown\",\"url\":\"https://evil.example\"}");
            Assert.Equal(count + 1, beacons.Count);
            Assert.Equal(3, opened.Count);
            CoreSettings.Current.OfflineMode = true;
            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"gate_shown\"}");
            Assert.Equal(count + 1, beacons.Count);
            CoreSettings.Current.OfflineMode = false;
            window.Web.OnWebMessage("{\"type\":\"exit\"}");
            GameWindow.FunnelLastOpenUtc = DateTime.MinValue;
            window.Web.OnWebMessage("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"plan\":\"prime\"}");
            Assert.Equal(3, opened.Count);
            Assert.Equal(count + 2, beacons.Count); // still counted during exit
        }
        finally
        {
            (GameWindow.FunnelOpen, GameWindow.FunnelSend, GameWindow.FunnelLastOpenUtc) = (oldOpen, oldSend, oldLast);
            CoreSettings.Current.OfflineMode = oldOffline;
        }
    });

    // WPF FunnelPageMessage.cs:79-88: three seconds, including clock rollback.
    [Fact]
    public void Breakout_CheckoutThrottleAllowsOnePerThreeSeconds()
    {
        var old = GameWindow.FunnelLastOpenUtc;
        try
        {
            GameWindow.FunnelLastOpenUtc = DateTime.MinValue;
            var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
            Assert.True(GameWindow.TakeFunnelOpenSlot(now));
            Assert.False(GameWindow.TakeFunnelOpenSlot(now.AddSeconds(2)));
            Assert.True(GameWindow.TakeFunnelOpenSlot(now.AddSeconds(3)));
            Assert.True(GameWindow.TakeFunnelOpenSlot(now));
        }
        finally { GameWindow.FunnelLastOpenUtc = old; }
    }
}
