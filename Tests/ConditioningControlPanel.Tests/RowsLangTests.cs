using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows long-label sweep (integration 2026-11-24): six pages in Russian, German and
/// Brazilian Portuguese at the 900 px panel width and the 520 px popup width. Does nothing unless
/// env <c>CCP_ROWS_LANG_DUMP=&lt;dir&gt;</c> is set; then it saves
/// <c>&lt;dir&gt;/&lt;page&gt;-&lt;lang&gt;-&lt;width&gt;.png</c> and writes every row problem it sees (clipped,
/// trimmed, more than three lines, overlapping, squeezing its control to nothing) to
/// <c>&lt;page&gt;-&lt;lang&gt;-&lt;width&gt;.txt</c> beside it, instead of failing: a translation that
/// squeezes is a finding for the owner, not a red suite. The always-on guard is
/// <see cref="Wrapped_labels_grow_their_row_and_short_ones_stay_one_line"/>.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsLangTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var page in new[] { "flash", "video", "bouncingtext", "haptics", "settings-performance", "settings-devices",
                                  "bubbles", "bubblecount", "settings-general" })
            foreach (var lang in new[] { "ru", "de", "pt-BR" })
                foreach (var width in new[] { 900, 520 })
                    yield return new object[] { page, lang, width };
    }

    private static string[] Show(string page) => page switch
    {
        "flash" => new[] { "RowImagesMin", "RowMotion", "RowDriftSpeed", "RowRoundedCorners", "RowDraggable", "RowShatter", "RowJackpotRemix" },
        "haptics" => new[] { "VideoHapticSyncSliders", "HapticAudioAdvanced" },
        "settings-performance" => new[] { "PnlGentleChoices" },
        _ => Array.Empty<string>(),
    };

    private static FrameworkElement Make(string page) =>
        page == "flash" ? new FlashFeatureControl() : RowsPageStillsTests.Make(page);

    [Theory]
    [MemberData(nameof(Cases))]
    public void Page_in_language_when_asked(string page, string lang, int width)
    {
        string? dir = Environment.GetEnvironmentVariable("CCP_ROWS_LANG_DUMP");
        if (string.IsNullOrWhiteSpace(dir)) return;
        string name = $"{page}-{lang}-{width}";
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage(lang, () =>
        {
            var root = Make(page);
            // Every Expander open and the lanes' hidden option blocks shown, as their own stills did.
            var host = RowsRender.Host(root, width, show: Show(page), before: p =>
            {
                foreach (var ex in RowsRender.Descendants(p).OfType<System.Windows.Controls.Expander>().ToList()) ex.IsExpanded = true;
            });
            var labels = RowsRender.Descendants(root).OfType<RowLabel>().Where(l => l.IsVisible || l.ActualWidth > 0).ToList();
            var problems = RowsRender.Problems(labels, squeeze: true);
            var wrapped = labels.Where(l => RowsRender.Lines(l) > 1)
                .Select(l => $"wraps to {RowsRender.Lines(l)} lines: '{l.ShownText}' ({l.TextPart.ActualWidth:0} px box)");
            Assert.NotNull(RowsRender.Dump(host, name, dir: dir));
            File.WriteAllLines(Path.Combine(dir, name + ".txt"),
                new[] { $"{labels.Count} labels" }.Concat(problems.Select(p => "PROBLEM " + p)).Concat(wrapped));
            return 0;
        }));
        RowsRender.AssertSilent(name);
    }

    /// <summary>
    /// A RowLabel in a star column too narrow for its text wraps to a second line and its OptionRow
    /// grows; the same label with room stays one line at the row's usual height. No ellipsis.
    /// </summary>
    [Fact]
    public void Wrapped_labels_grow_their_row_and_short_ones_stay_one_line()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            const string text = "Keep gaze-reactive effects on the calibrated screen";
            (RowLabel label, System.Windows.Controls.Grid row) Build(double width)
            {
                var row = new System.Windows.Controls.Grid();
                row.SetResourceReference(FrameworkElement.StyleProperty, "OptionRow");
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
                var label = new RowLabel { Text = text, Emblem = ConditioningControlPanel.Emblems.EmblemKind.Screens };
                row.Children.Add(label);
                var box = new System.Windows.Controls.CheckBox();
                System.Windows.Controls.Grid.SetColumn(box, 1);
                row.Children.Add(box);
                var host = new System.Windows.Controls.Grid { Width = width };
                host.Children.Add(row);
                RowsRender.Layout(host, width);
                return (label, row);
            }

            var (wide, wideRow) = Build(900);
            var (narrow, narrowRow) = Build(300);
            Assert.Equal(1, RowsRender.Lines(wide));
            Assert.True(RowsRender.Lines(narrow) is 2 or 3, $"{RowsRender.Lines(narrow)} lines at 300 px");
            Assert.Null(RowsRender.Clipped(wide));
            Assert.Null(RowsRender.Clipped(narrow));
            Assert.True(narrow.TextPart.ActualHeight > wide.TextPart.ActualHeight, "the narrow label did not wrap");
            Assert.True(narrowRow.ActualHeight + 0.5 >= narrow.DesiredSize.Height && narrowRow.ActualHeight >= wideRow.ActualHeight,
                $"the wrapped row did not grow ({narrowRow.ActualHeight:0} vs label {narrow.DesiredSize.Height:0})");
            Assert.Equal(System.Windows.TextTrimming.None, narrow.TextPart.TextTrimming);
        });
    }
}
