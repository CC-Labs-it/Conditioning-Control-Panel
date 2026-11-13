using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Mod Creator's original-text browser (<c>ModCreatorWindow.CreateOriginalTextBrowser</c>)
/// realizes off screen, lists every text grouped, and narrows to a search.
/// </summary>
public class ModTextBrowserRenderTests
{
    [Fact]
    public void The_browser_lists_every_text_and_narrows_to_a_search()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var entries = ModTextCatalog.Build();
            var picked = new List<string>();
            var root = ModCreatorWindow.CreateOriginalTextBrowser(entries, null, null, picked.Add, () => { });

            root.Measure(new Size(720, 420));
            root.Arrange(new Rect(0, 0, 720, 420));
            root.UpdateLayout();

            var search = Find<TextBox>(root).Single();
            var list = Find<ListBox>(root).Single();
            var title = Find<TextBlock>(root).First();

            Assert.Equal(entries.Count, list.Items.Count);
            Assert.NotNull(list.Items.Groups);
            Assert.Equal(entries.Select(e => e.Group).Distinct().Count(), list.Items.Groups!.Count);

            search.Text = "sparkle";
            var shown = list.Items.Cast<ModTextEntry>().ToList();
            Assert.NotEmpty(shown);
            Assert.True(shown.Count < entries.Count);
            Assert.All(shown, e => Assert.True(ModTextCatalog.Matches(e, "sparkle")));
            Assert.Contains($"{shown.Count} of {entries.Count}", title.Text);

            search.Text = "";
            Assert.Equal(entries.Count, list.Items.Count);
            Assert.Empty(picked);
        });
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T hit) yield return hit;
            foreach (var deeper in Find<T>(child)) yield return deeper;
        }
    }
}
