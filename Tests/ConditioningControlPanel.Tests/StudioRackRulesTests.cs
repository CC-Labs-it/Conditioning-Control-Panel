using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Declutter wave 1 (2026-11-15): on the Studio rack, SELECTED and ON are two signals. Selected
/// is the row surface (pane-coloured fill + the section hue bar); on is the row's content (filled
/// dot, full-strength label), off a hollow ring and a dimmed label. Group headers count what is on.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class StudioRackRulesTests
{
    [Fact]
    public void OnIsAFilledDotAndAFullLabel_OffIsARingAndADimmedLabel()
    {
        Assert.True(StudioRackRules.DotIsFilled(true));
        Assert.False(StudioRackRules.DotIsFilled(false));
        Assert.Equal(1.0, StudioRackRules.LabelOpacity(true));
        Assert.Equal(StudioRackRules.OffLabelOpacity, StudioRackRules.LabelOpacity(false));
        Assert.InRange(StudioRackRules.OffLabelOpacity, 0.5, 0.6);
    }

    [Fact]
    public void AnUnknownStateReadsAsOff()
    {
        Assert.False(StudioRackRules.IsOn(null));
        Assert.False(StudioRackRules.DotIsFilled(null));
        Assert.Equal(StudioRackRules.OffLabelOpacity, StudioRackRules.LabelOpacity(null));
    }

    [Fact]
    public void AGroupCountsOnlyTheRowsThatHaveASwitch()
    {
        var rows = new (bool, bool?)[]
        {
            (true, true), (true, false), (true, null), (true, true),
            (false, null),   // Visuals: no master switch, counted on neither side
        };
        Assert.Equal((2, 4), StudioRackRules.Count(rows));
        Assert.Equal("2 of 4 on", StudioRackRules.CountText("{0} of {1} on", 2, 4));
    }

    [Fact]
    public void AGroupWithNothingToCountShowsNoNumber()
    {
        Assert.Equal((0, 0), StudioRackRules.Count(new (bool, bool?)[] { (false, null) }));
        Assert.Null(StudioRackRules.CountText("{0} of {1} on", 0, 0));
        Assert.Null(StudioRackRules.CountText("", 1, 2));
        Assert.Null(StudioRackRules.CountText("{0} of {5} on", 1, 2));
    }

    [Fact]
    public void TheCountLineIsInEveryLanguageWithBothNumbers()
    {
        var dir = SourceRoots.LanguagesDirectory;
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            Assert.True(doc.RootElement.TryGetProperty(StudioRackRules.GroupCountKey, out var v),
                $"{System.IO.Path.GetFileName(f)} has no {StudioRackRules.GroupCountKey}");
            var text = v.GetString() ?? "";
            Assert.Contains("{0}", text);
            Assert.Contains("{1}", text);
            Assert.DoesNotContain("!", text);
            Assert.DoesNotContain("—", text);
            var shown = StudioRackRules.CountText(text, 3, 7);
            Assert.NotNull(shown);
            Assert.Contains("3", shown);
            Assert.Contains("7", shown);
        }
    }

    [Fact]
    public void TheSelectedRowWearsThePaneSurfaceAndTheStudioHueBar()
    {
        var xaml = File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "StudioTabView.xaml"));
        var style = xaml.Substring(xaml.IndexOf("x:Key=\"RackEntryStyle\"", StringComparison.Ordinal));
        style = style.Substring(0, style.IndexOf("</Style>", StringComparison.Ordinal));

        var fill = style.Substring(style.IndexOf("x:Name=\"RowFill\"", StringComparison.Ordinal));
        Assert.Contains($"{{DynamicResource {StudioRackRules.SelectedFillBrushKey}}}", fill.Substring(0, fill.IndexOf("/>", StringComparison.Ordinal)));
        var bar = style.Substring(style.IndexOf("x:Name=\"RowBar\"", StringComparison.Ordinal));
        Assert.Contains($"{{DynamicResource {StudioRackRules.SelectedBarBrushKey}}}", bar.Substring(0, bar.IndexOf("/>", StringComparison.Ordinal)));

        // The fill is the pane's surface, so the rack card must sit one step under it or the
        // selected row would vanish into its own ground.
        Assert.NotEqual(StudioRackRules.SelectedFillBrushKey, StudioRackRules.RackGroundBrushKey);
        var card = xaml.Substring(xaml.IndexOf("Width=\"230\"", StringComparison.Ordinal), 160);
        Assert.Contains($"Background=\"{{DynamicResource {StudioRackRules.RackGroundBrushKey}}}\"", card);
    }

    [Fact]
    public void TheStudioHueIsTheStripsStudioPink()
    {
        var theme = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "Theme");
        var colors = File.ReadAllText(System.IO.Path.Combine(theme, "Colors.xaml"));
        var brushes = File.ReadAllText(System.IO.Path.Combine(theme, "Brushes.xaml"));
        var pink = ConditioningControlPanel.Controls.NavRail.NavStripRules.Pink;
        var rgb = $"{pink.R:X2}{pink.G:X2}{pink.B:X2}";

        Assert.Contains($"<Color x:Key=\"SectionHueStudio\">#FF{rgb}</Color>", colors);
        Assert.Contains($"<Color x:Key=\"SectionHueStudioWash\">#14{rgb}</Color>", colors);
        Assert.Contains($"<Color x:Key=\"SectionHueStudioBorder\">#40{rgb}</Color>", colors);
        // Brushes.xaml carries the triad as literals (deferred-BAML trap): they must not drift.
        Assert.Contains($"x:Key=\"SectionHueStudioBrush\" Color=\"#FF{rgb}\"", brushes);
        Assert.Contains($"x:Key=\"SectionHueStudioBorderBrush\" Color=\"#40{rgb}\"", brushes);
    }

    [Fact]
    public void EveryRackDotIsEitherFilledOrAHollowRing()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new StudioTabView();
            page.Measure(new Size(1209, 700));
            page.Arrange(new Rect(0, 0, 1209, 700));
            page.UpdateLayout();

            var dots = Descendants(page).OfType<Ellipse>().Where(e => e.Width == 7 && e.Height == 7).ToList();
            Assert.NotEmpty(dots);
            foreach (var d in dots)
            {
                bool filled = d.Fill is SolidColorBrush f && f.Color.A > 0;
                bool ring = d.Stroke != null && d.StrokeThickness > 0;
                Assert.True(filled ^ ring, "a rack dot is a filled disc or a hollow ring, never both or neither");
                Assert.Equal(1.0, d.Opacity);
            }
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Views")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
