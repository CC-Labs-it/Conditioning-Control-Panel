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
    public void No_studio_row_label_binds_a_key_ending_in_a_colon()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "ConditioningControlPanel", "Features")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var app = Path.Combine(dir!, "ConditioningControlPanel");
        var en = JObject.Parse(File.ReadAllText(Path.Combine(app, "Localization", "Languages", "en.json")));
        var rowLabel = new Regex("<rows:RowLabel[^>]*Text=\"\\{loc:Str ([a-z0-9_]+)\\}\"");
        var bad = Directory.EnumerateFiles(Path.Combine(app, "Features"), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(f => rowLabel.Matches(File.ReadAllText(f)).Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .Where(x => ((string?)en[x.Key] ?? "").TrimEnd().EndsWith(':') || ((string?)en[x.Key] ?? "").TrimEnd().EndsWith('：'))
            .Select(x => x.File + ": " + x.Key)
            .ToList();
        Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad));
    }
}
