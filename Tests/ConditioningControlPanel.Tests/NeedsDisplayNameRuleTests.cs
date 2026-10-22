using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

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
    [InlineData(true, null, false, false)]     // the server says no: a blanked patron name is not asked again
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

    private static readonly string[] NewKeys =
    {
        "login_device_title", "login_device_body", "login_device_your_code", "login_device_open_manually",
        "login_device_copy_code", "login_device_open_again", "login_device_waiting", "login_device_generating",
        "login_device_failed", "login_device_retrying", "login_device_lost", "login_device_expired",
        "login_device_copied", "name_ask_body", "name_ask_bad_chars", "name_ask_close_tip",
        "account_name_not_set", "banner_signed_in_no_name",
    };

    [Fact]
    public void Every_new_key_is_in_all_ten_language_files_without_dashes_or_exclamations()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var json = JObject.Parse(File.ReadAllText(file));
            foreach (var key in NewKeys)
            {
                var value = (string?)json[key];
                Assert.False(string.IsNullOrWhiteSpace(value), $"{Path.GetFileName(file)} is missing {key}");
                Assert.DoesNotContain((char)0x2014, value!); // em-dash
                Assert.DoesNotContain((char)0x2013, value!); // en-dash
                Assert.DoesNotContain('!', value!);
            }
        }
    }

    [Fact]
    public void The_sign_in_and_profile_load_paths_both_reach_the_ask()
    {
        var mw = Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow");
        Assert.Contains("AskDisplayNameAfterSignIn(", File.ReadAllText(Path.Combine(mw, "MainWindow.Login.cs")));
        Assert.Contains("QueueDisplayNameAskIfNeeded();", File.ReadAllText(Path.Combine(mw, "MainWindow.xaml.cs")));
        Assert.DoesNotContain("?? \"User\"", File.ReadAllText(Path.Combine(mw, "MainWindow.Login.cs")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
