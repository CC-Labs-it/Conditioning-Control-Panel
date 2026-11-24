using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 desk run (wave 4, lane D item 5): the ComboBox on a rows-look option row showed its value in
/// the compact 11 px DarkComboBoxStyle face beside 16 px bold labels ("Mix", "Full"). Every combo on
/// a rows row (inside an OptionRow grid, or beside a RowLabel) now wears RowComboBoxStyle: 14 Bold
/// Nunito in TextLight, and the values it holds fit its box. Combos elsewhere keep their look.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowComboTests
{
    public static IEnumerable<object[]> Pages() => new[]
    {
        "flash", "video", "visuals", "lockcard", "wallpaper", "bubbles", "bubblecount", "bouncingtext", "subliminal",
        "spiral", "pinkfilter", "braindrain", "mindwipe", "scheduler", "ramp",
        "settings-audio", "settings-devices", "settings-emidesk", "settings-general", "settings-notifications",
        "settings-performance", "haptics", "lockdown", "awareness",
    }.Select(p => new object[] { p });

    private static bool OnRow(ComboBox c)
    {
        if (c.Parent is Panel p && p.Children.OfType<RowLabel>().Any()) return true;
        for (DependencyObject? d = c; d != null; d = VisualTreeHelper.GetParent(d) ?? (d as FrameworkElement)?.Parent)
            if (d is Grid g && g.Style != null && ReferenceEquals(g.Style, Application.Current.TryFindResource("OptionRow")))
                return true;
        return false;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_combo_on_a_rows_row_reads_like_the_row(string page)
    {
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage(null, () =>
        {
            var el = page == "flash" ? new FlashFeatureControl() : RowsPageStillsTests.Make(page);
            RowsRender.Host(el, 900);
            var combos = RowsRender.Descendants(el).OfType<ComboBox>().Where(OnRow).ToList();
            var problems = new List<string>();
            var rowStyle = Application.Current.TryFindResource("RowComboBoxStyle");
            Assert.NotNull(rowStyle);
            foreach (var c in combos)
            {
                string name = string.IsNullOrEmpty(c.Name) ? "(unnamed)" : c.Name;
                if (!ReferenceEquals(c.Style, rowStyle)) { problems.Add($"{name}: not RowComboBoxStyle"); continue; }
                if (Math.Abs(c.FontSize - 14) > 0.01 || c.FontWeight != FontWeights.Bold)
                    problems.Add($"{name}: {c.FontSize} {c.FontWeight}");

                // Room for the value: the box minus the presenter's 8+2 margin and the 18 px arrow column.
                // Only a box with a set Width can cut its value: an auto-width box grows with its selection.
                if (double.IsNaN(c.Width) || c.ActualWidth <= 0) continue;
                double room = c.ActualWidth - 8 - 2 - 18;
                var face = new Typeface(c.FontFamily, c.FontStyle, c.FontWeight, c.FontStretch);
                foreach (var item in c.Items)
                {
                    string? text = item is ComboBoxItem ci ? ci.Content as string : item as string;
                    if (string.IsNullOrEmpty(text)) continue;
                    var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face,
                        c.FontSize, Brushes.White, 1.0);
                    if (ft.WidthIncludingTrailingWhitespace > room + 0.5)
                        problems.Add($"{name}: '{text}' needs {ft.WidthIncludingTrailingWhitespace:0} of {room:0}");
                }
            }
            Assert.True(problems.Count == 0, page + ": " + string.Join("; ", problems));
            return 0;
        }));
    }

    [Fact]
    public void The_compact_dark_combo_keeps_its_size_off_the_rows()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var dark = (Style)Application.Current.TryFindResource("DarkComboBoxStyle")!;
            var c = new ComboBox { Style = dark };
            var host = new StackPanel();
            host.Children.Add(c);
            host.Measure(new Size(300, 100));
            Assert.Equal(11, c.FontSize, 2);
        });
    }

    /// <summary>With CCP_ROWS_DUMP set: stills of rows pages with every combo showing a value
    /// (the off-screen pages fill their selections in code that never runs here).</summary>
    [Theory]
    [InlineData("settings-performance")]
    [InlineData("wallpaper")]
    [InlineData("video")]
    public void Combo_stills_when_asked(string page)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CCP_ROWS_DUMP"))) return;
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage(null, () =>
        {
            var el = RowsPageStillsTests.Make(page);
            var host = RowsRender.Host(el, 900, before: p =>
            {
                foreach (var c in RowsRender.Descendants(p).OfType<ComboBox>())
                {
                    if (c.Items.Count == 0) c.Items.Add(new ComboBoxItem { Content = "Always new" });
                    try { if (c.SelectedIndex < 0) c.SelectedIndex = 0; } catch { }
                }
            });
            RowsRender.Dump(host, "combo-" + page);
            return 0;
        }));
    }
}
