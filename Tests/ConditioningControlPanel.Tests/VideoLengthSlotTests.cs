using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1761 (7.4.1): Studio > Mandatory Video > Video length drew "6m 17s" as "m 17s". The 7.4
/// readable rows draw the value at 19 ExtraBold Nunito, but this row kept fixed 56 px slots, so any
/// value with two-digit seconds spilled out on the left and lost its first digit. The page is laid out
/// at <see cref="RowsRender.Width"/> (900), narrower than the real Studio pane (about 1209), with the
/// widest text each slot can show, and each value must be drawn whole inside its own column.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class VideoLengthSlotTests
{
    [Fact]
    public void The_formatter_gives_the_widest_values_this_test_draws()
    {
        // Min slider goes to 600 s, max slider to 1800 s; digits are tabular, so these are the widest.
        Assert.Equal("9m 59s", VideoFeatureControl.FormatDuration(599));
        Assert.Equal("29m 59s", VideoFeatureControl.FormatDuration(1799));
        Assert.Equal("6m 17s", VideoFeatureControl.FormatDuration(377));
        Assert.Equal("off", VideoFeatureControl.FormatDuration(0));
    }

    [Theory]
    [InlineData(599, 1799)]
    [InlineData(377, 1799)]
    [InlineData(1799, 1799)]   // the min slot can never show this, but it must hold it all the same
    public void Video_length_values_are_drawn_whole(int minSeconds, int maxSeconds)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new VideoFeatureControl();
            string min = VideoFeatureControl.FormatDuration(minSeconds);
            string max = VideoFeatureControl.FormatDuration(maxSeconds);
            var host = RowsRender.Host(page, before: _ =>
            {
                page.TxtVideoMinDur.Text = min;
                page.TxtVideoMaxDur.Text = max;
            });
            RowsRender.Dump(host, $"b1761-video-length-{minSeconds}-{maxSeconds}");

            AssertWhole(page.TxtVideoMinDur, min);
            AssertWhole(page.TxtVideoMaxDur, max);

            // Both sliders keep a track you can drag.
            Assert.True(page.SliderVideoMinDur.ActualWidth >= 60, $"min slider is {page.SliderVideoMinDur.ActualWidth:0} px");
            Assert.True(page.SliderVideoMaxDur.ActualWidth >= 60, $"max slider is {page.SliderVideoMaxDur.ActualWidth:0} px");
        });
        RowsRender.AssertSilent("b1761-video-length");
    }

    [Fact]
    public void The_sliders_do_not_move_while_the_value_grows()
    {
        // Dragging from "off" to "29m 59s" must not resize the slots, or the slider jumps under the mouse.
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new VideoFeatureControl();
            var host = RowsRender.Host(page, before: _ =>
            {
                page.TxtVideoMinDur.Text = "off";
                page.TxtVideoMaxDur.Text = "off";
            });
            double minSlot = Column(page.TxtVideoMinDur).ActualWidth, maxSlot = Column(page.TxtVideoMaxDur).ActualWidth;
            double minTrack = page.SliderVideoMinDur.ActualWidth, maxTrack = page.SliderVideoMaxDur.ActualWidth;

            page.TxtVideoMinDur.Text = VideoFeatureControl.FormatDuration(599);
            page.TxtVideoMaxDur.Text = VideoFeatureControl.FormatDuration(1799);
            RowsRender.Layout(host);

            Assert.Equal(minSlot, Column(page.TxtVideoMinDur).ActualWidth, 1);
            Assert.Equal(maxSlot, Column(page.TxtVideoMaxDur).ActualWidth, 1);
            Assert.Equal(minTrack, page.SliderVideoMinDur.ActualWidth, 1);
            Assert.Equal(maxTrack, page.SliderVideoMaxDur.ActualWidth, 1);
        });
    }

    private static ColumnDefinition Column(RowValue value) =>
        ((Grid)value.Parent).ColumnDefinitions[Grid.GetColumn(value)];

    /// <summary>
    /// The value's text fits its column, WPF did not clip the value at layout, and the text's left
    /// edge starts inside its own column (a right aligned overflow starts left of it and loses digits).
    /// </summary>
    private static void AssertWhole(RowValue value, string text)
    {
        Assert.Equal(text, value.NumberPart.Text);
        var row = (Grid)value.Parent;
        var col = Column(value);
        var tb = value.NumberPart;
        var face = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        double textWidth = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, tb.FontSize, Brushes.White, 1.0).WidthIncludingTrailingWhitespace;

        Assert.True(textWidth <= col.ActualWidth + 0.5,
            $"'{text}' is {textWidth:0.0} px in a {col.ActualWidth:0.0} px slot");
        Assert.True(LayoutInformation.GetLayoutClip(value) == null, $"'{text}': the value is clipped by layout");
        double left = tb.TranslatePoint(new Point(0, 0), row).X;
        Assert.True(left + 0.5 >= col.Offset,
            $"'{text}' starts at {left:0.0}, left of its slot at {col.Offset:0.0}: the first digit is cut off");
        double right = tb.TranslatePoint(new Point(tb.ActualWidth, 0), row).X;
        Assert.True(right <= col.Offset + col.ActualWidth + 0.5,
            $"'{text}' ends at {right:0.0}, past its slot at {col.Offset + col.ActualWidth:0.0}");
    }
}
