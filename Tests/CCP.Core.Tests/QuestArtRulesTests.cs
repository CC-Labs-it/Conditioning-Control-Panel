using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class QuestArtRulesTests
{
    [Theory]
    [InlineData("logo.png", true, false, "logo2.png")]
    [InlineData("logo.png", false, true, "logo2.png")]
    [InlineData("logo.png", false, false, null)]
    [InlineData("features/bambi takeover.png", true, false, "features/mandatory_videos.png")]
    [InlineData("features/bambi takeover.png", false, true, null)]
    [InlineData("quests/pop_parade_d.png", true, true, null)]
    [InlineData("LOGO.png", true, true, null)]
    public void ModeReplacementMatchesTheWpfBuiltInRule(string resource, bool sissy, bool ccpDefault, string? expected)
        => Assert.Equal(expected, QuestArtRules.ModeReplacement(resource, sissy, ccpDefault));
}
