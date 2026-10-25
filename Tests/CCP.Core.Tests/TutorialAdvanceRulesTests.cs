using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class TutorialAdvanceRulesTests
{
    [Theory]
    [InlineData(" Pulse curve ", "pulse", true)]
    [InlineData("PULSE", "pulse", true)]
    [InlineData("15.49", "15", true)]
    [InlineData("15.5", "15", false)]
    [InlineData("14.5", "15", false)]
    [InlineData("0.70", "0.7", true)]
    [InlineData("115", "15", false)]
    [InlineData("anything", " ", false)]
    [InlineData(null, "pulse", false)]
    [InlineData("pulse", null, false)]
    public void MatchesWpfTextNumericToleranceAndSubstringRules(string? actual, string? expected, bool matches) =>
        Assert.Equal(matches, TutorialAdvanceRules.TextMatches(actual, expected));
}
