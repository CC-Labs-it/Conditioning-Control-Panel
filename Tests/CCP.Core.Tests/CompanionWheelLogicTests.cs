using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CompanionWheelLogicTests
{
    [Theory]
    [InlineData(0, 0, -1, true)]
    [InlineData(-10, 0, 1, true)]
    [InlineData(100, 50, 0, false)]
    [InlineData(0, 0, 0, false)]
    [InlineData(100, 99.5, -1, true)]
    [InlineData(100, 99.49, -1, false)]
    [InlineData(100, 100, 1, false)]
    [InlineData(100, 0.5, 1, true)]
    [InlineData(100, 0.51, 1, false)]
    [InlineData(100, 0, -1, false)]
    [InlineData(100, 50, -0.1, false)]
    public void OnlySpentVerticalNotchesLeaveTheInnerList(double height, double offset, double delta, bool expected)
        => Assert.Equal(expected, CompanionWheelLogic.ShouldForward(height, offset, delta));
}
