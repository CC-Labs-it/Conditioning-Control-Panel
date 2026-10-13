using System.Threading.Tasks;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The plan list (contract C1): the compiled fallback equals the server defaults, a server body is
/// read strictly (one odd field and the fallback stands), links are https on known hosts only, the
/// site link carries from=panel, and the cache answers without a second read.
/// </summary>
[Collection("PlanCatalog")]
public class PlanCatalogTests
{
    private const string C1 = """
    {
      "currency": "USD",
      "basic": { "name": "Basic", "tier": 1, "monthly": "7.50", "monthly_eur": "6.00", "yearly": "72.00",
                 "patreon_url": "https://www.patreon.com/checkout/CodeBambi?rid=27604786",
                 "substar_url": "https://subscribestar.adult/cc-labs",
                 "web_url": "https://app.cclabs.app/subscribe?plan=basic" },
      "prime": { "name": "Prime", "tier": 2, "monthly": "12.50", "monthly_eur": "10.00", "yearly": "120.00",
                 "patreon_url": "https://www.patreon.com/checkout/CodeBambi?rid=27681505",
                 "substar_url": "https://subscribestar.adult/cc-labs",
                 "web_url": "https://app.cclabs.app/subscribe?plan=prime" },
      "yearly_note": "20% off",
      "sale": null
    }
    """;

    [Fact]
    public void TheFallbackIsTheContractDefaults()
    {
        var parsed = PlanCatalog.Parse(C1)!;
        Assert.True(parsed.FromServer);
        Assert.False(PlanCatalog.Fallback.FromServer);
        Assert.Equal(PlanCatalog.Fallback.Currency, parsed.Currency);
        Assert.Equal(PlanCatalog.Fallback.Basic, parsed.Basic);
        Assert.Equal(PlanCatalog.Fallback.Prime, parsed.Prime);
        Assert.Null(parsed.Sale);
    }

    [Fact]
    public void ButtonsTargetTheCheckoutThatPreselectsTheTier()
    {
        Assert.EndsWith("rid=27604786", PlanCatalog.Fallback.For(1).PatreonUrl);
        Assert.EndsWith("rid=27681505", PlanCatalog.Fallback.For(2).PatreonUrl);
        Assert.Same(PlanCatalog.Fallback.Basic, PlanCatalog.Fallback.For(0));
        Assert.Same(PlanCatalog.Fallback.Prime, PlanCatalog.Fallback.For(3));
        Assert.Same(PlanCatalog.Fallback.Prime, PlanCatalog.Fallback.ForKey("Prime"));
        Assert.Null(PlanCatalog.Fallback.ForKey("lab"));
    }

    [Fact]
    public void TheSiteLinkIsTaggedFromPanel()
    {
        Assert.Equal("https://app.cclabs.app/subscribe?plan=basic&from=panel", PlanCatalog.WebUrlFor(PlanCatalog.Fallback.Basic));
        var bare = PlanCatalog.Fallback.Prime with { WebUrl = "https://app.cclabs.app/subscribe" };
        Assert.Equal("https://app.cclabs.app/subscribe?from=panel", PlanCatalog.WebUrlFor(bare));
    }

    [Fact]
    public void ASaleRidesOnThePlanList()
    {
        var body = C1.Replace("\"sale\": null", "\"sale\": {\"active\":true,\"percent\":50,\"tiers\":[\"prime\"]}");
        var sale = PlanCatalog.Parse(body)!.Sale!;
        Assert.Equal(50, sale.Percent);
        Assert.Contains(2, sale.Tiers);
    }

    [Theory]
    [InlineData("\"currency\": \"USD\"", "\"currency\": \"GBP\"")]
    [InlineData("\"monthly\": \"7.50\"", "\"monthly\": \"free\"")]
    [InlineData("\"monthly\": \"7.50\"", "\"monthly\": \"0\"")]
    [InlineData("\"yearly\": \"72.00\"", "\"yearly\": null")]
    [InlineData("https://www.patreon.com/checkout/CodeBambi?rid=27604786", "http://www.patreon.com/checkout/CodeBambi?rid=27604786")]
    [InlineData("https://www.patreon.com/checkout/CodeBambi?rid=27604786", "https://evil.example/checkout")]
    [InlineData("https://subscribestar.adult/cc-labs", "javascript:alert(1)")]
    [InlineData("https://app.cclabs.app/subscribe?plan=basic", "https://user@app.cclabs.app/subscribe")]
    public void AnOddFieldMeansTheFallback(string from, string to)
    {
        var at = C1.IndexOf(from, System.StringComparison.Ordinal);
        Assert.True(at >= 0);
        var body = C1.Substring(0, at) + to + C1.Substring(at + from.Length);
        Assert.Null(PlanCatalog.Parse(body));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void JunkMeansTheFallback(string? body) => Assert.Null(PlanCatalog.Parse(body));

    [Fact]
    public void NumbersAreAcceptedAsWellAsStrings()
    {
        Assert.Equal(750, PlanCatalog.Cents(new JValue(7.5)));
        Assert.Equal(1250, PlanCatalog.Cents(new JValue("12.50")));
        Assert.Null(PlanCatalog.Cents(new JValue("-1")));
        Assert.Null(PlanCatalog.Cents(new JValue(5000)));
    }

    [Fact]
    public void ThePageWireIsC1WithTaggedLinks()
    {
        var wire = PlanCatalog.ToWire(PlanCatalog.Fallback);
        Assert.Equal("USD", (string?)wire["currency"]);
        Assert.Equal("7.50", (string?)wire["basic"]!["monthly"]);
        Assert.Equal("10.00", (string?)wire["prime"]!["monthly_eur"]);
        Assert.Equal("120.00", (string?)wire["prime"]!["yearly"]);
        Assert.Equal(2, (int?)wire["prime"]!["tier"]);
        Assert.Equal("https://app.cclabs.app/subscribe?plan=prime&from=panel", (string?)wire["prime"]!["web_url"]);
        Assert.Equal("20% off", (string?)wire["yearly_note"]);
        Assert.Equal(JTokenType.Null, wire["sale"]!.Type);
    }

    [Fact]
    public async Task TheCacheAnswersWithoutASecondRead()
    {
        PlanCatalog.Reset();
        var reads = 0;
        PlanCatalog.FetchOverride = url =>
        {
            reads++;
            Assert.Equal("https://proxy.test/config/plans", url);
            return Task.FromResult<string?>(C1.Replace("7.50", "8.00"));
        };
        try
        {
            var first = await PlanCatalog.GetAsync("https://proxy.test/");
            var second = await PlanCatalog.GetAsync("https://proxy.test");
            Assert.Equal(1, reads);
            Assert.Equal(800, first.Basic.MonthlyCents);
            Assert.Same(first, second);
            Assert.Same(first, PlanCatalog.Current);
        }
        finally { PlanCatalog.Reset(); }
    }

    [Fact]
    public async Task AFailedReadKeepsTheFallback()
    {
        PlanCatalog.Reset();
        PlanCatalog.FetchOverride = _ => Task.FromResult<string?>(null);
        try
        {
            var set = await PlanCatalog.GetAsync("https://proxy.test");
            Assert.Same(PlanCatalog.Fallback, set);
        }
        finally { PlanCatalog.Reset(); }
    }
}
