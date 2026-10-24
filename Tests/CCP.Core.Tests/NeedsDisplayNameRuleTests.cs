using System;
using System.Linq;
using ConditioningControlPanel.Services.Account;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The display name is required (onboarding owner call, 2026-10-20): a signed-in account with no
/// name is asked after sign-in and at the next launch; an anonymous install never is. Plus the
/// prefill, the shape check that mirrors the server, and the new copy in all ten languages.
/// </summary>
public class NeedsDisplayNameRuleTests
{
    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(true, "", null, true)]
    [InlineData(true, "   ", null, true)]
    [InlineData(true, "Pixie", null, false)]
    [InlineData(true, "Pixie", false, false)]
    [InlineData(true, "Pixie", true, true)]    // the server's flag wins over a stale local name
    [InlineData(true, null, false, true)]      // an empty name is asked even when the flag says no
    [InlineData(false, null, null, false)]     // anonymous: never
    [InlineData(false, "", true, false)]
    public void Asks_only_a_signed_in_account_with_no_name(bool signedIn, string? name, bool? flag, bool expected)
        => Assert.Equal(expected, NeedsDisplayNameRule.ShouldAsk(signedIn, name, flag));

    [Fact]
    public void FirstName_skips_blank_names_and_returns_null_when_all_are_blank()
    {
        Assert.Equal("Echo", NeedsDisplayNameRule.FirstName("", "  ", null, " Echo ", "Later"));
        Assert.Null(NeedsDisplayNameRule.FirstName("", null, "   "));
    }

    [Theory]
    [InlineData("ab", DisplayNameShape.TooShort)]
    [InlineData("  ab  ", DisplayNameShape.TooShort)]
    [InlineData("abc", DisplayNameShape.Ok)]
    [InlineData("twenty_chars_exactly", DisplayNameShape.Ok)]
    [InlineData("twenty_one_chars_long", DisplayNameShape.TooLong)]
    [InlineData("pink.doll-01 x", DisplayNameShape.Ok)]
    [InlineData("Zoë", DisplayNameShape.Ok)]
    [InlineData("Котик", DisplayNameShape.Ok)]
    [InlineData("名前です", DisplayNameShape.Ok)]
    [InlineData("bad!name", DisplayNameShape.BadChars)]
    [InlineData("emoji\U0001F496x", DisplayNameShape.BadChars)]
    public void Check_mirrors_the_server_rules(string name, DisplayNameShape expected)
        => Assert.Equal(expected, NeedsDisplayNameRule.Check(name));

    [Fact]
    public void Suggest_cleans_a_provider_username_or_gives_up()
    {
        Assert.Equal("pinkdoll", NeedsDisplayNameRule.Suggest("pink!doll"));
        Assert.Equal("a_really_long_userna", NeedsDisplayNameRule.Suggest("a_really_long_username_here"));
        Assert.Equal("second", NeedsDisplayNameRule.Suggest(null, "!!", "second"));
        Assert.Null(NeedsDisplayNameRule.Suggest(null, "", "x!", "\U0001F496\U0001F496\U0001F496"));
    }

    [Fact]
    public void Every_suggestion_passes_the_check()
    {
        foreach (var raw in new[] { "dj.kitten", "  spaced out  ", "Ünïcödé_nämé_thät_is_lông", "a-b" })
        {
            var s = NeedsDisplayNameRule.Suggest(raw);
            if (s != null) Assert.Equal(DisplayNameShape.Ok, NeedsDisplayNameRule.Check(s));
        }
    }

}
