using System.IO;
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

    // ------------------------------------------------------------- gold Deck box name

    /// <summary>
    /// Why the off-screen Flash still drew the gold Deck box with no name: SuperBox paints its words in
    /// Refresh, which runs when Effect CHANGES or on Loaded. FlickerDeck is Effect's default value, so
    /// Effect="FlickerDeck" never fires the change callback, and an off-screen render never loads.
    /// Every other box (Vortex, Creep...) is a change and paints at construction. In the app the
    /// page loads, so the Deck box is named there: a still artefact, not a product bug.
    /// </summary>
    [Fact]
    public void The_deck_box_paints_its_words_on_load_not_at_construction() => WpfRenderHarness.OnStaThread(() =>
    {
        string Words(ConditioningControlPanel.Controls.SuperBox b) =>
            System.Windows.Automation.AutomationProperties.GetName(b.V2Toggle);

        var deck = new ConditioningControlPanel.Controls.SuperBox { Effect = ConditioningControlPanel.Services.Super.SuperEffect.FlickerDeck };
        var pool = new ConditioningControlPanel.Controls.SuperBox { Effect = ConditioningControlPanel.Services.Super.SuperEffect.Vortex };
        Assert.True(string.IsNullOrEmpty(Words(deck)), "the default effect never fires the change callback");
        Assert.False(string.IsNullOrEmpty(Words(pool)));
        deck.Refresh();   // what Loaded does in the app
        Assert.Equal(Words(pool), Words(deck));
    });

    // ------------------------------------------------------------- haptics width

    /// <summary>
    /// The narrowest width the panel ever lays Haptics out at. MainWindow draws a fixed 1585 x 901
    /// design canvas inside a Stretch="Fill" Viewbox, so resizing the window only scales it; column 1
    /// is always 1489. Haptics is hosted only in the Studio rack's detail pane (StudioTabView
    /// PanelHaptics; the rack routes "haptics" to ShowTab, never to FeaturePopupWindow, the 520 px
    /// popup): 1489 - 2 x 10 (StudioTab margin) - 230 (rack) - 14 (rack gap) = 1225. Less a 10 px
    /// scroll bar gives the width checked below.
    /// </summary>
    public const double HapticsNarrowest = 1489 - 2 * 10 - 230 - 14 - 10;

    [Fact]
    public void Haptics_is_never_laid_out_at_the_popup_width()
    {
        string Xaml(string rel) => File.ReadAllText(Path.Combine(Root(), rel));
        var main = Xaml(Path.Combine("MainWindow", "MainWindow.xaml"));
        Assert.Contains("<Viewbox Stretch=\"Fill\">", main);
        Assert.Contains("<Grid x:Name=\"DesignCanvas\" Width=\"1585\" Height=\"901\">", main);
        Assert.Contains("<ColumnDefinition Width=\"96\"/>", main);
        Assert.Contains("<views:StudioTabView x:Name=\"StudioTab\" Margin=\"10,5,10,10\"", main);
        var studio = Xaml(Path.Combine("Views", "Tabs", "StudioTabView.xaml"));
        Assert.Contains("<Border Grid.Column=\"0\" Width=\"230\" Margin=\"0,0,14,0\"", studio);
        Assert.Contains("<tabs:HapticsTabView x:Name=\"PanelHaptics\"", studio);
        // The only other host of feature pages is the popup, and nothing hands it Haptics.
        var hosts = Directory.EnumerateFiles(Root(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => File.ReadAllText(f).Contains("new HapticsTabView(") || File.ReadAllText(f).Contains("new Views.Tabs.HapticsTabView("))
            .ToList();
        Assert.Empty(hosts);
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("de")]
    [InlineData("pt-BR")]
    public void Haptics_rows_hold_at_the_narrowest_real_width(string lang)
    {
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage(lang, () =>
        {
            var page = RowsPageStillsTests.Make("haptics");
            var host = RowsRender.Host(page, HapticsNarrowest, show: new[] { "VideoHapticSyncSliders", "HapticAudioAdvanced" },
                before: p => { foreach (var ex in RowsRender.Descendants(p).OfType<Expander>().ToList()) ex.IsExpanded = true; });
            var labels = RowsRender.Descendants(page).OfType<RowLabel>().Where(l => l.IsVisible || l.ActualWidth > 0).ToList();
            Assert.True(labels.Count > 10, $"{labels.Count} labels");
            var problems = RowsRender.Problems(labels, squeeze: true);
            // A slider beside a label keeps a usable track, not just a sliver.
            foreach (var l in labels)
                if (l.Parent is Grid row)
                    foreach (var s in row.Children.OfType<Slider>().Where(s => s.Visibility == Visibility.Visible))
                        if (s.ActualWidth < 120) problems.Add($"'{l.ShownText}' leaves its slider {s.Name} {s.ActualWidth:0} px");
            RowsRender.Dump(host, $"w1-haptics-{lang}-{HapticsNarrowest:0}");
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            return 0;
        }));
        RowsRender.AssertSilent("w1-haptics-" + lang);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

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
