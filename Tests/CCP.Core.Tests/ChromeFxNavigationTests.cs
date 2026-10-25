using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChromeFxNavigationTests
{
    [Theory]
    [InlineData("settings", "quests", 12, 0)]
    [InlineData("play", "presets", -12, 0)]
    [InlineData("quests", "quests", 0, 12)]
    [InlineData("missing", "quests", 0, 12)]
    public void EntranceFollowsRailOrderOrRisesWithoutADirection(string from, string to, double x, double y)
        => Assert.Equal((x, y), TabEntranceRule.EntranceOffset(from, to, 12));

    [Theory]
    [InlineData("lab", "play")]
    [InlineData("progression", "settings")]
    [InlineData("PLAY", "play")]
    public void LegacyAliasesKeepTheirNavigationPosition(string alias, string key)
        => Assert.Equal(TabEntranceRule.IndexOf(key), TabEntranceRule.IndexOf(alias));

    [Theory]
    [InlineData("settings", true)]
    [InlineData("progression", true)]
    [InlineData("gradedintake", true)]
    [InlineData("spiral", true)]
    [InlineData("quests", false)]
    public void NativeBrowserPagesAreExemptFromSlideAndStagger(string key, bool expected)
        => Assert.Equal(expected, TabEntranceRule.IsAirspaceTab(key));
}
