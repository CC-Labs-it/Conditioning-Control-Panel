using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class IntakeNudgeRuleTests
{
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void EachReasonIndependentlyStandsDown(int reason, bool expected)
        => Assert.Equal(expected, IntakeNudgeRule.ShouldOffer(reason != 0, reason != 1,
            reason == 2 ? "2026-W44" : "2026-W43", "2026-W44", reason == 3,
            reason == 4, reason == 5, reason == 6, reason == 7));
}
