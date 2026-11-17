using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The fixes from the 7.3.0 tester round (tier 2, 2026-11-15 to 11-17) that #5485 did not
/// already carry: the rack tagged only Flash Images, the fog ran two phrases together, and
/// three more glyphs drew as empty boxes for the same reason the V2 star did. (The Deck and
/// owned Shatter case sits in FlickerDeckTests; presets and the star are in #5485's tests.)
/// </summary>
public class Super73FeedbackTests
{
    // ------------------------------------------------------------------ the rack's v2 pill

    [Theory]
    [InlineData("flash", SuperEffect.FlickerDeck)]
    [InlineData("bubbles", SuperEffect.InnerBloom)]
    [InlineData("subliminal", SuperEffect.Afterglow)]
    [InlineData("spiral", SuperEffect.Vortex)]
    [InlineData("pinkfilter", SuperEffect.Creep)]
    [InlineData("video", SuperEffect.LightsDown)]
    [InlineData("bouncingtext", SuperEffect.Scrawl)]
    [InlineData("BrainDrain", SuperEffect.Undertow)]
    public void Every_effect_with_a_V2_has_its_rack_row(string rackKey, SuperEffect effect)
        => Assert.Equal(effect, SuperBase.EffectForRackKey(rackKey));

    [Fact]
    public void Rack_rows_cover_every_effect_once_and_other_rows_carry_none()
    {
        var keys = new[] { "flash", "bubbles", "subliminal", "spiral", "pinkfilter", "video", "bouncingtext", "braindrain" };
        var effects = keys.Select(k => SuperBase.EffectForRackKey(k)).ToList();
        Assert.Equal(Enum.GetValues(typeof(SuperEffect)).Length, effects.Distinct().Count());
        Assert.DoesNotContain(null, effects);

        foreach (var none in new[] { "wallpaper", "visuals", "lockcard", "bubblecount", "mindwipe", "haptics", "scheduler", "", null })
            Assert.Null(SuperBase.EffectForRackKey(none));
    }

    // ------------------------------------------------------------------ the fog's finished line

    [Fact]
    public void Single_words_read_as_one_sentence()
        => Assert.Equal("let go and sink", AfterglowLine.Join(new[] { "let", "go", "and", "sink" }));

    [Fact]
    public void Phrases_are_set_apart_by_a_stop()
    {
        var line = AfterglowLine.Join(new[] { "thank her", "no key for you" });
        Assert.Equal("thank her" + AfterglowLine.PhraseStop + "no key for you", line);
        // One phrase among single words is enough: every ghost gets its stop.
        Assert.Equal(2, Regex.Matches(AfterglowLine.Join(new[] { "drop", "good girl", "sink" }), Regex.Escape(AfterglowLine.PhraseStop)).Count);
    }

    // ------------------------------------------------------------------ the star draws as a star

    private static string AppDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
                dir = dir.Parent;
            Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
            return Path.Combine(dir!.FullName, "ConditioningControlPanel");
        }
    }

    private static IEnumerable<string> SourceFiles()
    {
        var skip = new[] { "bin", "obj", ".claude", "Resources" };
        return Directory.EnumerateFiles(AppDir, "*.*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .Where(p => !Path.GetRelativePath(AppDir, p)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(s => skip.Contains(s, StringComparer.OrdinalIgnoreCase)));
    }

    private static string Decode(string literal)
    {
        literal = Regex.Replace(literal, @"\\u([0-9a-fA-F]{4})", m => char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));
        return Regex.Replace(literal, @"&#x([0-9a-fA-F]+);", m => char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));
    }

    private static bool IsIconFontChar(int cp) => (cp >= 0xE000 && cp <= 0xF8FF) || cp >= 0xF0000;

    [Fact]
    public void The_two_glyph_chains_are_what_they_say()
    {
        Assert.StartsWith("Segoe MDL2 Assets", GlyphFonts.Icon);
        Assert.DoesNotContain("MDL2", GlyphFonts.Symbol);
        Assert.StartsWith("Segoe UI Symbol", GlyphFonts.Symbol);
    }

    /// <summary>
    /// Segoe MDL2 Assets is symbol-encoded: WPF takes it to hold every character and never
    /// falls through, so a star or a tick set in a chain that STARTS with it draws as an empty
    /// box. That is how the V2 switch shipped in 7.3.0 as a hollow frame. Ordinary symbols take
    /// <see cref="GlyphFonts.Symbol"/>; only private-use icon characters take the MDL2 chain.
    /// </summary>
    [Fact]
    public void No_ordinary_symbol_is_set_in_the_icon_font_chain()
    {
        var chain = new Regex("FontFamily\\s*=\\s*\"Segoe MDL2 Assets|FontFamily\\(\"Segoe MDL2 Assets|GlyphFonts\\.Icon");
        var text = new Regex("(?:Text|Content)\\s*=\\s*\"([^\"{]*)\"");
        var offenders = new List<string>();
        int files = 0;

        foreach (var path in SourceFiles())
        {
            files++;
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!chain.IsMatch(lines[i])) continue;
                var window = string.Join("\n", lines.Skip(Math.Max(0, i - 3)).Take(7));
                foreach (Match m in text.Matches(window))
                {
                    var value = Decode(m.Groups[1].Value);
                    for (int c = 0; c < value.Length; c += char.IsSurrogatePair(value, c) ? 2 : 1)
                    {
                        int cp = char.ConvertToUtf32(value, c);
                        if (cp > 0x7F && !IsIconFontChar(cp))
                            offenders.Add(Path.GetRelativePath(AppDir, path) + ":" + (i + 1) + " U+" + cp.ToString("X4"));
                    }
                }
            }
        }

        Assert.True(files > 500, "the source walk found only " + files + " files: the scan would pass vacuously");
        Assert.True(offenders.Count == 0,
            "these glyphs are ordinary symbols set in the icon-font chain and draw as an empty box; " +
            "use Services.GlyphFonts.Symbol: " + string.Join(", ", offenders.Distinct()));
    }
}
