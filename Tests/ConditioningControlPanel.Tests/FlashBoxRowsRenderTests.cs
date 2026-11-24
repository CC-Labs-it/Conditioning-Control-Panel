using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows pilot (owner 2026-11-24): the REAL Flash Images page (FlashFeatureControl) realized
/// off screen at 900 px wide, every fold open and every grant-hidden row shown, so each option row is
/// checked for its emblem, its height and an untrimmed label. With env CCP_ROWS_DUMP=&lt;dir&gt; it saves
/// flash-box.png (144 dpi) and flash-box-96.png (96 dpi) for the owner's look.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class FlashBoxRowsRenderTests
{
    private const double Width = 900;

    [Fact]
    public void Every_flash_row_wears_an_emblem_fits_and_dumps_when_asked()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new FlashFeatureControl();

            // Every fold open, every row the grants hide shown: the look is judged on all of them.
            // A believable mix of values and switches, set the way the page's own loader does.
            // The gold Deck box, in whatever state the real SuperRows gives a test account (no grant
            // = shown locked, or folded when the gate cannot be read); then shown so its rows are seen.
            var host = RowsRender.Host(page, Width,
                show: new[] { "RowImagesMin", "BoxFlashV2", "RowMotion", "RowDriftSpeed", "RowRoundedCorners",
                              "RowDraggable", "RowShatter", "RowJackpotRemix" },
                before: _ =>
                {
                    page.RowGetFlashesV2.Visibility = Visibility.Collapsed;
                    page.SliderFrequency.Value = 120; page.TxtFrequency.Text = "120";
                    page.SliderImages.Value = 4; page.TxtImages.Text = "4";
                    page.ChkRandomImages.IsChecked = true;
                    page.SliderImagesMin.Value = 2; page.TxtImagesMin.Text = "2";
                    page.SliderMaxOnScreen.Value = 8; page.TxtMaxOnScreen.Text = "8";
                    page.ChkClickable.IsChecked = true;
                    page.ChkCorruption.IsChecked = true;
                    page.ChkGlow.IsChecked = true;
                    page.ChkFlashAvoidCenter.IsChecked = true;
                    page.ChkFlashShatter.IsChecked = true;
                    page.ChkFlashGazePop.IsChecked = true;
                    SuperRows.Apply(page.DeckRows, SuperEffect.FlickerDeck);
                    page.DeckRows.Visibility = Visibility.Visible;
                });

            var labels = RowsRender.AssertRows(page, minLabels: 25);

            // Section hue reaches the rows; the switches drive IsOn; Often follows value / Maximum.
            var perHour = labels.First(l => l.Emblem == EmblemKind.Often);
            Assert.Equal(120.0 / 180, perHour.Value, 3);
            Assert.Equal(8, labels.First(l => l.Emblem == EmblemKind.Most).Value);
            Assert.True(labels.First(l => l.Emblem == EmblemKind.Click).IsOn);
            Assert.False(labels.First(l => l.Emblem == EmblemKind.Solid).IsOn);
            var studio = ((SolidColorBrush)Application.Current.TryFindResource(OptionSection.HueKey(SectionTheme.Main))).Color;
            Assert.Equal(studio, perHour.EffectiveHue);
            Assert.Equal("an hour", page.TxtFrequency.Unit);

            // Sliders inside a section fill in its hue on this page; the Deck box's stay pink.
            Assert.Equal(studio, ((SolidColorBrush)RowHue.GetSliderFill(page.SliderFrequency)!).Color);
            Assert.Null(RowHue.GetSliderFill(page.SliderDeckFlips));

            // The Deck rows still dim and lock as a block, RowLabels and all.
            Assert.True(page.DeckRows.Opacity is 1 or SuperRows.LockedOpacity);

            // Live Preview pilot: the side-art card holds the desktop slice; play a moment of it for the still.
            var preview = RowsRender.Descendants(page).OfType<ConditioningControlPanel.Controls.Preview.EffectPreview>().Single();
            Assert.True(preview.ActualHeight > 600, $"the preview is {preview.ActualHeight:0} DIP tall, not the card");
            preview.Driver!.Open();
            for (int i = 0; i < 45; i++) preview.Driver.Scene.Tick(1 / 30.0);
            preview.Repaint();

            RowsRender.Dump(host, "flash-box", 144);
            RowsRender.Dump(host, "flash-box-96");
        });
    }
}
