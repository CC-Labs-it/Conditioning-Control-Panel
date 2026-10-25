using System;
using System.Linq;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ModAdvancedRulesTests
{
    [Fact]
    public void LayoutRejectsInvalidNumbersAndKeepsExplicitZero()
    {
        Assert.Null(ModAdvancedRules.TubeLayout("bad", "", "NaN", null, "1.5"));
        var layout = ModAdvancedRules.TubeLayout("0", "-1001", "9", "-501", "501")!;
        Assert.Equal(0, layout.AvatarOffsetX);
        Assert.Equal(-1000, layout.AvatarDetachedOffsetX);
        Assert.Equal(3, layout.AvatarScale);
        Assert.Equal(-500, layout.AvatarOffsetY);
        Assert.Equal(500, layout.AvatarDetachedOffsetY);
    }

    [Fact]
    public void TooltipLimitsSkipBlanksAndAllowUpdatingExistingKeysAtCap()
    {
        var rows = Enumerable.Range(0, 31).Select(i => ((string?)$"key{i}", (string?)new string('a', 501)))
            .Concat(new[] { ((string?)"key0", (string?)" last "), ("blank", " "), (" ", "text") });
        var result = ModAdvancedRules.Tooltips(rows)!;
        Assert.Equal(30, result.Count);
        Assert.Equal("last", result["key0"]);
        Assert.Equal(500, result["key29"].Length);
        Assert.False(result.ContainsKey("key30"));
        Assert.Null(ModAdvancedRules.Tooltips(new[] { ((string?)"", (string?)"value") }));
        Assert.Null(ModAdvancedRules.Text(" ", 200));
    }

    [Theory]
    [InlineData("NaN", null)]
    [InlineData("Infinity", null)]
    [InlineData("1,25", null)]
    [InlineData("0.1", 0.5)]
    [InlineData("10", 1.5)]
    [InlineData("0.7543", 0.754)]
    public void BubbleScaleIsFiniteInvariantAndRounded(string text, double? expected)
    {
        Assert.Equal(expected, ModBubbleScaleRules.Parse(text));
        Assert.Equal(expected, ModBubbleScaleRules.Parse(ModBubbleScaleRules.Format(expected)));
    }
}
