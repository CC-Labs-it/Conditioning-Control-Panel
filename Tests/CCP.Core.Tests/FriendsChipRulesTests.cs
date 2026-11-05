using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FriendsChipRulesTests
{
    [Theory]
    [InlineData(-1, 0)] [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(2, 2)] [InlineData(7, 2)]
    public void RailPlateClampsProviderTier(int tier, int expected) => Assert.Equal(expected, FriendsDrawerRules.DisplayTier(tier));

    [Theory]
    [InlineData(MotionLevel.Off, 0)] [InlineData(MotionLevel.Reduced, .5)] [InlineData(MotionLevel.Full, 1)]
    public void CountBumpRespectsMotionSetting(MotionLevel level, double expected) => Assert.Equal(expected, FriendsDrawerRules.MotionAmount(level));
}
