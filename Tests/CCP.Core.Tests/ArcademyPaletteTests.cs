using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ArcademyPaletteTests
{
    [Fact]
    public void DefaultsMatchWpfAndAreIndependent()
    {
        var palette = ArcademyPalette.Resolve(null);
        Assert.True(JToken.DeepEquals(JObject.Parse("""
            {"ground":"#14142B","navy":"#1A1A2E","panel":"#252542","ink":"#F2EBDD",
             "pink":"#FF69B4","lavender":"#B8A6E8","gold":"#F0C24B"}
            """), palette));
        palette["ground"] = "changed";
        Assert.Equal("#14142B", (string?)ArcademyPalette.Resolve(null)["ground"]);
    }

    [Fact]
    public void ModOnlyOverridesKnownNonblankStringsUpTo96Characters()
    {
        var mod = new JObject { ["ground"] = "custom", ["navy"] = " ", ["panel"] = 42,
            ["ink"] = new string('x', 97), ["pink"] = new string('y', 96), ["unknown"] = "x" };
        var palette = ArcademyPalette.Resolve(mod.ToString());
        Assert.Equal("custom", (string?)palette["ground"]);
        Assert.Equal("#1A1A2E", (string?)palette["navy"]);
        Assert.Equal("#252542", (string?)palette["panel"]);
        Assert.Equal("#F2EBDD", (string?)palette["ink"]);
        Assert.Equal(new string('y', 96), (string?)palette["pink"]);
        Assert.Null(palette["unknown"]);
        Assert.Equal(7, palette.Count);
    }

    [Fact]
    public void InvalidModFallsBackToDefaults() =>
        Assert.True(JToken.DeepEquals(ArcademyPalette.Resolve(null), ArcademyPalette.Resolve("{broken")));
}
