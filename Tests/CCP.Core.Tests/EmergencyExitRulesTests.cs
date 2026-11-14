using System;
using ConditioningControlPanel.Services;
using Xunit;
using ConditioningControlPanel.Localization;

namespace CCP.Core.Tests;
public class EmergencyExitRulesTests
{
    [Fact]
    public void PicksNeverRepeatAndAllGamesAreReachable()
    {
        var random = new Random(42);
        var seen = new System.Collections.Generic.HashSet<string>();
        var last = "";
        for (var i = 0; i < 100; i++)
        {
            var next = EmergencyExitRules.Pick(last, random);
            Assert.NotEqual(last, next);
            seen.Add(next); last = next;
        }
        Assert.Equal(4, seen.Count);
    }
    [Theory]
    [InlineData("builtin-bambisleep", "ignored", "ignored", "good girl", "Bambi")]
    [InlineData("builtin-sissyhypno", null, null, "sissy", "sissy")]
    [InlineData("builtin-locked", null, null, "pet", "pet")]
    [InlineData("", "ignored", "ignored", VocabTokens.VanillaPetName, VocabTokens.VanillaPetName)]
    [InlineData("builtin-ccp-default", "ignored", "ignored", VocabTokens.VanillaPetName, VocabTokens.VanillaPetName)]
    [InlineData("creator", "  Mod Name  ", "  pet name  ", "pet name", "  Mod Name  ")]
    [InlineData("creator", null, null, VocabTokens.VanillaPetName, VocabTokens.VanillaPetName)]
    [InlineData("creator", "  ", "  ", VocabTokens.VanillaPetName, VocabTokens.VanillaPetName)]
    public void AddressPreservesWpfOverridesAndFallbacks(string id, string? name, string? petName, string honorific, string subject) =>
        Assert.Equal((honorific, subject), EmergencyExitRules.Address(id, name, petName));

    [Theory]
    [InlineData("labyrinth", "completed", .999, true)]
    [InlineData("password", "completed", .33, false)]
    [InlineData("jigsaw", "completed", .329, true)]
    [InlineData("captcha", "failed", .99, true)]
    [InlineData("password", null, .99, true)]
    public void HostOwnsTheVerdict(string game, string? result, double roll, bool back) =>
        Assert.Equal(back, EmergencyExitRules.SendBack(game, result, roll));
}
