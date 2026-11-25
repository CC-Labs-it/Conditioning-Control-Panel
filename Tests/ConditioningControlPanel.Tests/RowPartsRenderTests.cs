using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows parts (owner 2026-11-24): RowLabel, RowValue and OptionEmblem built in code, off
/// screen. With env CCP_ROWS_DUMP=&lt;dir&gt; the sample saves row-samples.png at 144 dpi: six rows in
/// a pink (inside a real OptionSection), a green and a blue hue, one row OFF.
/// </summary>
public class RowPartsRenderTests
{
    private static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
    private static readonly Color Green = Color.FromRgb(0x5C, 0xE0, 0xA5);
    private static readonly Color Blue = Color.FromRgb(0x5E, 0xA8, 0xFF);

    [Fact]
    public void Sample_rows_measure_and_dump_when_asked()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var panel = new StackPanel { Margin = new Thickness(20, 6, 20, 12) };

            var basics = new OptionSection { Title = "Basics", Theme = SectionTheme.Main };
            var perHour = Row("Per Hour", EmblemKind.Often, 120.0 / 180, true, slider: (1, 180, 120), number: "120", unit: "an hour");
            basics.Rows.Add(perHour.Row);
            basics.Rows.Add(Row("Images", EmblemKind.Many, 4, true, slider: (1, 15, 4), number: "4", unit: "each time").Row);
            panel.Children.Add(basics);

            var clicking = Hued(Green);
            clicking.Children.Add(Row("Click", EmblemKind.Click, 0, true).Row);
            var off = Row("Stay until popped", EmblemKind.Stay, 0, false);
            clicking.Children.Add(off.Row);
            panel.Children.Add(clicking);

            var extras = Hued(Blue);
            var hydra = Row("\U0001F419Hydra", EmblemKind.Splits, 0, true);
            extras.Children.Add(hydra.Row);
            var most = Row("Max On Screen", EmblemKind.Most, 8, true, slider: (1, 20, 8), number: "8", unit: "at once");
            extras.Children.Add(most.Row);
            panel.Children.Add(extras);

            var host = new Border { Width = 560, Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1D, 0x3A)), Child = panel };
            host.Measure(new Size(560, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();

            foreach (var part in new FrameworkElement[] { perHour.Label, perHour.Value!, off.Label, hydra.Label, most.Value! })
            {
                Assert.True(part.ActualWidth > 0, part + " has no width");
                Assert.True(part.ActualHeight > 0, part + " has no height");
            }
            Assert.True(perHour.Label.EmblemPart.ActualWidth >= 30);
            Assert.Equal(RowFonts.EmblemSize, perHour.Label.EmblemPart.ActualHeight);
            Assert.True(perHour.Row.ActualHeight >= 46, "OptionRow is MinHeight 46");

            // The section hands its hue to the rows inside; plain panels hand theirs.
            var studio = ((SolidColorBrush)Application.Current.TryFindResource(OptionSection.HueKey(SectionTheme.Main))).Color;
            Assert.Equal(studio, perHour.Label.EffectiveHue);
            Assert.Equal(studio, perHour.Value!.EffectiveHue);
            Assert.Equal(Blue, hydra.Label.EffectiveHue);
            Assert.Equal(Blue, most.Label.EmblemPart.EffectiveHue);

            // An emblem drops the leading emoji; OFF dims the label and greys the picture.
            Assert.Equal("Hydra", hydra.Label.ShownText);
            Assert.Equal(EmblemPalette.OffLabelOpacity, off.Label.TextPart.Opacity);
            Assert.False(off.Label.EmblemPart.IsOn);
            Assert.Equal("an hour", perHour.Value!.UnitPart.Text);
            Assert.Equal(Visibility.Visible, perHour.Value!.UnitPart.Visibility);

            // Label ink is 70% TextLight + 30% hue; numbers 82% hue + 18% white.
            uint expected = EmblemPalette.LabelTint(EmblemPalette.TextLight, RowHueArgb(Blue));
            Assert.Equal(expected, RowHueArgb(((SolidColorBrush)hydra.Label.TextPart.Foreground).Color));
            Assert.Equal(EmblemPalette.ValueTint(RowHueArgb(Blue)), RowHueArgb(((SolidColorBrush)most.Value!.NumberPart.Foreground).Color));

            if (Environment.GetEnvironmentVariable("CCP_ROWS_DUMP") is not { Length: > 0 } dir) return;
            Directory.CreateDirectory(dir);
            const double dpi = 144;
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * dpi / 96), (int)Math.Ceiling(host.ActualHeight * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            bmp.Render(host);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(dir, "row-samples.png"));
            enc.Save(fs);
        });
    }

    [Fact]
    public void Nunito_bundles_a_Bold_and_an_ExtraBold_face_under_one_family()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var (weight, file) in new[] { (FontWeights.Bold, "Nunito-Bold.ttf"), (FontWeights.ExtraBold, "Nunito-ExtraBold.ttf") })
            {
                var face = new Typeface(RowFonts.Nunito, FontStyles.Normal, weight, FontStretches.Normal);
                Assert.True(face.TryGetGlyphTypeface(out var glyphs), "no glyph typeface for Nunito " + weight);
                Assert.EndsWith(file, glyphs.FontUri.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.Equal(weight, glyphs.Weight);
                Assert.Contains(glyphs.Win32FamilyNames.Values.Concat(glyphs.FamilyNames.Values), n => n.StartsWith("Nunito", StringComparison.Ordinal));
            }
        });
    }

    [Fact]
    public void Xaml_can_write_the_lane_2_shape()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            const string xaml =
                "<StackPanel xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:rows=\"clr-namespace:ConditioningControlPanel.Controls.Rows;assembly=ConditioningControlPanel\">" +
                "<rows:RowLabel Text=\"Hydra\" Emblem=\"Splits\" Subject=\"Video\" Value=\"3\" IsOn=\"False\"/>" +
                "<rows:RowLabel Text=\"No picture\"/>" +
                "<rows:RowValue Text=\"180\" Unit=\"an hour\"/>" +
                "<rows:OptionEmblem Kind=\"Most\" Value=\"5\" Hue=\"#FF5CE0A5\"/>" +
                "</StackPanel>";
            var root = (StackPanel)XamlReader.Parse(xaml);
            var label = (RowLabel)root.Children[0];
            Assert.Equal(EmblemKind.Splits, label.Emblem);
            Assert.Equal(EmblemSubject.Video, label.Subject);
            Assert.False(label.IsOn);
            Assert.Null(((RowLabel)root.Children[1]).Emblem);
            Assert.Equal(Visibility.Collapsed, ((RowLabel)root.Children[1]).EmblemPart.Visibility);
            Assert.Equal("an hour", ((RowValue)root.Children[2]).Unit);
            Assert.Equal(Green, ((OptionEmblem)root.Children[3]).EffectiveHue);
            // Code-behind writes numbers straight in.
            var v = (RowValue)root.Children[2];
            v.Text = "42";
            Assert.Equal("42", v.NumberPart.Text);
        });
    }

    // ---------------------------------------------------------------- helpers

    private static StackPanel Hued(Color hue)
    {
        var p = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        RowHue.SetHue(p, hue);
        return p;
    }

    private static (Grid Row, RowLabel Label, RowValue? Value) Row(string text, EmblemKind kind, double value, bool on,
        (double Min, double Max, double Val)? slider = null, string? number = null, string? unit = null)
    {
        var row = new Grid();
        row.SetResourceReference(FrameworkElement.StyleProperty, "OptionRow");
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

        var label = new RowLabel { Text = text, Emblem = kind, Value = value, IsOn = on };
        row.Children.Add(label);

        FrameworkElement control;
        if (slider is { } s)
        {
            var sl = new Slider { Minimum = s.Min, Maximum = s.Max, Value = s.Val, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            sl.SetResourceReference(FrameworkElement.StyleProperty, "PinkSlider");
            control = sl;
        }
        else
        {
            control = new CheckBox { IsChecked = on, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        }
        Grid.SetColumn(control, 1);
        row.Children.Add(control);

        RowValue? rv = null;
        if (number != null)
        {
            rv = new RowValue { Text = number, Unit = unit ?? string.Empty };
            Grid.SetColumn(rv, 2);
            row.Children.Add(rv);
        }
        return (row, label, rv);
    }

    private static uint RowHueArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
}
