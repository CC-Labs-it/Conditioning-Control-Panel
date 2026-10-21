using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AwarenessAppListTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ;,\n\r .exe ; .EXE ")]
    public void EmptyEntriesCannotBecomeScopeRules(string? input)
        => Assert.Empty(AwarenessAppScopeRules.ParseAppList(input));

    [Fact]
    public void MixedSeparatorsAndExeSuffixesKeepFirstSpellingAndOrder()
    {
        var result = AwarenessAppScopeRules.ParseAppList(" Chrome.exe, discord; CHROME.EXE\nms-teams\r\nDiscord.exe ");
        Assert.Equal(new[] { "Chrome", "discord", "ms-teams" }, result);
        Assert.True(AwarenessAppScopeRules.MatchesAppList(result, "CHROME"));
        Assert.True(AwarenessAppScopeRules.MatchesAppList(result, "ms-teams"));
        Assert.False(AwarenessAppScopeRules.MatchesAppList(result, "chromedriver"));
        Assert.False(AwarenessAppScopeRules.MatchesAppList(result, "googlechrome"));
    }

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("CHROME", true)]
    [InlineData("firefox", false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    public void MatchUsesWholeProcessNamesAndNormalizesUnparsedListEntries(string process, bool expected)
    {
        Assert.Equal(expected, AwarenessAppScopeRules.MatchesAppList(new[] { "", "   ", ".exe", " Chrome.EXE " }, process));
        Assert.False(AwarenessAppScopeRules.MatchesAppList(null, process));
    }
}
