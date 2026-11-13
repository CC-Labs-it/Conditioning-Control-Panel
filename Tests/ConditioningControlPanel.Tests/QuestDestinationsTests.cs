using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs#1534: a daily or weekly quest card opens the page that finishes it. Every quest
/// category is either sent somewhere real or deliberately left plain, and the trip only
/// navigates (no game launch, no dialog, nothing started).
/// </summary>
public class QuestDestinationsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [Fact]
    public void Every_category_is_either_routed_or_left_plain_never_both()
    {
        foreach (var c in Enum.GetValues<QuestCategory>())
        {
            bool routed = QuestDestinations.Map.ContainsKey(c);
            bool plain = QuestDestinations.NoHome.Contains(c);
            Assert.True(routed ^ plain, $"{c}: routed={routed} plain={plain}");
        }
    }

    [Fact]
    public void Every_destination_is_a_live_tab_key()
    {
        var keys = NavSections.AllTabs.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (c, d) in QuestDestinations.Map)
        {
            Assert.True(keys.Contains(d.TabKey), $"{c}: '{d.TabKey}' is not a nav tab");
            Assert.False(NavSections.Redirects.ContainsKey(d.TabKey), $"{c}: '{d.TabKey}' is a retired redirect");
        }
    }

    [Fact]
    public void Rack_destinations_name_real_studio_modules()
    {
        var racks = SettingsPaletteIndex.All.Where(e => e.RackKey != null).Select(e => e.RackKey!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (c, d) in QuestDestinations.Map.Where(kv => kv.Value.RackKey != null))
        {
            Assert.Equal("studio", d.TabKey);
            Assert.True(racks.Contains(d.RackKey!), $"{c}: rack '{d.RackKey}' has no palette row");
        }
    }

    /// <summary>The XAML file that owns each tab a destination highlights in. A new highlighted
    /// destination must add its tab here.</summary>
    private static readonly Dictionary<string, string> TabXaml = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lockdown"] = "Views/Tabs/LockdownTabView.xaml",
        ["bambitakeover"] = "Views/Tabs/BambiTakeoverTabView.xaml",
        ["remotecontrol"] = "Views/Tabs/RemoteControlTabView.xaml",
        ["awareness"] = "Views/Tabs/AwarenessTabView.xaml",
        ["blinktrainer"] = "Views/Tabs/BlinkTrainerTabView.xaml",
    };

    /// <summary>The palette's FindElementByName walks from the WINDOW root, so a highlighted name
    /// must live in its own tab's XAML and nowhere else in the app.</summary>
    [Fact]
    public void Highlighted_controls_live_in_their_tab_and_are_unique()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var allXaml = Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Select(f => (Path: f, Text: File.ReadAllText(f))).ToList();

        foreach (var (c, d) in QuestDestinations.Map.Where(kv => kv.Value.ElementName != null))
        {
            Assert.True(TabXaml.TryGetValue(d.TabKey, out var rel), $"{c}: add the XAML file for tab '{d.TabKey}'");
            var own = Path.GetFullPath(Path.Combine(app, rel!));
            var mark = $"x:Name=\"{d.ElementName}\"";
            int Count(string text) => (text.Length - text.Replace(mark, "").Length) / mark.Length;

            var hits = allXaml.Where(x => Count(x.Text) > 0).ToList();
            Assert.True(hits.Count == 1 && Path.GetFullPath(hits[0].Path) == own && Count(hits[0].Text) == 1,
                $"{c}: {d.ElementName} should be named once, in {rel}; found in [{string.Join(", ", hits.Select(h => Path.GetFileName(h.Path)))}]");
        }
    }

    [Fact]
    public void A_quest_click_only_navigates()
    {
        foreach (var (c, d) in QuestDestinations.Map)
        {
            var entry = d.ToPaletteEntry(c);
            Assert.Null(entry.GameId);
            Assert.Null(entry.LauncherKey);
            Assert.False(entry.OpensLauncher);
            Assert.Equal(d.TabKey, entry.TabKey);
        }
    }

    [Fact]
    public void Lockdown_lands_on_its_page_and_rings_the_start_button()
    {
        var d = QuestDestinations.For(QuestCategory.Lockdown);
        Assert.NotNull(d);
        Assert.Equal("lockdown", d!.TabKey);
        Assert.Equal("BtnActivateLockdown", d.ElementName);
    }

    [Fact]
    public void Shipped_quests_with_no_home_are_the_plain_ones()
    {
        foreach (var q in QuestDefinition.DailyQuests.Concat(QuestDefinition.WeeklyQuests))
            Assert.Equal(QuestDestinations.NoHome.Contains(q.Category), QuestDestinations.For(q.Category) == null);
        Assert.Null(QuestDestinations.For(QuestCategory.Streak));
        Assert.Null(QuestDestinations.For(QuestCategory.Combined));
    }
}
