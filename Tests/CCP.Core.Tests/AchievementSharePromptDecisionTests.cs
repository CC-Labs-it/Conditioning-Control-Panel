using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AchievementSharePromptDecisionTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void ExistingConsentWinsAndUserRequestBypassesScreenOwners(bool game, bool launcher, bool visible)
    {
        Assert.Equal(AchievementSharePromptRouting.Skip,
            AchievementSharePromptDecision.Decide(true, game, launcher, visible, userAsked: true));
        Assert.Equal(AchievementSharePromptRouting.Ask,
            AchievementSharePromptDecision.Decide(false, game, launcher, visible, userAsked: true));
        Assert.Equal(game || launcher || !visible ? AchievementSharePromptRouting.Inbox : AchievementSharePromptRouting.Ask,
            AchievementSharePromptDecision.Decide(false, game, launcher, visible));
    }
}
