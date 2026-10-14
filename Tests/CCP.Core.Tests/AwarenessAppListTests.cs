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
        => Assert.Empty(AwarenessAppList.ParseAppList(input));

    [Fact]
    public void MixedSeparatorsAndExeSuffixesKeepFirstSpellingAndOrder()
    {
        var result = AwarenessAppList.ParseAppList(" Chrome.exe, discord; CHROME.EXE\nms-teams\r\nDiscord.exe ");
        Assert.Equal(new[] { "Chrome", "discord", "ms-teams" }, result);
        Assert.True(AwarenessAppList.MatchesAppList(result, "CHROME"));
        Assert.True(AwarenessAppList.MatchesAppList(result, "ms-teams"));
        Assert.False(AwarenessAppList.MatchesAppList(result, "chromedriver"));
        Assert.False(AwarenessAppList.MatchesAppList(result, "googlechrome"));
    }

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("CHROME", true)]
    [InlineData("firefox", false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    public void MatchUsesWholeProcessNamesAndNormalizesUnparsedListEntries(string process, bool expected)
    {
        Assert.Equal(expected, AwarenessAppList.MatchesAppList(new[] { "", "   ", ".exe", " Chrome.EXE " }, process));
        Assert.False(AwarenessAppList.MatchesAppList(null, process));
    }
}
