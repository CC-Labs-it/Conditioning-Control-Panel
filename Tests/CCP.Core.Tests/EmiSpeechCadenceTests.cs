using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class EmiSpeechCadenceTests
{
    [Theory]
    [InlineData(0, 4050)]
    [InlineData(35, 4050)]
    [InlineData(36, 4086)]
    [InlineData(120, 9210)]
    public void LandedLineHasReadableFloorAndLinearGrowth(int length, int expected)
        => Assert.Equal(expected, EmiSpeechCadence.HoldMs(new string('x', length)));

    [Fact]
    public void NullLineUsesTheSameFloor() => Assert.Equal(4050, EmiSpeechCadence.HoldMs(null));

    [Theory]
    [InlineData(false, false, "firstContact")]
    [InlineData(false, true, "firstContact")]
    [InlineData(true, false, "desktopFirstBoot")]
    [InlineData(true, true, "summoned")]
    public void GreetingOrdersTheUnspentIntroductions(bool first, bool desktop, string expected)
        => Assert.Equal(expected, EmiSpeechCadence.Greeting(first, desktop));
}
