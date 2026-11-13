using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Mod Creator's browsable list of original texts (<see cref="ModTextCatalog"/>): it is built
/// from the definition lists, it keeps up with the inline feature labels, and it leaves .ccpmod
/// loading alone.
/// </summary>
public class ModTextCatalogTests
{
    private static readonly IReadOnlyList<ModTextEntry> Entries = ModTextCatalog.Build();

    private static HashSet<string> Texts() => Entries.Select(e => e.Text).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_skill_name_flavour_and_effect_is_listed()
    {
        var texts = Texts();
        Assert.NotEmpty(SkillDefinition.All);
        foreach (var s in SkillDefinition.All)
        {
            Assert.Contains(s.Name.Trim(), texts);
            if (!string.IsNullOrWhiteSpace(s.FlavorText) && s.FlavorText.Length <= 200) Assert.Contains(s.FlavorText.Trim(), texts);
            if (!string.IsNullOrWhiteSpace(s.Description) && s.Description.Length <= 200) Assert.Contains(s.Description.Trim(), texts);
        }
    }

    [Fact]
    public void Achievements_quests_ranks_and_companions_are_listed()
    {
        var texts = Texts();
        foreach (var a in Achievement.All.Values) Assert.Contains(a.Name.Trim(), texts);
        foreach (var q in QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests)) Assert.Contains(q.Name.Trim(), texts);
        foreach (var r in RankTitles.All) Assert.Contains(r.Title, texts);
        foreach (var c in CompanionDefinition.AllCompanions) Assert.Contains(c.Name.Trim(), texts);
    }

    [Fact]
    public void The_main_groups_are_filled_and_in_display_order()
    {
        foreach (var g in new[] { ModTextCatalog.Skills, ModTextCatalog.Flavour, ModTextCatalog.Achievements,
                                  ModTextCatalog.Quests, ModTextCatalog.Levels, ModTextCatalog.Companions, ModTextCatalog.Other })
            Assert.Contains(Entries, e => e.Group == g);

        var order = Entries.Select(e => ModTextCatalog.Groups.ToList().IndexOf(e.Group)).ToList();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void Every_text_is_listed_once_and_is_a_key_the_loader_accepts()
    {
        Assert.Equal(Entries.Count, Texts().Count);
        Assert.All(Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Text));
            Assert.True(e.Text.Length <= 200, e.Text);
            Assert.Equal(e.Text.Trim(), e.Text);
        });
    }

    /// <summary>
    /// The feature labels are inline literals at their call sites, so they are mirrored in
    /// <c>ModTextCatalog.UiLabels</c>. This scan is what keeps the mirror current: a new
    /// <c>ML("...")</c> / <c>MLCard("...")</c> label or Studio rack row that is not listed fails here.
    /// </summary>
    [Fact]
    public void Every_feature_label_probed_in_the_ui_is_listed()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var found = new List<string>();

        var ui = File.ReadAllText(Path.Combine(app, "MainWindow", "MainWindow.UiUpdates.cs"));
        found.AddRange(Regex.Matches(ui, "\\bML(?:Card)?\\(\"([^\"]+)\"").Select(m => Regex.Unescape(m.Groups[1].Value)));

        var studio = File.ReadAllText(Path.Combine(app, "Views", "Tabs", "StudioTabView.xaml.cs"));
        found.AddRange(Regex.Matches(studio, "\\bAdd\\(\"[a-z]+\", \"[^\"]*\", [^,]+, \"([^\"]+)\"").Select(m => m.Groups[1].Value));

        Assert.True(found.Count > 20, "the label scan found only " + found.Count + " labels; did the call sites move?");
        var listed = ModTextCatalog.UiLabels.ToHashSet(StringComparer.Ordinal);
        var missing = found.Distinct().Where(l => !listed.Contains(l)).ToList();
        Assert.True(missing.Count == 0, "add these to ModTextCatalog.UiLabels: " + string.Join(" | ", missing));
    }

    [Theory]
    [InlineData(0, "BASIC BIMBO")]
    [InlineData(19, "BASIC BIMBO")]
    [InlineData(20, "DUMB AIRHEAD")]
    [InlineData(49, "DUMB AIRHEAD")]
    [InlineData(50, "SYNTHETIC BLOWDOLL")]
    [InlineData(99, "SYNTHETIC BLOWDOLL")]
    [InlineData(100, "PERFECT FUCKPUPPET")]
    [InlineData(500, "PERFECT FUCKPUPPET")]
    public void Rank_titles_keep_their_level_thresholds(int level, string title) =>
        Assert.Equal(title, RankTitles.ForLevel(level));

    [Fact]
    public void Search_needs_every_word_and_ignores_case()
    {
        var e = new ModTextEntry(ModTextCatalog.Skills, "Sparkle Boost", "skill name");
        Assert.True(ModTextCatalog.Matches(e, null));
        Assert.True(ModTextCatalog.Matches(e, "  "));
        Assert.True(ModTextCatalog.Matches(e, "sparkle"));
        Assert.True(ModTextCatalog.Matches(e, "boost skill"));
        Assert.True(ModTextCatalog.Matches(e, "skills"));
        Assert.False(ModTextCatalog.Matches(e, "sparkle quest"));
    }

    [Fact]
    public void The_export_lists_every_text_on_its_own_line_under_its_group()
    {
        var text = ModTextCatalog.ToExportText(Entries, "9.9.9");
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        Assert.Contains("App version: 9.9.9", lines);
        foreach (var g in Entries.Select(e => e.Group).Distinct())
            Assert.Contains($"== {g} ({Entries.Count(e => e.Group == g)}) ==", lines);
        var lineSet = lines.ToHashSet(StringComparer.Ordinal);
        Assert.All(Entries, e => Assert.Contains(e.Text, lineSet));
    }

    /// <summary>
    /// An existing mod (the shipped Dronification manifest, packed the way the Mod Creator exports)
    /// still loads with the same text replacements, and every one still applies.
    /// </summary>
    [Fact]
    public async Task An_existing_ccpmod_loads_with_its_text_replacements_unchanged()
    {
        var json = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "drone_mod.json"));
        var expected = JObject.Parse(json)["textReplacements"]!.ToObject<Dictionary<string, string>>()!;
        Assert.True(expected.Count > 10);

        var dir = Path.Combine(Path.GetTempPath(), "ccp-modtext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var pack = Path.Combine(dir, "drone-mode.ccpmod");
            using (var zip = ZipFile.Open(pack, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("mod.json");
                using var w = new StreamWriter(entry.Open());
                w.Write(json);
            }

            var manifest = await ModService.PeekManifestAsync(pack);
            Assert.NotNull(manifest);
            Assert.Equal(expected, manifest!.TextReplacements);

            Assert.Null(ModService.SanitizeManifest(manifest));
            foreach (var (from, to) in expected)
            {
                Assert.Equal(to, manifest.TextReplacements![from]);
            }

            // A round trip through the manifest JSON keeps the pairs as they were.
            var again = JsonConvert.DeserializeObject<ModManifest>(JsonConvert.SerializeObject(manifest))!;
            Assert.Equal(manifest.TextReplacements, again.TextReplacements);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
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
