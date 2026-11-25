using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows reach (7.4.1 round, lane L3): the switchable sticker edge (ccp-bugs #1742, white
/// stays shipped) and the row label look on the door lists (Studio rack, Settings mini-rail).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsReachTests
{
    [Fact]
    public void DarkRim_is_the_shipped_edge_and_white_still_draws_as_it_did()
    {
        Assert.Equal(EmblemEdge.DarkRim, EmblemPalette.ShippedEdge); // owner 2026-11-25, ccp-bugs #1742
        var white = EmblemPalette.EdgePasses(EmblemEdge.White);
        Assert.Single(white);
        Assert.Equal(EmblemPalette.StickerEdge, white[0].Argb);
        Assert.Equal(EmblemPalette.StickerEdgeDip, white[0].Dip);
    }

    [Fact]
    public void A_dark_rim_sits_outside_the_dark_edge()
    {
        var dark = EmblemPalette.EdgePasses(EmblemEdge.Dark);
        Assert.Single(dark);
        Assert.Equal(EmblemPalette.StickerEdgeDark, dark[0].Argb);

        var rim = EmblemPalette.EdgePasses(EmblemEdge.DarkRim);
        Assert.Equal(2, rim.Count);
        // Outermost first: the light rim reaches further than the dark edge drawn over it.
        Assert.Equal(EmblemPalette.StickerRim, rim[0].Argb);
        Assert.Equal(EmblemPalette.StickerEdgeDark, rim[1].Argb);
        Assert.True(rim[0].Dip > rim[1].Dip);
        Assert.Equal(rim[0].Dip, EmblemPalette.EdgeReachDip(EmblemEdge.DarkRim));
        // A dark edge is darker than the panel ground it has to stand off from.
        Assert.True(Luma(EmblemPalette.StickerEdgeDark) < Luma(0xFF161322));
    }

    [Fact]
    public void An_emblem_takes_the_edge_from_its_page() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new StackPanel();
        var emblem = new OptionEmblem();
        page.Children.Add(emblem);
        Assert.Equal(EmblemPalette.ShippedEdge, RowHue.GetEdge(emblem));
        RowHue.SetEdge(page, EmblemEdge.DarkRim);
        Assert.Equal(EmblemEdge.DarkRim, RowHue.GetEdge(emblem));
    });

    [Fact]
    public void A_door_label_wears_the_row_label_look() => WpfRenderHarness.OnStaThread(() =>
    {
        var label = new DoorLabel { HueKey = "SectionHueStudioBrush", Text = "Flash Images" };
        var host = new Grid();
        host.Children.Add(label);
        host.Measure(new Size(200, 100));
        Assert.Equal(RowFonts.LabelSize, label.FontSize);
        Assert.Equal(FontWeights.Bold, label.FontWeight);
        Assert.Contains("Nunito", label.FontFamily.Source);
        Assert.Equal(TextTrimming.None, label.TextTrimming);
        Assert.Equal(TextWrapping.Wrap, label.TextWrapping);

        var hue = ((SolidColorBrush)Application.Current.FindResource("SectionHueStudioBrush")).Color;
        Assert.Equal(hue, label.EffectiveHue);
        var light = ((SolidColorBrush)Application.Current.FindResource("TextLightBrush")).Color;
        uint want = EmblemPalette.LabelTint(RowHue.ToArgb(light), RowHue.ToArgb(hue));
        Assert.Equal(want, RowHue.ToArgb(((SolidColorBrush)label.Foreground).Color));
    });

    [Fact]
    public void Every_studio_rack_row_reads_through_a_door_label() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new StudioTabView();
        var rack = (Panel)page.FindName("RackList")!;
        var rows = rack.Children.OfType<RadioButton>().ToList();
        Assert.True(rows.Count >= 13, $"only {rows.Count} rack rows");
        foreach (var row in rows)
        {
            var labels = RowsRender.Descendants(row).OfType<TextBlock>().Where(t => t.FontSize >= RowFonts.LabelSize).ToList();
            Assert.True(labels.Count > 0 && labels.All(l => l is DoorLabel), $"rack row '{row.Tag}' has a caption that is not a DoorLabel");
            // The resting height is a floor, so a wrapped label grows its row.
            Assert.True(double.IsNaN(row.Height) || row.IsChecked == true, $"rack row '{row.Tag}' keeps a fixed Height");
            Assert.Equal(38, row.MinHeight);
        }
    });

    [Fact]
    public void Every_settings_pill_is_tinted_with_its_own_dot() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new AppSettingsTabView();
        int seen = 0;
        foreach (var key in AppSettingsTabView.SectionKeys)
        {
            string suffix = char.ToUpperInvariant(key[0]) + key.Substring(1);
            if (page.FindName("SectionPill" + suffix) is not RadioButton pill) continue;
            var grid = (Grid)pill.Content;
            var dot = grid.Children.OfType<Ellipse>().Single();
            var label = grid.Children.OfType<DoorLabel>().Single();
            Assert.Equal(((SolidColorBrush)dot.Fill).Color, label.EffectiveHue);
            seen++;
        }
        Assert.True(seen >= 10, $"only {seen} settings pills carry a DoorLabel");
    });

    private static double Luma(uint argb) =>
        0.2126 * EmblemPalette.R(argb) + 0.7152 * EmblemPalette.G(argb) + 0.0722 * EmblemPalette.B(argb);
}
