using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class QuestStampRulesTests
{
    [Theory]
    [InlineData(-2, 10, false, 0, -2, 12)]
    [InlineData(4, 10, false, .4, 4, 6)]
    [InlineData(20, 10, false, 1, 20, 0)]
    [InlineData(2, 10, true, 1, 10, 0)]
    [InlineData(20, 10, true, 1, 20, 0)]
    [InlineData(2, 0, false, 0, 2, 0)]
    public void ProgressClampsFillAndCompletedCounters(int current, int target, bool done, double fraction, int display, int remaining) =>
        Assert.Equal((fraction, display, remaining), QuestStampRules.Progress(current, target, done));

    [Theory]
    [InlineData("quest_custom_name", "Server title")]
    [InlineData(" ", "Server title")]
    [InlineData("Translated title", "Translated title")]
    public void MissingTranslationsUseTheDefinition(string localized, string expected) =>
        Assert.Equal(expected, QuestStampRules.Text(localized, "Server title"));

    [Theory]
    [InlineData("logo.png", true, false, "logo2.png")]
    [InlineData("logo.png", false, true, "logo2.png")]
    [InlineData("features/bambi takeover.png", true, false, "features/mandatory_videos.png")]
    [InlineData("features/quest.png", true, true, null)]
    public void LegacyArtChoosesOnlyTheWpfBuiltInSubstitutions(string resource, bool sissy, bool ccpDefault, string? expected) =>
        Assert.Equal(expected, QuestStampRules.LegacyImageOverride(resource, sissy, ccpDefault));
}
