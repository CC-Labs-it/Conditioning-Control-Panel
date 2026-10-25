using ConditioningControlPanel.Services.Privacy;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AchievementSharePromptRuleTests
{
    [Theory]
    [InlineData(true, false, false, true, false, AchievementSharePromptRouting.Skip)]
    [InlineData(true, true, true, false, true, AchievementSharePromptRouting.Skip)]
    [InlineData(false, false, false, true, false, AchievementSharePromptRouting.Ask)]
    [InlineData(false, true, false, true, false, AchievementSharePromptRouting.Inbox)]
    [InlineData(false, false, true, true, false, AchievementSharePromptRouting.Inbox)]
    [InlineData(false, false, false, false, false, AchievementSharePromptRouting.Inbox)]
    [InlineData(false, true, true, false, true, AchievementSharePromptRouting.Ask)]
    public void WpfRouting(bool sharing, bool game, bool launcher, bool visible, bool asked,
        AchievementSharePromptRouting expected) =>
        Assert.Equal(expected, AchievementSharePromptRule.Decide(sharing, game, launcher, visible, asked));
}
