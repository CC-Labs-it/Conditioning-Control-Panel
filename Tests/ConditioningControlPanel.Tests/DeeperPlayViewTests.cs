using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Play > Deeper is play-only (tier 2 testers, 2026-11-16): choose a file, a read-only effect
/// heat map, one button that loads, maximises and plays. The full layout (editor, import,
/// publishing) stays one link away and is what the Home "Deeper Editor" tile opens.
///
/// <para>The heat map maths is in EffectHeatMapTests; the wiring is pinned here by source-text
/// reads, because MainWindow cannot be instantiated in a unit test.</para>
/// </summary>
public class DeeperPlayViewTests
{
    // ------------------------------------------------------------------ wiring

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    /// <summary>The play-only Border alone, from its x:Name to the full layout that follows it.</summary>
    private static string PlayViewXaml()
    {
        var xaml = ReadSource("Views", "Tabs", "DeeperTabView.xaml");
        var start = xaml.IndexOf("x:Name=\"DeeperPlayView\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("x:Name=\"DeeperFullBody\"", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "DeeperPlayView must sit before DeeperFullBody");
        return xaml.Substring(start, end - start);
    }

    [Fact]
    public void The_play_view_has_a_picker_a_heat_map_one_play_button_and_the_editor_link()
    {
        var view = PlayViewXaml();
        foreach (var name in new[] { "CmbDeeperPlayFile", "BtnDeeperPlayBrowse", "DeeperPlayHeat",
                                     "BtnDeeperPlayStart", "BtnDeeperOpenFullLayout" })
            Assert.Contains($"x:Name=\"{name}\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void The_play_view_carries_no_editing_import_or_publishing_controls()
    {
        var view = PlayViewXaml();
        foreach (var handler in new[] { "BtnDeeperNewEnhancement_Click", "BtnDeeperImport_Click",
                                        "DeeperRowSubmit_Click", "DeeperRowDelete_Click",
                                        "BtnDeeperCatalogue_Click", "BtnDeeperTutorial_Click" })
            Assert.DoesNotContain(handler, view, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_door_into_the_tab_picks_a_layout_and_the_editor_doors_ask_for_the_full_one()
    {
        var nav = ReadSource("MainWindow", "MainWindow.TabNavigation.cs");
        var start = nav.IndexOf("case \"deeper\":", StringComparison.Ordinal);
        var end = nav.IndexOf("case \"", start + 8, StringComparison.Ordinal);
        Assert.Contains("ApplyDeeperLayoutForVisit()", nav.Substring(start, end - start), StringComparison.Ordinal);

        // The tour and the catalogue's "open library" land on the full layout they point at.
        var tab = ReadSource("MainWindow", "MainWindow.DeeperTab.cs");
        foreach (var method in new[] { "private void StartDeeperTabTutorial()", "private void SwitchToDeeperLibraryTab()" })
        {
            var at = tab.IndexOf(method, StringComparison.Ordinal);
            Assert.True(at > 0, method + " is gone");
            Assert.Contains("ShowDeeperEditorLayout()", tab.Substring(at, 400), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_play_button_maximises_and_starts_the_player()
    {
        var src = ReadSource("MainWindow", "MainWindow.DeeperPlayView.cs");
        Assert.Contains("MaximizeAndPlayWhenReady(path)", src, StringComparison.Ordinal);
        var player = ReadSource("Views", "Deeper", "EnhancementPlayerWindow.PlayNow.cs");
        Assert.Contains("WindowState = WindowState.Maximized", player, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_play_view_string_has_an_english_master()
    {
        var en = File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, "en.json"));
        var used = new HashSet<string>();
        foreach (var text in new[] { PlayViewXaml(), ReadSource("MainWindow", "MainWindow.DeeperPlayView.cs"),
                                     ReadSource("Views", "Tabs", "DeeperTabView.xaml") })
            foreach (Match m in Regex.Matches(text, @"deeper_playview_[a-z_]+"))
                used.Add(m.Value);
        Assert.NotEmpty(used);
        foreach (var key in used)
            Assert.Contains($"\"{key}\":", en, StringComparison.Ordinal);
    }
}
