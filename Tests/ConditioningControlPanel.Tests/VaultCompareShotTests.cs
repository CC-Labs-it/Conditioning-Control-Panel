using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Localization;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The tier compare face (two tickets) as it lays out: in every language, monthly and yearly, the
/// card fits a 1280x720 window without scrolling and no single-line text runs out of its slot.
/// Set <c>CCP_GATE_DUMP</c> to a folder to also write PNGs of every language, monthly and
/// yearly, at the size the card shows at.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class VaultCompareShotTests
{
    public static IEnumerable<object[]> Languages() =>
        new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" }.Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Languages))]
    public void The_compare_card_fits_a_small_window_whole(string lang)
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(lang);
            WpfRenderHarness.OnStaThread(() =>
            {
                foreach (var yearly in new[] { false, true })
                {
                    var (host, card) = Realize(yearly);
                    var what = $"{lang} {(yearly ? "yearly" : "monthly")}";
                    if (Environment.GetEnvironmentVariable("CCP_GATE_DUMP") is { Length: > 0 } dir)
                        Save(host, Path.Combine(dir, $"compare-{lang}-{(yearly ? "yearly" : "monthly")}.png"));
                    // the window is the card plus its 12 px margin: 720 px less a 48 px taskbar
                    Assert.True(card.ActualHeight + 24 <= 672, $"{what}: the card is {card.ActualHeight:0} px tall");
                    Assert.True(card.ActualWidth + 24 <= 1280, $"{what}: the card is {card.ActualWidth:0} px wide");
                    var lines = Descendants(card).OfType<TextBlock>()
                        .Where(t => t.Visibility == Visibility.Visible && t.TextWrapping == TextWrapping.NoWrap && t.Text.Length > 0).ToList();
                    Assert.NotEmpty(lines);
                    foreach (var t in lines)
                    {
                        var probe = new TextBlock { Text = t.Text, FontSize = t.FontSize, FontFamily = t.FontFamily, FontWeight = t.FontWeight };
                        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Assert.True(t.ActualWidth + 0.5 >= probe.DesiredSize.Width, $"{what}: '{t.Text}' needs {probe.DesiredSize.Width:0} px, gets {t.ActualWidth:0}");
                    }
                }
            });
        }
        finally { loc.SetLanguage(previous); }
    }

    private static (Grid Host, FrameworkElement Card) Realize(bool yearly)
    {
        var card = VaultGateDialog.CompareCardForShot(yearly);
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x22, 0x3A)), Width = 980 };
        host.Children.Add(card);
        host.Measure(new Size(980, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        return (host, card);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static void Save(FrameworkElement el, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var w = (int)Math.Ceiling(el.ActualWidth);
        var h = (int)Math.Ceiling(el.ActualHeight);
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
