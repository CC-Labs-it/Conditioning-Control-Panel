using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Announcements;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The paper promo card as it lays out with the contract's sample promo, in every language: it
/// fits a 1080p screen at 125% whole, and no single-line text (pills, countdown, tag) runs out of
/// its slot. Set <c>CCP_PROMO_DUMP</c> to a folder to also write PNGs at twice the size; set
/// <c>CCP_PROMO_SHOTS</c> to a folder of gameplay stills (effects.jpg, effects-b.jpg, tc-b.jpg) to
/// draw real stickers instead of flat test cards.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PromoCardShotTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 18, 48, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> Languages() =>
        new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" }.Select(l => new object[] { l });

    internal static PromoCard SamplePromo(int stickers = 2)
    {
        var ends = Now.AddDays(2).AddHours(4).AddMinutes(12).ToUnixTimeMilliseconds();
        var urls = new[] { "effects.jpg", "effects-b.jpg", "tc-b.jpg" }.Take(stickers).Select(n => $"\"https://cclabs.app/promo/bf50/{n}\"");
        var json = $$"""
            { "layout": "tc-paper", "tag": "BLACK FRIDAY", "hook": "Whirlpool, meet Steam.", "code": "BF50",
              "rows": [ { "label": "50% off", "sub": "Basic or Prime" }, { "label": "Prime half price", "sub": "first month or year" } ],
              "stickers": [ {{string.Join(",", urls)}} ], "ends_at": {{ends}},
              "cta": { "label": "Half off on Patreon", "url": "https://www.patreon.com/CodeBambi" } }
            """;
        using var doc = JsonDocument.Parse(json);
        return PromoCardRules.TryRead(doc.RootElement.Clone(), Now) ?? throw new InvalidOperationException("sample promo is invalid");
    }

    private const string Title = "12 hours of effects this month, Sam";
    private const string Message = "Prime stacks the Super effects (Whirlpool, Steam, Deck, Froth and more) and they react to each other. "
        + "The companion can fire them for you. Code BF50 takes half off the first month or year at patreon.com/CodeBambi, until Monday 30 Nov.";

    [Theory]
    [MemberData(nameof(Languages))]
    public void The_card_fits_a_small_screen_and_no_line_is_cut(string lang)
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(lang);
            WpfRenderHarness.OnStaThread(() =>
            {
                foreach (var count in lang == "en" ? new[] { 0, 1, 2, 3 } : new[] { 2 })
                {
                    var host = Realize(SamplePromo(count), Stickers(count));
                    var what = $"{lang}, {count} stickers";
                    if (Environment.GetEnvironmentVariable("CCP_PROMO_DUMP") is { Length: > 0 } dir)
                        Save(host, Path.Combine(dir, $"promo-{lang}-{count}.png"));
                    // 1080p at 125% leaves about 826 px of work area; the window adds 36 around the card.
                    Assert.True(host.ActualHeight + 36 <= 826, $"{what}: the card is {host.ActualHeight:0} px tall");
                    var lines = Descendants(host).OfType<TextBlock>()
                        .Where(t => t.IsVisible || t.Visibility == Visibility.Visible)
                        .Where(t => t.TextWrapping == TextWrapping.NoWrap && t.Text.Length > 0).ToList();
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

    [Fact]
    public void A_promo_of_only_layout_and_rows_draws_the_offer_and_Not_now()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            using var doc = JsonDocument.Parse("""{ "layout": "tc-paper", "rows": [ { "label": "50% off", "sub": "Basic or Prime" } ] }""");
            var promo = PromoCardRules.TryRead(doc.RootElement.Clone(), Now)!;
            var host = Realize(promo, Array.Empty<ImageSource?>());
            if (Environment.GetEnvironmentVariable("CCP_PROMO_DUMP") is { Length: > 0 } dir)
                Save(host, Path.Combine(dir, "promo-en-minimal.png"));
            var buttons = Descendants(host).OfType<Button>().Select(b => System.Windows.Automation.AutomationProperties.GetName(b)).ToList();
            Assert.Equal(new[] { Loc.Get("promo_not_now"), Loc.Get("btn_close") }.OrderBy(x => x), buttons.OrderBy(x => x));
            var texts = Descendants(host).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("50% off", texts);
            Assert.DoesNotContain(texts, t => t == Loc.Get("promo_ends_in").ToUpperInvariant());
        });
    }

    [Fact]
    public void The_card_fonts_are_real_weights_not_synthesised()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var (family, weight) in new[] { ("Fredoka", FontWeights.SemiBold), ("Fredoka", FontWeights.Medium), ("Courier Prime", FontWeights.Bold) })
            {
                var face = new Typeface(new FontFamily(new Uri("pack://application:,,,/"), $"./Fonts/Promo/#{family}"), FontStyles.Normal, weight, FontStretches.Normal);
                Assert.True(face.TryGetGlyphTypeface(out var glyphs), $"{family} {weight} is not embedded");
                Assert.Equal(weight, glyphs.Weight);
                Assert.Equal(StyleSimulations.None, glyphs.StyleSimulations);
            }
        });
    }

    private static Grid Realize(PromoCard promo, IReadOnlyList<ImageSource?> stickers)
    {
        var window = new PromoCardWindow("bf50-test", Title, Message, promo, () => Now, stickers);
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        window.Close();
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x26, 0x27, 0x33)) };
        host.Children.Add(root);
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        return host;
    }

    /// <summary>The stills when CCP_PROMO_SHOTS names a folder that has them, else flat test cards.</summary>
    private static IReadOnlyList<ImageSource?> Stickers(int count)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_PROMO_SHOTS");
        var names = new[] { "effects.jpg", "effects-b.jpg", "tc-b.jpg" };
        var tints = new[] { Color.FromRgb(0x8E, 0x3B, 0x8F), Color.FromRgb(0x3B, 0x6E, 0xC8), Color.FromRgb(0xE8, 0xA0, 0xC8) };
        return names.Take(count).Select((n, i) =>
        {
            var path = dir is { Length: > 0 } ? Path.Combine(dir, n) : null;
            if (path != null && File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 640;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return (ImageSource?)bmp;
            }
            var flat = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null,
                Enumerable.Repeat(new[] { tints[i].B, tints[i].G, tints[i].R, (byte)0 }, 4).SelectMany(p => p).ToArray(), 8);
            flat.Freeze();
            return flat;
        }).ToList();
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
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * 2), (int)Math.Ceiling(el.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
