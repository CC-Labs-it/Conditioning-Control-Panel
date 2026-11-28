using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Studio rows (readable rows look) name an option without a trailing colon: a row label bound to a key that
/// ends in one (Bubble Pop's "Size:", installed 7.4.0) reads as a stray. Keys with a colon belong to older forms.</summary>
public class RowLabelColonTests
{
    [Fact]
    public void No_row_label_binds_a_key_ending_in_a_colon()
    {
        // 7.4.2: the scan reaches the Settings, Companion and Lockdown views too (six strays there:
        // Delay, Power, Startup Video, Highlight mode, Interval, Duration) and reads every language,
        // since French writes " :" and Japanese and Chinese a full-width colon.
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "ConditioningControlPanel", "Features")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var app = Path.Combine(dir!, "ConditioningControlPanel");
        var languages = Directory.EnumerateFiles(SourceRoots.LanguagesDirectory, "*.json")
            .Select(f => (Lang: Path.GetFileNameWithoutExtension(f), Strings: JObject.Parse(File.ReadAllText(f))))
            .ToList();
        Assert.Equal(10, languages.Count);
        var rowLabel = new Regex("<rows:RowLabel[^>]*Text=\"\\{loc:Str ([a-z0-9_]+)\\}\"");
        var bad = new[] { "Features", "Views" }
            .SelectMany(root => Directory.EnumerateFiles(Path.Combine(app, root), "*.xaml", SearchOption.AllDirectories))
            .SelectMany(f => rowLabel.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .SelectMany(x => languages.Select(l => (x.File, x.Key, l.Lang, Value: ((string?)l.Strings[x.Key] ?? "").TrimEnd())))
            .Where(x => x.Value.EndsWith(':') || x.Value.EndsWith('：'))
            .Select(x => $"{x.File}: {x.Key} ({x.Lang} \"{x.Value}\")")
            .ToList();
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }

    [Fact]
    public void Bubble_pop_speed_row_reads_in_the_players_language()
    {
        // 7.4.2: the row said "Speed" in English in every language; it binds setting_speed, the key
        // the other speed rows use.
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "ConditioningControlPanel", "Features")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var xaml = File.ReadAllText(Path.Combine(dir!, "ConditioningControlPanel", "Features", "BubblePopFeatureControl.xaml"));
        Assert.DoesNotContain("Text=\"Speed\"", xaml);
        Assert.Contains("<rows:RowLabel Grid.Column=\"0\" Text=\"{loc:Str setting_speed}\" Emblem=\"Fast\"/>", xaml);
    }
}
