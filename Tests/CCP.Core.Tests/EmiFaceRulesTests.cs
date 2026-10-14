using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiFaceRulesTests
{
    [Theory]
    [InlineData(":)", true, false)]
    [InlineData(":P", true, false)]
    [InlineData("0_0", false, false)]
    [InlineData("(◉_◉)", false, false)]
    [InlineData("(¬‿¬)", false, true)]
    [InlineData("♥♥♥", false, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void CanonFacesKeepTheirRotationAndKaomojiLift(string? text, bool side, bool kao)
    {
        Assert.Equal(side, EmiFaceRules.IsSide(text));
        Assert.Equal(kao, EmiFaceRules.IsKao(text));
    }
}
