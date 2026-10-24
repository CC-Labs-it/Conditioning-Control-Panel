using Xunit;

namespace ConditioningControlPanel.Tests;

public class BannerSparkleLifetimeTests
{
    [Theory]
    [InlineData(1, 1, false, true, true)]
    [InlineData(1, 2, false, true, false)]
    [InlineData(1, 2, false, false, false)]
    [InlineData(2, 2, true, true, false)]
    [InlineData(2, 2, false, false, false)]
    public void CompletionRemovesOnlyALiveSpriteFromTheCurrentRun(
        long spriteRun, long currentRun, bool clearing, bool present, bool expected)
        => Assert.Equal(expected, BannerFxRules.CanRemoveSparkle(spriteRun, currentRun, clearing, present));
}
