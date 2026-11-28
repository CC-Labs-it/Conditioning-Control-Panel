using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Rows;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 review (wave 8, item 3): the font pickers list every installed family, and RowComboFit measured all
/// of them on each resize pass. A list of <see cref="RowComboFit.CacheFromItems"/> or more keeps its
/// widest-item width until its items change; a short list still re-measures every pass, since its items
/// may be relabelled in place. The probe: an item's text grows without a list change.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowComboFitCacheTests
{
    private static (ComboBox box, Grid row) Build(int count)
    {
        var style = (Style)Application.Current.TryFindResource("RowComboBoxStyle")!;
        var c = new ComboBox { Style = style, Width = 80 };
        for (int i = 0; i < count; i++) c.Items.Add(new ComboBoxItem { Content = "Font " + i });
        var row = new Grid { Width = 800 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(c, 1);
        row.Children.Add(c);
        c.SelectedIndex = 0;
        Pass(row, c);
        return (c, row);
    }

    private static void Pass(Grid row, ComboBox c)
    {
        RowsRender.Layout(row, 800);
        RowComboFit.Refresh(c);
        RowsRender.Layout(row, 800);
    }

    [Fact]
    public void A_long_list_measures_once_until_its_items_change()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (c, row) = Build(RowComboFit.CacheFromItems + 60);
            double before = c.MinWidth;
            ((ComboBoxItem)c.Items[3]!).Content = "A much longer family name, Condensed";
            Pass(row, c);
            Assert.Equal(before, c.MinWidth, 1);

            c.Items.Add(new ComboBoxItem { Content = "Font x" });
            Pass(row, c);
            Assert.True(c.MinWidth > before + 20, $"items changed but the box kept {c.MinWidth:0} (was {before:0})");
        });
    }

    [Fact]
    public void A_short_list_measures_every_pass()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var (c, row) = Build(5);
            double before = c.MinWidth;
            ((ComboBoxItem)c.Items[3]!).Content = "A much longer family name, Condensed";
            Pass(row, c);
            Assert.True(c.MinWidth > before + 20, $"a relabelled short list kept {c.MinWidth:0} (was {before:0})");
        });
    }
}
