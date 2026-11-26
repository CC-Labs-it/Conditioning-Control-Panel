using System.Windows.Controls;
using System.Windows.Data;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 desk run (wave 4, lane D item 1): the effect page hero title is an <see cref="EmojiTextBlock"/>
/// bound with <c>{loc:Str ...}</c>. It rebuilds its own Inlines to swap the colour emoji for a picture,
/// and a TextBlock that changes its Inlines writes its Text back as a local value, which threw the
/// language binding away after the first switch: the Flash page kept "Flash-Bilder" in Japanese and
/// in English again. The title must follow every later switch.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class EmojiTextBlockLanguageTests
{
    private static string Shown(TextBlock tb)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in tb.Inlines)
            if (inline is System.Windows.Documents.Run run) sb.Append(run.Text);
        return sb.ToString().Trim();
    }

    private static string Plain(string key) =>
        LocalizationManager.Instance.Get(key).Replace("⚡", "").Trim();

    [Fact]
    public void A_bound_title_follows_every_language_switch()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var loc = LocalizationManager.Instance;
            var previous = loc.CurrentLanguage;
            try
            {
                loc.SetLanguage("en");
                var tb = new EmojiTextBlock();
                tb.SetBinding(TextBlock.TextProperty, new Binding("[section_flash_images]")
                {
                    Source = loc,
                    Mode = BindingMode.OneWay,
                });
                // XAML sets the attributes in source order: Text, then FontSize (which rebuilds).
                tb.FontSize = 17;
                tb.FontWeight = System.Windows.FontWeights.Bold;
                Assert.Equal(Plain("section_flash_images"), Shown(tb));
                Assert.Single(tb.Inlines, i => i is System.Windows.Documents.InlineUIContainer);

                foreach (var lang in new[] { "de", "ja", "en", "de" })
                {
                    loc.SetLanguage(lang);
                    Assert.Equal(Plain("section_flash_images"), Shown(tb));
                    Assert.Single(tb.Inlines, i => i is System.Windows.Documents.InlineUIContainer);
                }
            }
            finally { loc.SetLanguage(previous); }
        });
    }

    [Fact]
    public void The_flash_page_hero_title_follows_every_language_switch()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var loc = LocalizationManager.Instance;
            var previous = loc.CurrentLanguage;
            try
            {
                loc.SetLanguage("en");
                var page = new ConditioningControlPanel.Features.FlashFeatureControl();
                var host = RowsRender.Host(page, 900);
                var hero = System.Linq.Enumerable.First(
                    System.Linq.Enumerable.OfType<EmojiTextBlock>(RowsRender.Descendants(page)));
                Assert.Equal(Plain("section_flash_images"), Shown(hero));

                foreach (var lang in new[] { "de", "ja", "en" })
                {
                    loc.SetLanguage(lang);
                    RowsRender.Layout(host, 900);
                    Assert.Equal(Plain("section_flash_images"), Shown(hero));
                }
            }
            finally { loc.SetLanguage(previous); }
        });
    }

    [Fact]
    public void A_plain_text_still_takes_its_emoji_picture()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tb = new EmojiTextBlock { Text = "⚡ Flash Images" };
            Assert.Equal("Flash Images", Shown(tb));
            Assert.Single(tb.Inlines, i => i is System.Windows.Documents.InlineUIContainer);

            tb.Text = "⚡ Other";
            Assert.Equal("Other", Shown(tb));
        });
    }
}
