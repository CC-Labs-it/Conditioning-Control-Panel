using ConditioningControlPanel.Services.Companion.Rain; using Xunit;
namespace CCP.Core.Tests;
public sealed class RainLookCodecTests
{
    [Fact]
    public void CanonicalLookDropsLockedItemsAndInvalidColors()
    {
        const string look = """
            {"rev":1,"items":{"hair":"original-blonde","dress":"rose-flare",
            "accessories":["hoops-original","silver-drops","hoops-original"],"bad slot":"x"},
            "colors":{"fabric":"#AA3366","hair":"invalid","unknown":"#ffffff"}}
            """;
        var actual = RainLookCodec.Normalize(look, (slot, id) => id is "rose-flare" or "silver-drops");
        Assert.Equal("""
            {"rev":1,"items":{"accessories":["hoops-original"],"hair":"original-blonde"},"colors":{"fabric":"#aa3366"}}
            """, actual);
        Assert.Equal(actual, RainLookCodec.Normalize(actual));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"rev\":2,\"items\":{\"dress\":\"x\"}}")]
    [InlineData("{\"rev\":1,\"colors\":{\"unknown\":\"#ffffff\"}}")]
    public void InvalidOrEmptyLooksUseAuthoredDefault(string? input)
        => Assert.True(RainLookCodec.IsDefault(input));
    [Fact]
    public void BoundsInputAndAccessoryList()
    {
        Assert.Equal("", RainLookCodec.Normalize(new string('x', RainLookCodec.MaxChars + 1)));
        var ids = new Newtonsoft.Json.Linq.JArray();
        for (int i = 0; i < 20; i++) ids.Add("a" + i);
        var look = new Newtonsoft.Json.Linq.JObject { ["rev"] = 1,
            ["items"] = new Newtonsoft.Json.Linq.JObject { ["accessories"] = ids } };
        var normalized = Newtonsoft.Json.Linq.JObject.Parse(RainLookCodec.Normalize(look.ToString()));
        Assert.Equal(16, ((Newtonsoft.Json.Linq.JArray)normalized["items"]!["accessories"]!).Count);
    }
}
