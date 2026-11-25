using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A launcher card's blurb has a fixed two-line box under the title (LauncherGridLayout.BlurbHeight);
/// a third line is cut off with an ellipsis. These rows measure the real strings in all ten
/// language files with the tile's own font, size and wrap width at the default 1280 px launcher,
/// headless (a TextBlock measured, never shown), so a blurb that needs three lines fails here
/// instead of on someone's screen.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LauncherBlurbFitTests
{
    private static readonly string[] Languages =
        { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    // The games column at the launcher's default size (see LauncherGridLayoutTests): 1280 wide,
    // less the side margins, the 440 px panel card and the gutter. Three columns of cards.
    private const double ColumnAtDefault = 1280 - 56 - 440 - 28;   // 756

    /// <summary>The tile's blurb TextBlock as LauncherWindow.Tiles builds it, minus the fixed
    /// height, so its desired height says how many lines the text really takes.</summary>
    private static TextBlock Blurb(string text, double width) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Segoe UI"),   // the launcher window's font, which the blurb inherits
        FontSize = LauncherGridLayout.BlurbFontSize,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Width = width,
    };

    /// <summary>Lines the text wraps to at that width, and the height it takes.</summary>
    internal static (int Lines, double Height) Measure(string text, double width)
    {
        var wrapped = Blurb(text, width);
        wrapped.Measure(new Size(width, double.PositiveInfinity));
        var oneLine = Blurb(text, double.NaN);
        oneLine.TextWrapping = TextWrapping.NoWrap;
        oneLine.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lineHeight = Math.Max(1, oneLine.DesiredSize.Height);
        return ((int)Math.Round(wrapped.DesiredSize.Height / lineHeight), wrapped.DesiredSize.Height);
    }

    private static string Read(string lang, string key)
    {
        var path = Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty(key, out var v), $"{lang}.json is missing {key}");
        return v.GetString() ?? "";
    }

    [Theory]
    [InlineData("launcher_game_tc_blurb")]
    public void The_blurb_fits_two_lines_at_the_default_size_in_every_language(string key)
    {
        double tileWidth = LauncherGridLayout.TileWidth(ColumnAtDefault,
            LauncherGridLayout.Columns(ColumnAtDefault, 6));
        double width = LauncherGridLayout.BlurbWidth(tileWidth);
        Assert.True(width > 100, $"blurb width {width:F1} looks wrong");

        var misses = new List<string>();
        var report = new List<string>();
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var lang in Languages)
            {
                var text = Read(lang, key);
                var (lines, height) = Measure(text, width);
                report.Add($"{key} {lang}: {lines} lines, {height:F1} px at {width:F0} px");
                if (lines > LauncherGridLayout.BlurbMaxLines || height > LauncherGridLayout.BlurbHeight + 0.5)
                    misses.Add($"{lang}: {lines} lines, {height:F1} px in a {LauncherGridLayout.BlurbHeight} px box at {width:F0} px wide: \"{text}\"");
            }
        });
        // CCP_BLURB_DUMP=<file> writes every language's measured line count, for a hand check.
        if (Environment.GetEnvironmentVariable("CCP_BLURB_DUMP") is { Length: > 0 } dump)
            File.AppendAllLines(dump, report);
        Assert.True(misses.Count == 0, $"{key} does not fit two lines:\n" + string.Join("\n", misses));
    }

    [Fact]
    public void The_measure_counts_a_third_line()
    {
        // A guard on the guard: a string long enough for three lines must read as three, or the
        // row above passes everything.
        WpfRenderHarness.OnStaThread(() =>
        {
            double width = LauncherGridLayout.BlurbWidth(LauncherGridLayout.TileWidth(ColumnAtDefault, 3));
            var (one, _) = Measure("short", width);
            var (three, h) = Measure(string.Join(" ", Enumerable.Repeat("accept", 14)), width);
            Assert.Equal(1, one);
            Assert.True(three >= 3, $"a long string measured {three} lines ({h:F1} px)");
            Assert.True(h > LauncherGridLayout.BlurbHeight, $"three lines took only {h:F1} px");
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
