using System.Linq;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows sweep, lane R2 (2026-11-24): the Bubbles (+ Froth), Bubble Count, Bouncing Text
/// (+ Scrawl) and Subliminals (+ Afterglow) pages realized off screen at 900 px with every fold open
/// and every hidden option row shown, checked by the shared <see cref="RowsRender"/> rules. With env
/// CCP_ROWS_DUMP=&lt;dir&gt; each saves a still. Silent: none of these pages starts a player on build.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsR2Tests
{
    private static readonly Color Gold = Color.FromRgb(0xFF, 0xE0, 0x8A);

    [Fact]
    public void Bubbles_rows_wear_emblems_and_fit()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new BubblePopFeatureControl();
            var labels = RowsRender.Check(page, "r2-bubbles", minLabels: 21,
                show: new[] { "TriggerOptionsPanel", "FrothRows", "V2Box", "MotionRow", "ChkBrainDrainBubble" },
                before: _ =>
                {
                    page.RowGetBubblesV2.Visibility = Visibility.Collapsed;
                    page.SliderFreq.Value = 30; page.TxtFreq.Text = "30";
                    page.ChkTriggers.IsChecked = true;
                    page.ChkTypeFlash.IsChecked = true;
                    page.ChkTypeVideo.IsChecked = false;
                    SuperRows.Apply(page.FrothRows, SuperEffect.InnerBloom);
                    page.FrothRows.Visibility = Visibility.Visible;
                });

            var often = labels.Single(l => l.Emblem == EmblemKind.Often);
            Assert.Equal(30.0 / 60, often.Value, 3);
            Assert.Equal("a minute", page.TxtFreq.Unit);
            Assert.True(labels.Single(l => l.Text == "Flash").IsOn);
            Assert.False(labels.Single(l => l.Text == "Video").IsOn);
            Assert.True(labels.Single(l => l.Emblem == EmblemKind.After).IsOn);
            // The Froth rows take the gold of their box and still dim and lock as a block.
            Assert.Equal(Gold, labels.Single(l => l.Emblem == EmblemKind.Reacts).EffectiveHue);
            Assert.True(page.FrothRows.Opacity is 1 or SuperRows.LockedOpacity);
        });
    }

    [Fact]
    public void Bubble_count_rows_wear_emblems_and_fit()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new BubbleCountFeatureControl();
            var labels = RowsRender.Check(page, "r2-bubblecount", minLabels: 4,
                before: _ => { page.SliderFreq.Value = 5; page.TxtFreq.Text = "5"; page.ChkStrict.IsChecked = true; });
            Assert.Equal(0.5, labels.Single(l => l.Emblem == EmblemKind.Often).Value, 3);
            Assert.Equal("an hour", page.TxtFreq.Unit);
            Assert.True(labels.Single(l => l.Emblem == EmblemKind.Lock).IsOn);
        });
    }

    [Fact]
    public void Bouncing_text_rows_wear_emblems_and_fit()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new BouncingTextFeatureControl();
            var labels = RowsRender.Check(page, "r2-bouncingtext", minLabels: 19,
                before: _ =>
                {
                    page.ChkFxSpin.IsChecked = true;
                    page.SliderScrawlCap.Value = 30; page.TxtScrawlCap.Text = "30";
                    SuperRows.Apply(page.ScrawlRows, SuperEffect.Scrawl);
                    page.ScrawlRows.Visibility = Visibility.Visible;
                });
            var most = labels.Single(l => l.Emblem == EmblemKind.Most);
            Assert.Equal(30, most.Value);
            Assert.Equal("at most", page.TxtScrawlCap.Unit);
            Assert.Equal(Gold, most.EffectiveHue);
            Assert.True(page.ScrawlRows.Opacity is 1 or SuperRows.LockedOpacity);
            // The effect switches stand one per line now: each sits in its own row, left aligned.
            var motion = labels.Where(l => l.Emblem == EmblemKind.Motion).ToList();
            Assert.Equal(5, motion.Count);
            var x0 = motion[0].TranslatePoint(new Point(0, 0), page).X;
            Assert.All(motion, l => Assert.Equal(x0, l.TranslatePoint(new Point(0, 0), page).X, 1));
        });
    }

    [Fact]
    public void Subliminal_rows_wear_emblems_and_fit()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var page = new SubliminalFeatureControl();
            var labels = RowsRender.Check(page, "r2-subliminal", minLabels: 11,
                show: new[] { "AfterglowRows" },
                before: _ =>
                {
                    page.SliderAfterglowWords.Value = 3;
                    SuperRows.Apply(page.AfterglowRows, SuperEffect.Afterglow);
                    page.AfterglowRows.Visibility = Visibility.Visible;
                });
            // The whisper switch's emoji goes; the words row counts words; the gold box rows are gold.
            Assert.DoesNotContain(labels, l => l.ShownText.StartsWith("\U0001F4CA"));
            var words = labels.Single(l => l.Emblem == EmblemKind.Many);
            Assert.Equal(EmblemSubject.Word, words.Subject);
            Assert.Equal(3, words.Value);
            Assert.Equal(Gold, words.EffectiveHue);
        });
    }

    [Fact]
    public void Afterglow_option_box_rows_wear_emblems_and_fit()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            // Folded and collapsed in the Subliminals page since take two; its rows still follow the rules.
            var box = new AfterglowOptionsBox();
            RowsRender.Check(box, "r2-afterglow-box", minLabels: 6, show: new[] { "Body" });
        });
    }
}
