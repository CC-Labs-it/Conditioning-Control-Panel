using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Core.Tests;

public sealed class AwarenessAppScopeRulesTests
{
    [Fact]
    public void MixedSeparatorsExeSuffixAndDuplicatesKeepFirstSpellingAndOrder() =>
        Assert.Equal(new[] { "Chrome", "DISCORD", "ms-teams", "zoom", "slack" },
            AwarenessAppScopeRules.ParseAppList("  Chrome.exe , DISCORD.EXE; chrome\nms-teams\r\nzoom;slack"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(", ; .exe\r\n")]
    public void EmptyAppListsAreValidAndContainNoWildcard(string? text) =>
        Assert.Empty(AwarenessAppScopeRules.ParseAppList(text));

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("CHROME", true)]
    [InlineData("chromedriver", false)]
    [InlineData("googlechrome", false)]
    [InlineData("", false)]
    public void MatchingUsesWholeProcessNamesWithTolerantListEntries(string process, bool expected) =>
        Assert.Equal(expected, AwarenessAppScopeRules.MatchesAppList(new[] { "", " .exe ", " Chrome.EXE " }, process));

    [Theory]
    [InlineData(AwarenessAppScope.Everywhere, false, false, false, "", null)]
    [InlineData(AwarenessAppScope.ExceptListed, false, false, true, "", null)]
    [InlineData(AwarenessAppScope.OnlyListed, false, false, false, "chrome", "foreground window could not be identified")]
    [InlineData(AwarenessAppScope.Everywhere, true, true, true, "chrome", "Control Panel itself has focus")]
    [InlineData(AwarenessAppScope.OnlyListed, true, true, true, "chrome", "Control Panel itself has focus")]
    [InlineData(AwarenessAppScope.OnlyListed, true, true, false, "chrome", null)]
    [InlineData(AwarenessAppScope.OnlyListed, true, false, true, "chrome", null)]
    [InlineData(AwarenessAppScope.OnlyListed, true, false, true, "chromedriver", "'chromedriver' is not on the allow list")]
    [InlineData(AwarenessAppScope.ExceptListed, true, false, false, "chrome", "'chrome' is on the block list")]
    [InlineData(AwarenessAppScope.ExceptListed, true, false, false, "firefox", null)]
    public void ForegroundPolicyPreservesUnknownOwnFocusAndAllowOrBlockSemantics(
        AwarenessAppScope scope, bool known, bool own, bool ignoreOwn, string process, string? reason) =>
        Assert.Equal(reason, AwarenessAppScopeRules.SuppressionReason(scope, new[] { "Chrome.exe" }, process, known, own, ignoreOwn));

    [Fact]
    public void OnlyListedWithoutAnyAppsSuppressesKnownAndUnknownWindows()
    {
        Assert.NotNull(AwarenessAppScopeRules.SuppressionReason(AwarenessAppScope.OnlyListed, null, "chrome", true, false, false));
        Assert.NotNull(AwarenessAppScopeRules.SuppressionReason(AwarenessAppScope.OnlyListed, null, "", false, false, false));
        Assert.False(AwarenessAppScopeRules.MatchesAppList(null, "chrome"));
    }
}
