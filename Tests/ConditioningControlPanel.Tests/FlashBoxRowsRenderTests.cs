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
            var host = new Grid { Width = Width, Background = new SolidColorBrush(Color.FromRgb(0x16, 0x13, 0x22)) };
            host.Children.Add(page);

            // Every fold open, every row the grants hide shown: the look is judged on all of them.
            foreach (var s in Descendants(page).OfType<OptionSection>()) s.IsOpen = true;
            foreach (var name in new[] { "RowImagesMin", "BoxFlashV2", "RowMotion", "RowDriftSpeed", "RowRoundedCorners",
                                         "RowDraggable", "RowShatter", "RowJackpotRemix" })
                ((FrameworkElement)page.FindName(name)).Visibility = Visibility.Visible;
            page.RowGetFlashesV2.Visibility = Visibility.Collapsed;

            // A believable mix of values and switches, set the way the page's own loader does.
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

            // The gold Deck box, in whatever state the real SuperRows gives a test account (no grant
            // = shown locked, or folded when the gate cannot be read); then shown so its rows are seen.
            SuperRows.Apply(page.DeckRows, SuperEffect.FlickerDeck);
            page.DeckRows.Visibility = Visibility.Visible;

            Layout(host);

            var labels = Descendants(page).OfType<RowLabel>().ToList();
            Assert.True(labels.Count >= 25, $"only {labels.Count} RowLabels on the Flash page");
            foreach (var label in labels)
            {
                Assert.True(label.Emblem.HasValue, $"'{label.Text}' has no emblem");
                if (label.Parent is Grid row)
                {
                    Assert.True(row.MinHeight >= 46, $"'{label.ShownText}' row is not an OptionRow");
                    Assert.True(double.IsNaN(row.Height), $"'{label.ShownText}' row keeps a fixed Height");
                    Assert.True(row.ActualHeight + 0.5 >= label.DesiredSize.Height, $"'{label.ShownText}' is clipped by its row");
                    // Nothing in the row starts left of the label's right edge.
                    double right = label.TranslatePoint(new Point(label.ActualWidth, 0), row).X;
                    foreach (UIElement other in row.Children)
                    {
                        if (other == label || other is not FrameworkElement fe || fe.Visibility != Visibility.Visible) continue;
                        double left = fe.TranslatePoint(new Point(0, 0), row).X;
                        Assert.True(left + 0.5 >= right, $"'{label.ShownText}' overlaps {fe.GetType().Name} {fe.Name}");
                    }
                }
                var tb = label.TextPart;
                var ft = new FormattedText(tb.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.White, 1.0);
                Assert.True(ft.WidthIncludingTrailingWhitespace <= tb.ActualWidth + 1,
                    $"'{tb.Text}' is trimmed ({ft.WidthIncludingTrailingWhitespace:0} > {tb.ActualWidth:0})");
                Assert.False(LeadingEmoji.Strip(tb.Text) != tb.Text, $"'{tb.Text}' still leads with an emoji");
            }

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
            var preview = Descendants(page).OfType<ConditioningControlPanel.Controls.Preview.EffectPreview>().Single();
            Assert.True(preview.ActualHeight > 600, $"the preview is {preview.ActualHeight:0} DIP tall, not the card");
            preview.Driver!.Open();
            for (int i = 0; i < 45; i++) preview.Driver.Scene.Tick(1 / 30.0);
            preview.Repaint();

            if (Environment.GetEnvironmentVariable("CCP_ROWS_DUMP") is not { Length: > 0 } dir) return;
            Directory.CreateDirectory(dir);
            Save(host, Path.Combine(dir, "flash-box.png"), 144);
            Save(host, Path.Combine(dir, "flash-box-96.png"), 96);
        });
    }

    private static void Layout(FrameworkElement host)
    {
        host.Measure(new Size(Width, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
    }

    private static void Save(FrameworkElement host, string path, double dpi)
    {
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * dpi / 96), (int)Math.Ceiling(host.ActualHeight * dpi / 96),
            dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>Logical and visual children both (the SuperBox hosts its rows in a template), each once.</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var kids = LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>().ToList();
            if (node is Visual or System.Windows.Media.Media3D.Visual3D)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) kids.Add(VisualTreeHelper.GetChild(node, i));
            foreach (var k in kids)
                if (seen.Add(k)) { yield return k; stack.Push(k); }
        }
    }
}
