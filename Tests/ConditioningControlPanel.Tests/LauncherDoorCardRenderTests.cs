using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The launcher's two door cards, built offscreen through the same class the window uses. Set
/// CCP_DOORS_SHOT_DIR to also write PNGs (rest, hover, mid-open) for a look.
/// </summary>
public class LauncherDoorCardRenderTests
{
    private static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
    private static readonly Color Blue = Color.FromRgb(0x55, 0xB7, 0xFF);

    [Fact]
    public void Display_font_resolves_to_the_bundled_Fredoka()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var face = new Typeface(LauncherDoorCard.DisplayFont, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            Assert.True(face.TryGetGlyphTypeface(out var glyphs));
            Assert.Contains("fredoka", glyphs.FontUri.ToString(), StringComparison.OrdinalIgnoreCase);
        });

    [Fact]
    public void Door_card_lays_out_with_title_count_and_chevron()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var card = Build("Games", 9, "{0} games", Pink, stamp: true);
            Layout(card.Root, 760, 380);
            Assert.True(card.Card.ActualHeight > 300);
            Assert.True(card.Card.Focusable);
            Assert.False(card.ReelRunning);
            card.StopReel();
            card.Rest();
            Assert.Equal(1, card.Root.Opacity);
        });

    [Fact]
    public void Shot_WhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_DOORS_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        WpfRenderHarness.OnStaThread(() =>
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "font-probe.txt"), Probe());
            Save(Column(null), 800, 880, Path.Combine(dir, "doors-rest.png"));
            Save(Column(games => { games.HoldPose(true, 1, 1); }, tools => tools.HoldPose(false, 0.985, 0.78)), 800, 880, Path.Combine(dir, "doors-hover-games.png"));
            Save(Column(games => games.HoldPose(true, 1.05, 0.6), tools => tools.HoldPose(false, 0.94, 0.35)), 800, 880, Path.Combine(dir, "doors-mid-open.png"));
        });
    }

    private static string Probe()
    {
        string Uri(FontFamily f)
        {
            var face = new Typeface(f, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            return face.TryGetGlyphTypeface(out var g) ? g.FontUri.ToString() : "(none)";
        }
        return "display: " + Uri(LauncherDoorCard.DisplayFont) + Environment.NewLine
             + "bare code string: " + Uri(new FontFamily("/Fonts/#Fredoka, Segoe UI")) + Environment.NewLine;
    }

    private static Border Column(Action<LauncherDoorCard>? games, Action<LauncherDoorCard>? tools = null)
    {
        var host = new UniformGrid { Columns = 1, Margin = new Thickness(16) };
        var g = Build("Games", 9, "{0} games", Pink, stamp: true, extra: true);
        var t = Build("Tools", 7, "{0} tools", Blue, stamp: false);
        host.Children.Add(g.Root);
        host.Children.Add(t.Root);
        games?.Invoke(g);
        tools?.Invoke(t);
        return new Border { Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33)), Child = host };
    }

    private static LauncherDoorCard Build(string title, int count, string format, Color hue, bool stamp, bool extra = false)
    {
        var app = Application.Current;
        var surface = ((SolidColorBrush)app.FindResource("SurfaceBgBrush")).Color;
        FrameworkElement? pill = null;
        if (stamp)
        {
            pill = new Border
            {
                Background = (Brush)app.FindResource("AccentGradientBrush"),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(11, 4, 12, 5),
                Child = new TextBlock { Text = "2 new", FontFamily = LauncherDoorCard.DisplayFont, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White },
            };
        }
        FrameworkElement? go = null;
        if (extra)
        {
            go = new Border
            {
                Height = 46, MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 22, 0, 0),
                CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(LauncherDoorCardColors.Lighten(hue, 0.3)),
                Child = new TextBlock
                {
                    Text = "Continue: Racing Thoughts", FontFamily = LauncherDoorCard.DisplayFont, FontSize = 16, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(LauncherDoorCardColors.Lighten(hue, 0.6)),
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 20, 0),
                },
            };
        }
        return new LauncherDoorCard(new DoorCardSpec(
            title, count, format, hue, Arts(hue), surface,
            new SolidColorBrush(Color.FromArgb(0x99, hue.R, hue.G, hue.B)),
            (Brush)app.FindResource("TextSecondaryBrush"),
            ((SolidColorBrush)app.FindResource("TextLightBrush")).Color,
            pill, go, Glow: true));
    }

    /// <summary>Stand-in tile art (the worktree carries no LFS media): bright shapes in the hue.</summary>
    private static IReadOnlyList<ImageSource> Arts(Color hue)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(hue, Color.FromRgb(0x30, 0x10, 0x50), 30), null, new Rect(0, 0, 720, 405));
            for (int i = 0; i < 9; i++)
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x90, 255, (byte)(120 + i * 12), (byte)(200 - i * 10))), null,
                    new Point(80 + i * 75, 120 + (i % 3) * 90), 40 + i * 4, 40 + i * 4);
        }
        var bmp = new RenderTargetBitmap(720, 405, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return new ImageSource[] { bmp };
    }

    private static void Layout(FrameworkElement e, double w, double h)
    {
        e.Measure(new Size(w, h));
        e.Arrange(new Rect(0, 0, w, h));
        e.UpdateLayout();
    }

    private static void Save(FrameworkElement e, double w, double h, string path)
    {
        Layout(e, w, h);
        var bmp = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
