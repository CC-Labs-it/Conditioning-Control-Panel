using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Rows;
using ConditioningControlPanel.Emblems;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Readable Rows parts on the Avalonia head (WPF bb40f9edba, 26e7b33aa4, 4511e6e4c5, 3d646a89f0,
/// 5ed0725ee6): the label and number tints follow the inherited section hue with the Core maths, an
/// emblem drops the label's leading emoji, an OFF row draws dim and grey, long labels wrap and never
/// trim, the bundled Nunito resolves to its own Bold and ExtraBold faces, the sticker draws with the
/// shipped edge, and only a box that names its own hue fills its sliders on a page that opted in.
/// </summary>
public sealed class RowPartsTests
{
    private static readonly Color Green = Color.FromArgb(0xFF, 0x5C, 0xE0, 0xA5);

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Window Show(Control content, double width = 400)
    {
        var w = new Window { Width = width, Height = 300, Content = content };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return w;
    }

    private static Color Fg(TextBlock t) => Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color;

    [Fact]
    public Task The_label_and_number_wear_the_inherited_hue() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var label = new RowLabel { Text = "\U0001F419Hydra", Emblem = EmblemKind.Often };
        var value = new RowValue { Text = "180", Unit = "an hour" };
        var box = new StackPanel { Children = { label, value } };
        RowHue.SetHue(box, Green);
        var w = Show(box);
        try
        {
            Assert.Equal(Green, label.EffectiveHue);
            Assert.Equal(Green, label.EmblemPart.EffectiveHue);
            Assert.Equal(Green, value.EffectiveHue);
            uint hue = RowHue.ToArgb(Green);
            var light = RowHue.FindBrush(label, "TextLightBrush") is ISolidColorBrush l ? RowHue.ToArgb(l.Color) : EmblemPalette.TextLight;
            Assert.Equal(RowHue.ToColor(EmblemPalette.LabelTint(light, hue)), Fg(label.TextPart));
            Assert.Equal(RowHue.ToColor(EmblemPalette.ValueTint(hue)), Fg(value.NumberPart));
            Assert.Equal("Hydra", label.ShownText);   // the emblem replaces the leading emoji
            Assert.Equal("180", value.NumberPart.Text);
            Assert.True(value.UnitPart.IsVisible);
            Assert.Equal("an hour", value.UnitPart.Text);

            // The section hue moves: the parts repaint without being touched.
            RowHue.SetHue(box, Colors.Transparent);
            var pink = label.EffectiveHue;
            Assert.NotEqual(Green, pink);
            Assert.Equal(RowHue.ToColor(EmblemPalette.ValueTint(RowHue.ToArgb(pink))), Fg(value.NumberPart));
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task An_off_row_is_dim_and_a_label_without_emblem_keeps_its_emoji() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var off = new RowLabel { Text = "Hydra", Emblem = EmblemKind.Splits, Subject = EmblemSubject.Video, Value = 3, IsOn = false };
        var plain = new RowLabel { Text = "\U0001F419Hydra" };
        var w = Show(new StackPanel { Children = { off, plain } });
        try
        {
            Assert.Equal(EmblemPalette.OffLabelOpacity, off.TextPart.Opacity, 6);
            Assert.False(off.EmblemPart.IsOn);
            Assert.Equal(EmblemSubject.Video, off.EmblemPart.Subject);
            Assert.False(plain.EmblemPart.IsVisible);
            Assert.Equal("\U0001F419Hydra", plain.ShownText);
            off.IsOn = true;
            Assert.Equal(1.0, off.TextPart.Opacity, 6);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task A_long_label_wraps_and_never_trims() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var label = new RowLabel { Text = "A very long option label that cannot possibly fit on one line here", Emblem = EmblemKind.Often, Width = 160 };
        var w = Show(new StackPanel { Children = { label } });
        try
        {
            Assert.Equal(TextWrapping.Wrap, label.TextPart.TextWrapping);
            Assert.Equal(TextTrimming.None, label.TextPart.TextTrimming);
            Assert.Equal(RowFonts.LabelSize, label.TextPart.FontSize);
            Assert.True(label.Bounds.Height > RowFonts.LabelSize * 2, "a wrapped label grows the row");
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Nunito_resolves_to_its_bundled_Bold_and_ExtraBold_faces() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        foreach (var weight in new[] { FontWeight.Bold, FontWeight.ExtraBold })
        {
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(RowFonts.Nunito, FontStyle.Normal, weight), out var face),
                "no glyph typeface for Nunito " + weight);
            Assert.StartsWith("Nunito", face!.FamilyName, StringComparison.Ordinal);
            Assert.Equal(weight, face.Weight);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_sticker_draws_with_the_shipped_edge_and_goes_grey_when_off() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var on = new OptionEmblem { Kind = EmblemKind.Most, Value = 5, Hue = Green, Width = 34, Height = 34 };
        var off = new OptionEmblem { Kind = EmblemKind.Most, Value = 5, Hue = Green, IsOn = false, Width = 34, Height = 34 };
        var w = Show(new StackPanel { Children = { on, off } });
        try
        {
            Assert.Equal(EmblemPalette.ShippedEdge, RowHue.GetEdge(on));
            Assert.Equal(Green, on.EffectiveHue);
            var a = Pixels(on);
            var b = Pixels(off);
            Assert.True(a.Count(p => p.A > 0) > 100, "the sticker painted");
            // On: some pixels carry the hue (green clearly above red); off: grey, none do.
            Assert.Contains(a, p => p.A > 200 && p.G > p.R + 40);
            Assert.DoesNotContain(b, p => p.A > 40 && p.G > p.R + 40);
            Assert.NotEmpty(OptionEmblem.Shapes(EmblemKind.Most, EmblemSubject.Image, 5));
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Only_a_box_that_names_its_hue_fills_its_sliders_on_an_opted_in_page() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var inner = new Border();
        var box = new Border { Child = inner };
        var page = new StackPanel { Children = { box } };
        var w = Show(page);
        try
        {
            RowHue.SetHue(box, Green);
            Assert.Null(RowHue.GetSliderFill(inner));           // page has not opted in
            RowHue.SetTintSliders(page, true);
            var fill = Assert.IsAssignableFrom<ISolidColorBrush>(RowHue.GetSliderFill(inner));
            Assert.Equal(Green, fill.Color);
            Assert.False(inner.IsSet(RowHue.SliderFillProperty)); // inherited, the child did not set it
            RowHue.SetTintSliders(page, false);
            Assert.Null(RowHue.GetSliderFill(inner));
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    private static Color[] Pixels(Control c)
    {
        var size = new PixelSize(34, 34);
        using var bmp = new RenderTargetBitmap(size);
        bmp.Render(c);
        var px = new byte[34 * 34 * 4];
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(px.Length);
        try
        {
            bmp.CopyPixels(new PixelRect(size), buffer, px.Length, 34 * 4);
            System.Runtime.InteropServices.Marshal.Copy(buffer, px, 0, px.Length);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
        // BGRA, premultiplied.
        return Enumerable.Range(0, 34 * 34).Select(i => Color.FromArgb(px[i * 4 + 3], px[i * 4 + 2], px[i * 4 + 1], px[i * 4])).ToArray();
    }
}
