using ConditioningControlPanel.Services.Deeper;
using Xunit;
namespace CCP.Core.Tests;
public sealed class DeeperEditorIntroRulesTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, true, true, false)]
    public void CoachmarksOnlyStartBeforeFirstIntroWithoutAnotherWalkthrough(bool seen, bool active, bool pending, bool expected)
        => Assert.Equal(expected, DeeperEditorIntroRules.ShouldStart(seen, active, pending));
}
