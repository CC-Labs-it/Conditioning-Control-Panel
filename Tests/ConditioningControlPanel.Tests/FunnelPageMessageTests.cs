using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Funnel;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Host side of the game-page end cards (contract C10): a page's <c>funnel</c> message is counted,
/// a cta_clicked naming a plan opens that plan's checkout (never a page-supplied url), opens are
/// throttled and refused when the host says no, DtRH's payout-result carries plan + daily_gift,
/// and the three hosts forward the message and send the plan list at init.
/// </summary>
[Collection("FunnelBeacon")]
public class FunnelPageMessageTests
{
    private static JObject Msg(string json) => JObject.Parse(json);

    [Fact]
    public void OnlyFunnelMessagesWithACountedEventAreRead()
    {
        Assert.Null(FunnelPageMessage.Read(Msg("{\"type\":\"pong\"}")));
        Assert.Null(FunnelPageMessage.Read(Msg("{\"type\":\"funnel\",\"event\":\"purchase\"}")));
        Assert.Null(FunnelPageMessage.Read(null));
        var r = FunnelPageMessage.Read(Msg("{\"type\":\"funnel\",\"event\":\"gate_shown\",\"a\":\"breakout\",\"b\":\"prime\"}"))!;
        Assert.Equal(("gate_shown", "breakout", "prime"), (r.Event, r.A, r.B));
        Assert.Null(r.Url);
    }

    [Theory]
    [InlineData("patreon", "prime", "https://www.patreon.com/checkout/CodeBambi?rid=27681505")]
    [InlineData("", "prime", "https://www.patreon.com/checkout/CodeBambi?rid=27681505")]
    [InlineData("patreon", "basic", "https://www.patreon.com/checkout/CodeBambi?rid=27604786")]
    [InlineData("substar", "basic", "https://subscribestar.adult/cc-labs")]
    [InlineData("web", "basic", "https://app.cclabs.app/subscribe?plan=basic&from=panel")]
    [InlineData("card", "prime", "https://www.patreon.com/checkout/CodeBambi?rid=27681505")]
    public void ACtaNamingAPlanOpensThatPlansCheckout(string rail, string plan, string url)
    {
        var r = FunnelPageMessage.Read(Msg(
            "{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"a\":\"goon_host\",\"b\":\"" + rail + "\",\"plan\":\"" + plan + "\"}"),
            PlanCatalog.Fallback)!;
        Assert.Equal(url, r.Url);
        // The counted rail is the one actually opened: an unknown rail opens Patreon and counts as patreon.
        Assert.Equal(rail is "web" or "substar" ? rail : "patreon", r.B);
    }

    [Fact]
    public void APageNeverSuppliesTheUrl()
    {
        var r = FunnelPageMessage.Read(Msg(
            "{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"a\":\"x\",\"b\":\"patreon\",\"plan\":\"lab\",\"url\":\"https://evil.example\"}"),
            PlanCatalog.Fallback)!;
        Assert.Null(r.Url);
        var gate = FunnelPageMessage.Read(Msg(
            "{\"type\":\"funnel\",\"event\":\"gate_shown\",\"a\":\"x\",\"b\":\"prime\",\"plan\":\"prime\"}"), PlanCatalog.Fallback)!;
        Assert.Null(gate.Url);
    }

    [Fact]
    public async Task HandleCountsOpensOnceAndHonoursMayOpen()
    {
        FunnelPageMessage.Reset();
        var opened = new List<string>();
        var sent = new List<string>();
        FunnelPageMessage.OpenOverride = opened.Add;
        FunnelBeacon.BaseUrlOverride = "https://proxy.test";
        FunnelBeacon.SendOverride = (_, body) => { lock (sent) sent.Add(body); return Task.CompletedTask; };
        try
        {
            var cta = Msg("{\"type\":\"funnel\",\"event\":\"cta_clicked\",\"a\":\"dtrh\",\"b\":\"patreon\",\"plan\":\"prime\"}");
            FunnelPageMessage.Handle(cta, mayOpen: false);
            Assert.Empty(opened);
            FunnelPageMessage.Handle(cta);
            FunnelPageMessage.Handle(cta);   // double-fired click: throttled
            Assert.Single(opened);
            Assert.EndsWith("rid=27681505", opened[0]);
            for (var i = 0; i < 50; i++) { lock (sent) if (sent.Count >= 3) break; await Task.Delay(20); }
            lock (sent)
            {
                Assert.Equal(3, sent.Count);
                Assert.All(sent, s => Assert.Equal("cta_clicked|panel|dtrh|patreon", s));
            }
        }
        finally
        {
            FunnelPageMessage.Reset();
            FunnelBeacon.BaseUrlOverride = null;
            FunnelBeacon.SendOverride = null;
        }
    }

    [Fact]
    public void TheThrottleAllowsOnePerWindow()
    {
        FunnelPageMessage.Reset();
        var t = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(FunnelPageMessage.TakeOpenSlot(t));
        Assert.False(FunnelPageMessage.TakeOpenSlot(t.AddSeconds(1)));
        Assert.True(FunnelPageMessage.TakeOpenSlot(t.AddSeconds(4)));
        FunnelPageMessage.Reset();
    }

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, true, false)]    // a Prime grant of its own
    [InlineData(false, false, false, false)]  // not a gift day
    [InlineData(true, false, true, false)]    // capture rig
    public void ADtrhRunCameFromTheDailyGift(bool testMode, bool hasLab, bool freeToday, bool expected)
        => Assert.Equal(expected, DtrhHostService.RunCameFromDailyGift(testMode, hasLab, freeToday));

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    [Fact]
    public void DtrhPayoutResultCarriesTheEndCardFields()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "Services", "Chaos", "DtrhHostService.cs"));
        var at = src.IndexOf("type = \"payout-result\"", StringComparison.Ordinal);
        Assert.True(at > 0);
        var body = src.Substring(at, 700);
        Assert.Contains("plan = dailyGift ? \"prime\" : null", body);
        Assert.Contains("daily_gift = dailyGift", body);
    }

    [Theory]
    [InlineData("Services/Chaos/DtrhHostService.cs")]
    [InlineData("Services/BackRoom/BackRoomBridge.cs")]
    [InlineData("Services/GoonGame/GoonHostService.cs")]
    public void EveryEndCardHostForwardsTheFunnelMessage(string file)
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), file));
        Assert.Contains("FunnelPageMessage.Type", src);
        Assert.Contains("FunnelPageMessage.Handle(", src);
    }

    [Theory]
    [InlineData("Services/Chaos/DtrhHostService.cs")]
    [InlineData("Services/BackRoom/BackRoomHostService.cs")]
    [InlineData("Services/GoonGame/GoonHostService.cs")]
    public void EveryEndCardHostSendsThePlanListAtInit(string file)
        => Assert.Contains("plans = Services.Vault.PlanCatalog.ToWire(".Replace("Services.", file.Contains("GoonGame") ? "Services." : ""),
            File.ReadAllText(Path.Combine(AppDir(), file)));
}
