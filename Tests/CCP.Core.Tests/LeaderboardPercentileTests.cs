using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class LeaderboardPercentileTests
{
    private static readonly LeaderboardEntryData[] Entries =
    {
        new() { UnifiedId = "other", DisplayName = "Other" },
        new() { UnifiedId = "me", DiscordId = "42", DisplayName = "Bambi" }
    };

    [Theory]
    [InlineData("me", null, null)]
    [InlineData(null, "42", null)]
    [InlineData(null, null, "bAMBi")]
    public void LocalBoardFindsEachIdentityWhenServerOmitsRank(string? unified, string? discord, string? name) =>
        Assert.Equal(2, LeaderboardClient.PlayerPercentile(null, null, Entries, 100, unified, discord, name));

    [Fact]
    public void ServerRankWinsAndPercentilesClamp()
    {
        Assert.Equal(70, LeaderboardClient.PlayerPercentile(70, 100, Entries, 100, "me", null, null));
        Assert.Equal(99, LeaderboardClient.PlayerPercentile(100, 100, Entries, 100, "me", null, null));
        Assert.Equal(1, LeaderboardClient.PlayerPercentile(1, 1000, Entries, 100, "me", null, null));
        Assert.Equal(0, LeaderboardClient.PlayerPercentile(null, null, Entries, 100, "missing", null, null));
        Assert.Equal(0, LeaderboardClient.PlayerPercentile(null, null, Entries, 0, "me", null, null));
        Assert.Equal(0, LeaderboardClient.PlayerPercentile(null, null, Array.Empty<LeaderboardEntryData>(), 100, "me", null, null));
    }

    [Fact]
    public void XpChipTooltipsPreferTheModAndUnknownSourcesHaveNoTooltip()
    {
        var source = ConditioningControlPanel.Localization.Loc.Get("skill_mult_sparkle_boost");
        Assert.Equal("Skill tree bonus: +10% XP from Sparkle Boost", SkillTreeRules.GetBonusChipTooltip(source));
        Assert.Equal("Custom boost", SkillTreeRules.GetBonusChipTooltip(source,
            id => id == "sparkle_boost_1" ? "Custom boost" : null));
        Assert.Null(SkillTreeRules.GetBonusChipTooltip("Unknown source"));
    }

    [Theory]
    [InlineData(BuiltInMods.CCPDefaultId, true)]
    [InlineData(BuiltInMods.BambiSleepId, true)]
    [InlineData(BuiltInMods.SissyHypnoId, true)]
    [InlineData(BuiltInMods.DronificationId, false)]
    [InlineData(BuiltInMods.LockedId, false)]
    [InlineData("community", false)]
    [InlineData(null, false)]
    public void JournalSupportsOnlyTheThreeBimboficationMods(string? id, bool supported) =>
        Assert.Equal(supported, BuiltInMods.SupportsBimboJournal(id));
}
