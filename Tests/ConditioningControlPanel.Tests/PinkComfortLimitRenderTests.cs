using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Settings > Performance > Accessibility: the "Limit pink filter to" rows realize, their names
/// are reachable, and the slider row is folded until the switch is on. The pink filter page's
/// clip note exists and starts hidden. Set <c>CCP_PINKCAP_PNG_DIR</c> to also write offscreen
/// renders (switch on, and the note showing) for review.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class PinkComfortLimitRenderTests
{
    private static Grid Realize(FrameworkElement element, double width)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(width, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, width, Math.Max(1, host.DesiredSize.Height)));
        host.UpdateLayout();
        return host;
    }

    private static void MaybeRender(FrameworkElement el, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_PINKCAP_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(el.ActualWidth);
        var h = (int)Math.Ceiling(el.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    [Fact]
    public void The_accessibility_rows_realize_folded_until_switched_on()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var s = new PerformanceSettingsSection();
            var host = Realize(s, 760);
            Assert.NotNull(s.ChkPinkComfortLimit);
            Assert.NotNull(s.SliderPinkComfortLimit);
            Assert.NotNull(s.TxtPinkComfortLimit);
            Assert.Equal(Visibility.Collapsed, s.PnlPinkComfortLimit.Visibility);
            Assert.NotEqual(true, s.ChkPinkComfortLimit.IsChecked);   // off by default
            Assert.Equal(5, s.SliderPinkComfortLimit.Minimum);
            Assert.Equal(50, s.SliderPinkComfortLimit.Maximum);
            Assert.Equal(10, s.SliderPinkComfortLimit.Value);

            // Review render: the switch on, its slider showing.
            s.ChkPinkComfortLimit.IsChecked = true;
            s.PnlPinkComfortLimit.Visibility = Visibility.Visible;
            host.Measure(new Size(760, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, 760, host.DesiredSize.Height));
            host.UpdateLayout();
            MaybeRender(host, "performance-accessibility.png");
        });

    [Fact]
    public void The_pink_filter_page_has_a_hidden_clip_note()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var page = new PinkFilterFeatureControl();
            var host = Realize(page, 760);
            var note = Assert.IsType<TextBlock>(page.FindName("TxtComfortClip"));
            Assert.Equal(Visibility.Collapsed, note.Visibility);

            // Review render: the note as it reads with a 30% filter and a 10% limit.
            if (page.FindName("SliderOpacity") is Slider slider) slider.Value = 30;
            if (page.FindName("TxtOpacity") is TextBlock pct) pct.Text = "30%";
            note.Text = string.Format(Localization.Loc.Get("hint_pink_comfort_clip"), 10);
            note.Visibility = Visibility.Visible;
            host.Measure(new Size(760, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, 760, host.DesiredSize.Height));
            host.UpdateLayout();
            MaybeRender(host, "pink-filter-clip-note.png");
        });
}
