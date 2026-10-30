using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Fix wave F1c: the dressing room's bake shows "n of 17" and a meter instead of a greyed button.
/// The count itself (<see cref="RainBakeProgress"/>), the window title line and the ten-language copy.
/// </summary>
public sealed class RainBakeProgressTests
{
    [Theory]
    [InlineData(0, 17, 0.0, 0.0, 1)]
    [InlineData(3, 17, 0.5, 3.5 / 17, 4)]
    [InlineData(16, 17, 0.99, 16.99 / 17, 17)]
    [InlineData(17, 17, 0.7, 1.0, 17)]     // every clip in: a stale clip fraction adds nothing
    [InlineData(20, 17, 0.0, 1.0, 17)]     // never past the end
    [InlineData(2, 4, 3.0, 0.75, 3)]       // a broken clip fraction clamps
    [InlineData(0, 0, 0.5, 0.0, 0)]        // no clips: nothing to show
    public void Fraction_and_the_clip_in_hand(int done, int total, double clip, double fraction, int shown)
    {
        var p = new RainBakeProgress(done, total, clip);
        Assert.Equal(fraction, p.Fraction, 6);
        Assert.Equal(shown, p.ClipShown);
        Assert.InRange(p.Percent, 0, 100);
    }

    [Fact]
    public void Fill_and_the_window_title()
    {
        var p = new RainBakeProgress(2, 17, 0.4);
        Assert.Equal("Getting dressed: 3 of 17", p.Fill("Getting dressed: {n} of {total}"));
        Assert.Equal("Rain's wardrobe - Getting dressed: 3 of 17",
            RainWardrobeHostService.TitleFor("Rain's wardrobe", p, "Getting dressed: {n} of {total}"));
        Assert.Equal("Rain's wardrobe", RainWardrobeHostService.TitleFor("Rain's wardrobe", null, "x {n}"));
        Assert.Equal("Rain's wardrobe", RainWardrobeHostService.TitleFor("Rain's wardrobe", new RainBakeProgress(0, 0, 0), "x {n}"));
    }

    [Fact]
    public void Page_lines_skip_a_key_the_language_files_lack()
    {
        var lines = RainWardrobeHostService.PageStrings(key => key == "rain_wardrobe_baking" ? "Cambio: {n} di {total}" : key);
        Assert.Equal(new[] { "baking" }, lines.Keys.ToArray());
        Assert.Equal(new[] { "applying", "baking", "bakeHint" }, RainWardrobeHostService.PageStringKeys.Keys.ToArray());
    }

    private static string LanguagesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization", "Languages")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Localization", "Languages");
    }

    [Fact]
    public void Every_bake_line_is_in_all_ten_languages_with_its_placeholders_and_house_punctuation()
    {
        var files = Directory.GetFiles(LanguagesDir(), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, file + " carries a BOM");
            var json = JObject.Parse(Encoding.UTF8.GetString(bytes));
            foreach (var key in RainWardrobeHostService.PageStringKeys.Values)
            {
                var text = (string?)json[key];
                Assert.False(string.IsNullOrWhiteSpace(text), $"{Path.GetFileName(file)} lacks {key}");
                Assert.DoesNotContain('\u2014', text!);
                Assert.DoesNotContain('\u2013', text!);
                Assert.DoesNotContain('!', text!);
                Assert.DoesNotContain('\uFFFD', text!);
            }
            var baking = (string)json["rain_wardrobe_baking"]!;
            Assert.Contains("{n}", baking);
            Assert.Contains("{total}", baking);
        }
    }
}
