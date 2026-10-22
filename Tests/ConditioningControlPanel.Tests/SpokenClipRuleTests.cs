using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1411: on CCP Default the trigger lines and subliminal whispers are silent BY DESIGN (no synthetic
/// speech; CCP Default never borrows the Bambi clips). The editors mark those lines, and the mark
/// asks the same rule the players resolve through, so it cannot disagree with what plays.
/// </summary>
public class SpokenClipRuleTests
{
    private const string Shared = @"C:\sub_audio\GOOD GIRL.mp3";
    private const string Neutral = @"C:\words\relax.mp3";

    private static string? Resolve(string? modId, string? mod = null, string? shared = null, string? neutral = null)
        => SpokenClipRule.Resolve(modId, () => mod, () => shared, () => neutral);

    [Fact]
    public void A_bambi_mod_finds_the_shared_clip()
        => Assert.Equal(Shared, Resolve(BuiltInMods.BambiSleepId, shared: Shared));

    [Theory]
    [InlineData(BuiltInMods.CCPDefaultId)]
    [InlineData(BuiltInMods.LockedId)]
    [InlineData(null)]
    public void Ccp_default_never_borrows_the_shared_clip(string? modId)
        => Assert.Null(Resolve(modId, shared: Shared));

    [Fact]
    public void Ccp_default_finds_the_neutral_circe_word()
        => Assert.Equal(Neutral, Resolve(BuiltInMods.CCPDefaultId, shared: Shared, neutral: Neutral));

    [Fact]
    public void A_bambi_mod_never_reads_the_neutral_words()
        => Assert.Null(Resolve(BuiltInMods.BambiSleepId, neutral: Neutral));

    [Theory]
    [InlineData(BuiltInMods.CCPDefaultId)]
    [InlineData(BuiltInMods.BambiSleepId)]
    public void The_mods_own_clip_wins_and_nothing_else_is_asked(string modId)
    {
        var asked = 0;
        var got = SpokenClipRule.Resolve(modId, () => "own.mp3",
            () => { asked++; return Shared; }, () => { asked++; return Neutral; });
        Assert.Equal("own.mp3", got);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void A_line_with_no_voice_is_silent_and_a_blank_line_is_not_marked()
    {
        Func<string, bool> hasVoice = p => p == "RELAX";
        Assert.False(SpokenClipRule.IsSilent("RELAX", hasVoice));
        Assert.True(SpokenClipRule.IsSilent("GOOD GIRL", hasVoice));
        Assert.False(SpokenClipRule.IsSilent("  ", hasVoice));
        Assert.False(SpokenClipRule.IsSilent(null, hasVoice));
    }

    [Fact]
    public void The_hint_shows_only_when_at_least_one_line_is_silent()
    {
        Func<string, bool> hasVoice = p => p == "RELAX";
        Assert.False(SpokenClipRule.AnySilent(new[] { "RELAX" }, hasVoice));
        Assert.True(SpokenClipRule.AnySilent(new[] { "RELAX", "OBEY" }, hasVoice));
        Assert.False(SpokenClipRule.AnySilent(Array.Empty<string>(), hasVoice));
        Assert.False(SpokenClipRule.AnySilent(null, hasVoice));
    }

    // Source pins: both players resolve through the one rule, and every editor of a spoken list
    // asks the subliminal player's own lookup (trigger lines speak through PlayTriggerAudio).
    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", Path.Combine(parts)));
    }

    [Theory]
    [InlineData("Services", "Subliminal", "SubliminalService.cs")]
    [InlineData("Services", "KeywordTriggerService.cs")]
    public void Both_players_resolve_through_the_rule(params string[] parts)
    {
        var src = Source(parts);
        Assert.Contains("SpokenClipRule.Resolve(", src);
        Assert.DoesNotContain("ModAudioPolicy.UsesSharedSubAudio", src);
    }

    [Theory]
    [InlineData("Features", "SubliminalFeatureControl.xaml.cs")]
    [InlineData("MainWindow", "MainWindow.UiUpdates.cs")]
    [InlineData("MainWindow", "MainWindow.Patreon.cs")]
    public void Every_spoken_list_editor_marks_with_the_players_lookup(params string[] parts)
        => Assert.Contains("MarkSilentLines(t => App.Subliminal?.HasRecordedVoice(t) ?? true)", Source(parts));

    [Fact]
    public void The_mark_is_a_quiet_glyph_with_the_house_fallback_and_a_tooltip()
    {
        var xaml = Source("Dialogs", "TextEditorDialog.xaml");
        Assert.Contains("x:Name=\"SilentMark\"", xaml);
        Assert.Contains("FontFamily=\"Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI\"", xaml);
        Assert.Contains("{loc:Str tooltip_silent_line}", xaml);
        Assert.Contains("{loc:Str hint_silent_lines}", xaml);
    }
}
