using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The language picker's flags (<see cref="LanguageFlag"/>): every language the app offers has
/// one, each one draws something of its own, and the picker shows it in the dropdown rows and in
/// the closed box.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LanguageFlagTests
{
    [Fact]
    public void EveryAvailableLanguageHasAFlag()
    {
        var missing = LocalizationManager.AvailableLanguages
            .Where(l => !LanguageFlag.Has(l.Code) || LanguageFlag.For(l.Code) == null)
            .Select(l => l.Code)
            .ToList();
        Assert.True(missing.Count == 0, "no flag for: " + string.Join(", ", missing));
    }

    [Fact]
    public void AnUnknownCodeHasNoFlagRatherThanSomeoneElses()
    {
        Assert.Null(LanguageFlag.For("xx"));
        Assert.Null(LanguageFlag.For(""));
        Assert.Null(LanguageFlag.For(null));
        // The region part never borrows: "pt-BR" and a plain "pt" share Brazil's painter.
        Assert.Same(LanguageFlag.For("pt-BR"), LanguageFlag.For("pt"));
    }

    [Fact]
    public void EachFlagIsTwentyByFourteenAndDrawsSomethingOfItsOwn() => WpfRenderHarness.OnStaThread(() =>
    {
        var seen = new Dictionary<string, string>();
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var image = LanguageFlag.For(code)!;
            Assert.Equal(LanguageFlag.FlagWidth, image.Width, 3);
            Assert.Equal(LanguageFlag.FlagHeight, image.Height, 3);

            var pixels = Render(image, 4);
            var opaque = 0;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 200) opaque++;
            Assert.True(opaque > pixels.Length / 4 * 0.8, $"{code}: flag is mostly blank");

            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(pixels));
            Assert.False(seen.TryGetValue(hash, out var twin), $"{code} draws the same flag as {twin}");
            seen[hash] = code;
        }
    });

    [Fact]
    public void ThePickerShowsTheFlagInTheRowsAndInTheClosedBox() => WpfRenderHarness.OnStaThread(() =>
    {
        var combo = new ComboBox { Width = 190, Height = 28, FontSize = 11 };
        if (Application.Current?.TryFindResource("DarkComboBoxStyle") is Style dark) combo.Style = dark;
        MainWindow.FillLanguageCombo(combo, shortLabels: false);

        foreach (ComboBoxItem item in combo.Items)
        {
            var option = Assert.IsType<LanguageOption>(item.Content);
            Assert.Equal(item.Tag as string, option.Code);
            Assert.NotNull(option.Flag);
            Assert.NotNull(item.ContentTemplate);
        }

        combo.SelectedIndex = LocalizationManager.AvailableLanguages.ToList().FindIndex(l => l.Code == "it");
        Assert.IsType<LanguageOption>(combo.SelectionBoxItem);
        Assert.NotNull(combo.SelectionBoxItemTemplate);

        // The closed box really paints the flag: Italy's green shows up left of the name.
        var host = new Border { Background = Brushes.Black, Child = combo };
        host.Measure(new Size(190, 28));
        host.Arrange(new Rect(0, 0, 190, 28));
        host.UpdateLayout();
        var bmp = new RenderTargetBitmap(190 * 2, 28 * 2, 192, 192, PixelFormats.Pbgra32);
        bmp.Render(host);
        var pixels = new byte[190 * 2 * 28 * 2 * 4];
        bmp.CopyPixels(pixels, 190 * 2 * 4, 0);
        bool green = false;
        for (int i = 0; i < pixels.Length && !green; i += 4)
            green = Math.Abs(pixels[i] - 0x46) < 20 && Math.Abs(pixels[i + 1] - 0x92) < 20 && pixels[i + 2] < 30;
        Assert.True(green, "the closed language box shows no Italian flag");
    });

    /// <summary>Look check, not a judge. Does nothing without CCP_FLAG_SHEET (a PNG path).</summary>
    [Fact]
    public void FlagSheetWhenAsked() => WpfRenderHarness.OnStaThread(() =>
    {
        var path = Environment.GetEnvironmentVariable("CCP_FLAG_SHEET");
        if (string.IsNullOrWhiteSpace(path)) return;

        var panel = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)) };
        foreach (var (code, name, _) in LocalizationManager.AvailableLanguages)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 4) };
            row.Children.Add(new Image { Source = LanguageFlag.For(code), Width = 20, Height = 14, Margin = new Thickness(0, 0, 7, 0) });
            row.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
        }
        panel.Measure(new Size(200, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 200, panel.DesiredSize.Height));
        panel.UpdateLayout();
        var bmp = new RenderTargetBitmap(200 * 4, (int)Math.Ceiling(panel.DesiredSize.Height) * 4, 384, 384, PixelFormats.Pbgra32);
        bmp.Render(panel);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = System.IO.File.Create(path);
        enc.Save(f);
    });

    private static byte[] Render(ImageSource image, int scale)
    {
        int w = (int)(LanguageFlag.FlagWidth * scale), h = (int)(LanguageFlag.FlagHeight * scale);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(image, new Rect(0, 0, LanguageFlag.FlagWidth, LanguageFlag.FlagHeight));
        var bmp = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var pixels = new byte[w * h * 4];
        bmp.CopyPixels(pixels, w * 4, 0);
        return pixels;
    }
}
