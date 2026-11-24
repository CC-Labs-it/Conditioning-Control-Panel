using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Preview.Scenes;
using ConditioningControlPanel.Controls.Rows;
using ConditioningControlPanel.Emblems;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 fix round, wave 1 lane B (2026-11-24): the rows the 7.4 round added wear the Readable Rows
/// look once both are merged, and the Pink Filter preview obeys the Accessibility limit (#5606) the
/// way the real tint and fog do. Off screen, silent (RowsRender.Check asserts it), nothing clicked.
/// With CCP_ROWS_DUMP set the checks save w1-*.png.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsRoundTests
{
    private static readonly SKRect Screen = new(0, 0, 378, 1078);

    [Fact]
    public void The_accessibility_rows_on_performance_wear_the_rows_look() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new PerformanceSettingsSection();
        var labels = RowsRender.Check(page, "w1-performance", minLabels: 13,
            show: new[] { "PnlGentleChoices", "PnlPinkComfortLimit" },
            before: _ => page.ChkPinkComfortLimit.IsChecked = true);

        var limit = labels.Single(l => l.Emblem == EmblemKind.Gentle && ReferenceEquals(Row(l), Row(page.ChkPinkComfortLimit)));
        Assert.True(limit.IsOn, "the label follows its switch");
        Assert.Equal(page.FindResource("OptionRow"), page.PnlPinkComfortLimit.Style);
        Assert.Contains(page.PnlPinkComfortLimit.Children.OfType<RowLabel>(), l => l.Emblem == EmblemKind.Clear);
        Assert.IsType<RowValue>(page.TxtPinkComfortLimit);
        var hue = (Color)Application.Current.FindResource("SectionHuePerformance");
        Assert.All(labels, l => Assert.Equal(hue, l.EffectiveHue));
    });

    private static Grid? Row(FrameworkElement e) => e.Parent as Grid;

    [Fact]
    public void The_pink_clip_note_sits_under_the_opacity_label_text() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new PinkFilterFeatureControl();
        RowsRender.Check(page, "w1-pinkfilter", minLabels: 3, show: new[] { "TxtComfortClip" },
            before: _ => page.TxtComfortClip.Text = "Shown at 10%, your pink filter limit (Settings > Performance).");
        var note = page.TxtComfortClip;
        var opacity = RowsRender.Descendants(page).OfType<RowLabel>().First(l => l.Emblem == EmblemKind.Clear);
        // The note starts where the label's text starts (after the emblem), not under the emblem.
        double noteX = note.TranslatePoint(new Point(), page).X;
        double textX = opacity.TextPart.TranslatePoint(new Point(), page).X;
        Assert.InRange(noteX, textX - 2, textX + 2);
        Assert.Equal(RowFonts.Nunito, note.FontFamily);
        Assert.Equal(RowFonts.UnitSize, note.FontSize);
    });

    [Fact]
    public void The_pink_preview_never_paints_past_the_comfort_limit()
    {
        var s = new AppSettings { PinkFilterOpacity = 50, PinkComfortLimitOn = true, PinkComfortLimitPct = 10 };
        using var scene = new PinkFilterPreviewScene(() => s, () => MotionLevel.Full, () => false, () => false, () => (255, 105, 180));
        scene.Layout(Screen, Screen);
        scene.Start();
        for (int i = 0; i < 60; i++) scene.Tick(1 / 30.0);
        Assert.InRange(scene.TintNow.Alpha, 0, (int)(0.10 * 255) + 1);

        // Limit off: the slider's own value paints.
        s.PinkComfortLimitOn = false;
        Assert.InRange(scene.TintNow.Alpha, (int)(0.50 * 255) - 2, 255);
    }

    [Fact]
    public void The_preview_fog_is_held_to_the_limit_too()
    {
        var s = new AppSettings { PinkFilterOpacity = 5, SteamReach = 100, SuperCreepCap = 0.9, PinkComfortLimitOn = true, PinkComfortLimitPct = 10 };
        using var scene = new PinkFilterPreviewScene(() => s, () => MotionLevel.Full, () => false, () => true, () => (255, 105, 180));
        scene.Layout(Screen, Screen);
        scene.Start();
        for (int i = 0; i < 180; i++) scene.Tick(1 / 30.0);
        using var bmp = new SKBitmap(new SKImageInfo((int)Screen.Width, (int)Screen.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var c = new SKCanvas(bmp)) { c.Clear(SKColors.Transparent); scene.Render(c, Screen, Screen); c.Flush(); }
        // Tint (5%) under fog (held to 10%): source-over of two layers each within the limit.
        double max = 1 - (1 - 0.05) * (1 - 0.10);
        for (int y = 0; y < bmp.Height; y += 37)
            for (int x = 0; x < bmp.Width; x += 13)
                Assert.True(bmp.GetPixel(x, y).Alpha <= max * 255 + 3, $"alpha {bmp.GetPixel(x, y).Alpha} at {x},{y}");
    }
}
