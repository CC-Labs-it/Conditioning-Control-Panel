using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Views.Controls.Studio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep, lane R1 (2026-11-24): Mandatory Video (with its mini-game box, the Mercy row
/// and the gold Lights Down box), Visuals, Lock Card, Wallpaper and the shared "Show content on"
/// picker. Each page is realised off screen through <see cref="RowsRender.Check"/> with every fold
/// open and every row the page can hide shown, so each RowLabel is checked for its emblem, its
/// OptionRow height and an untrimmed label. Nothing here plays media or opens a window.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsR1Tests
{
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xE0, 0x8A);

    [Fact]
    public void Video_rows_wear_emblems_and_the_lights_down_box_is_gold()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new VideoFeatureControl();
            var labels = RowsRender.Check(page, "r1-video", minLabels: 23,
                show: new[] { "PanelFloatingOptions", "LightsDownRows" },
                before: _ =>
                {
                    page.FoldMore.IsOpen = true;
                    page.SliderPerHour.Value = 10; page.TxtPerHour.Text = "10";
                    page.ChkStrict.IsChecked = true;
                    page.ChkVideoLoopShort.IsChecked = false;
                    SuperRows.Apply(page.LightsDownRows, SuperEffect.LightsDown);
                    page.LightsDownRows.Visibility = Visibility.Visible;
                });

            var perHour = labels.First(l => l.Emblem == EmblemKind.Often);
            Assert.Equal(10.0 / 20, perHour.Value, 3);
            Assert.Equal("an hour", page.TxtPerHour.Unit);
            Assert.True(labels.First(l => l.Emblem == EmblemKind.Lock).IsOn);
            Assert.False(labels.First(l => l.Emblem == EmblemKind.Loop).IsOn);
            Assert.Equal(3, labels.First(l => l.Emblem == EmblemKind.Target).Value);

            // The Lights Down rows sit outside every fold: they wear the gold of their box.
            var lamp = labels.Single(l => l.Emblem == EmblemKind.Lamp);
            Assert.Equal(Gold, lamp.EffectiveHue);
            Assert.True(page.LightsDownRows.Opacity is 1 or SuperRows.LockedOpacity);

            // Mercy (its own UserControl inside the More fold) follows its switch.
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Safe);
        });
    }

    [Fact]
    public void Visuals_rows_wear_emblems()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new VisualsFeatureControl();
            var labels = RowsRender.Check(page, "r1-visuals", minLabels: 6,
                before: _ => page.ChkAudio.IsChecked = true);
            Assert.True(labels.First(l => l.Emblem == EmblemKind.Loud).IsOn);
            Assert.Contains(labels, l => l.Emblem == EmblemKind.Clear);
        });
    }

    [Fact]
    public void Lock_card_rows_wear_emblems()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new LockCardFeatureControl();
            var labels = RowsRender.Check(page, "r1-lockcard", minLabels: 10,
                show: new[] { "RowRepeatsMin", "RowTargetLength", "RowTargetVariance" },
                before: _ =>
                {
                    page.FoldMore.IsOpen = true;
                    page.SliderFreq.Value = 5; page.TxtFreq.Text = "5";
                    page.ChkVoiceMode.IsChecked = true;
                });
            Assert.Equal(5.0 / 10, labels.First(l => l.Emblem == EmblemKind.Often).Value, 3);
            Assert.Equal("an hour", page.TxtFreq.Unit);
            // The hard-coded voice label keeps its text; RowLabel draws it without the mic emoji.
            var voice = labels.Single(l => l.Emblem == EmblemKind.Mic);
            Assert.True(voice.IsOn);
            Assert.Equal("Solve by voice", voice.ShownText.Trim());
        });
    }

    [Fact]
    public void Wallpaper_rows_wear_emblems()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new WallpaperFeatureControl();
            var labels = RowsRender.Check(page, "r1-wallpaper", minLabels: 7,
                show: new[] { "RowWpOwnImage", "RowWpInterval" },
                before: _ =>
                {
                    page.ChkWpRotate.IsChecked = true;
                    page.SliderWpInterval.Value = 150;
                });
            Assert.True(labels.First(l => l.Emblem == EmblemKind.Loop).IsOn);
            var often = labels.Single(l => l.Emblem == EmblemKind.Often);
            Assert.Equal(150.0 / 300, often.Value, 2);
        });
    }

    [Theory]
    [InlineData(900)]
    [InlineData(520)]
    public void Show_content_on_picker_fits(double width)
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var labels = RowsRender.Check(new MonitorTargetPicker(), $"r1-monitorpicker-{width}", minLabels: 1, width: width);
            Assert.Equal(EmblemKind.Screens, labels.Single().Emblem);
        });
    }
}
