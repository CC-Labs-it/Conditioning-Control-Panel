using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1547 (7.2.1, Bambi mod): "Strict lock on lock card doesn't work - always allows me to
/// press escape". By design: Escape is the default panic key and an open lock card is the first
/// thing panic ends (hard rule 6). The fix is copy, so the confirm and the card stop promising that
/// Escape does nothing while Escape is the panic key.
/// </summary>
public class LockCardStrictCopyTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static readonly string[] NewKeys =
    {
        LockCardStrictCopy.HintPanicIsEscape,
        LockCardStrictCopy.ConfirmTitle,
        LockCardStrictCopy.ConfirmBody,
        LockCardStrictCopy.ConfirmBodyPanicIsEscape,
        "tooltip_cannot_escape_must_complete_typing",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static JObject Lang(string lang) =>
        JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json")));

    [Theory]
    [InlineData("Escape", true)]
    [InlineData(" escape ", true)]
    [InlineData("F8", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Panic_is_escape_reads_the_binding(string? key, bool expected)
        => Assert.Equal(expected, LockCardStrictCopy.PanicIsEscape(key));

    [Fact]
    public void A_card_esc_closes_keeps_the_esc_hint_whatever_the_panic_key()
    {
        Assert.Equal(LockCardStrictCopy.HintEscCloses, LockCardStrictCopy.HintKey(true, "Escape"));
        Assert.Equal(LockCardStrictCopy.HintEscCloses, LockCardStrictCopy.HintKey(true, "F8"));
    }

    [Fact]
    public void A_strict_card_with_escape_as_panic_says_escape_still_ends_it()
        => Assert.Equal(LockCardStrictCopy.HintPanicIsEscape, LockCardStrictCopy.HintKey(false, "Escape"));

    [Fact]
    public void A_strict_card_with_another_panic_key_names_that_key()
        => Assert.Equal(LockCardStrictCopy.HintOnlyPanicKey, LockCardStrictCopy.HintKey(false, "F8"));

    [Fact]
    public void The_strict_confirm_follows_the_panic_binding()
    {
        Assert.Equal(LockCardStrictCopy.ConfirmBodyPanicIsEscape, LockCardStrictCopy.ConfirmBodyKey("Escape"));
        Assert.Equal(LockCardStrictCopy.ConfirmBody, LockCardStrictCopy.ConfirmBodyKey("F8"));
    }

    /// <summary>The contract the copy describes: an open card (strict or not) is the first rung of
    /// every panic press, in both panic modes. If this ever changes the copy is wrong.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Panic_always_ends_an_open_card(bool overrideAll)
        => Assert.Equal(PanicPolicy.Rung.DismissLockCard,
            PanicPolicy.Decide(lockCardOpen: true, paletteClaimedPress: false, overrideAll: overrideAll));

    [Fact]
    public void Every_language_carries_the_strict_copy_with_its_marks()
    {
        foreach (var lang in Languages)
        {
            var json = Lang(lang);
            foreach (var key in NewKeys)
            {
                var text = (string?)json[key];
                Assert.False(string.IsNullOrWhiteSpace(text), $"{lang} {key}: missing");
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
                Assert.DoesNotContain("!", text);
                Assert.DoesNotContain("Ã", text); // mojibake guard
                var wantsMark = key == LockCardStrictCopy.ConfirmBody;
                Assert.True(text!.Contains("{0}") == wantsMark, $"{lang} {key}: panic key mark {(wantsMark ? "missing" : "unexpected")}: {text}");
            }
        }
    }

    [Fact]
    public void The_strict_confirm_no_longer_promises_escape_does_nothing()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Features", "LockCardFeatureControl.xaml.cs"));
        Assert.DoesNotContain("NOT be able to escape lock cards with ESC", src);
        Assert.Contains("LockCardStrictCopy.ConfirmBodyKey", src);

        var win = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Windows", "LockCardWindow.xaml.cs"));
        Assert.Contains("LockCardStrictCopy.HintKey(EscClosesCard", win);
    }
}
