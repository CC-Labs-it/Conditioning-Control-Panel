using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Super Lights Down drawn offscreen through the real overlay (the small picture, stretched by WPF)
/// over stand-ins: a full-screen picture, or a desktop with floating video windows laid ON TOP the
/// way the z-order puts them. Set CCP_LIGHTSDOWN_SHOT to a folder to also save each frame as a PNG
/// for a look by eye.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LightsDownRenderTests
{
    private const int W = 1920, H = 1080;
    private const double Dt = 1.0 / 30;
    private const uint Pink = 0xFF69B4;

    private static RenderTargetBitmap Shoot(string name, LightsDownLook look, IReadOnlyList<LightsDownHole> holes,
        bool desktop, IReadOnlyList<Rect>? floaters = null, bool flat = false)
    {
        var o = LightsDownOverlay.ForTest(W, H);
        o.Render(look, holes, true);
        o.Content = null;   // lift the drawing out of the never-shown window

        var stage = new Grid { Width = W, Height = H };
        stage.Children.Add(flat ? new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(200, 200, 205)) } : desktop ? Desktop() : Picture());
        stage.Children.Add(o.RootForTest);
        if (floaters != null)
        {
            var top = new Canvas();
            int k = 0;
            foreach (var r in floaters)
            {
                var b = new Border
                {
                    Width = r.Width, Height = r.Height, CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(4), BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)),
                    Background = VideoBrush(k++),
                };
                Canvas.SetLeft(b, r.X); Canvas.SetTop(b, r.Y);
                top.Children.Add(b);
            }
            stage.Children.Add(top);
        }
        stage.Measure(new Size(W, H));
        stage.Arrange(new Rect(0, 0, W, H));
        stage.UpdateLayout();
        var rtb = new RenderTargetBitmap(W, H, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(stage);

        var shot = Environment.GetEnvironmentVariable("CCP_LIGHTSDOWN_SHOT");
        if (!string.IsNullOrEmpty(shot))
        {
            Directory.CreateDirectory(shot);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(System.IO.Path.Combine(shot, name + ".png"));
            enc.Save(fs);
        }
        return rtb;
    }

    private static Brush VideoBrush(int k)
    {
        var (a, b) = (k % 3) switch
        {
            0 => (Color.FromRgb(40, 170, 190), Color.FromRgb(250, 150, 90)),
            1 => (Color.FromRgb(120, 60, 200), Color.FromRgb(240, 90, 150)),
            _ => (Color.FromRgb(60, 140, 80), Color.FromRgb(230, 220, 120)),
        };
        return new LinearGradientBrush(a, b, 35);
    }

    /// <summary>A full-screen clip stand-in: a bright gradient with a few shapes.</summary>
    private static UIElement Picture()
    {
        var g = new Grid();
        g.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromRgb(70, 180, 210), Color.FromRgb(250, 190, 120), 25) });
        g.Children.Add(new Ellipse { Width = 520, Height = 520, Fill = new SolidColorBrush(Color.FromArgb(150, 255, 240, 200)) });
        return g;
    }

    /// <summary>A desktop stand-in: a light wallpaper and two app windows with lines of "text".</summary>
    private static UIElement Desktop()
    {
        var c = new Canvas { Background = new LinearGradientBrush(Color.FromRgb(180, 200, 225), Color.FromRgb(215, 205, 230), 90) };
        foreach (var (x, y, w, h) in new[] { (80.0, 70.0, 760.0, 520.0), (980.0, 380.0, 860.0, 600.0) })
        {
            var win = new Border { Width = w, Height = h, Background = Brushes.White, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            var lines = new StackPanel { Margin = new Thickness(24, 50, 24, 0) };
            for (int i = 0; i < 12; i++)
                lines.Children.Add(new Rectangle { Height = 10, Width = w * (0.5 + 0.4 * ((i * 37) % 10) / 10.0), Fill = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 18), HorizontalAlignment = HorizontalAlignment.Left });
            win.Child = lines;
            Canvas.SetLeft(win, x); Canvas.SetTop(win, y);
            c.Children.Add(win);
        }
        return c;
    }

    private static (int R, int G, int B) At(BitmapSource bmp, int x, int y)
    {
        var px = new byte[4];
        bmp.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return (px[2], px[1], px[0]);
    }

    private static int Lum(BitmapSource bmp, int x, int y)
    {
        var (r, g, b) = At(bmp, x, y);
        return (r + g + b) / 3;
    }

    private static LightsDownFullScreen Rested()
    {
        var s = new LightsDownFullScreen();
        for (int i = 0; i < 200; i++) s.Step(Dt, false, false, LightsDownTuning.Stock, 0);
        return s;
    }

    private static double Moving(double seconds, double gentle)
    {
        var s = Rested();
        for (double x = 0; x < seconds; x += Dt) s.Step(Dt, true, false, LightsDownTuning.Stock, gentle);
        return s.Level;
    }

    private static LightsDownHoles Holes(IReadOnlyList<Rect> rects)
    {
        var h = new LightsDownHoles();
        var live = new List<(long, LightsDownRect, bool)>();
        for (int i = 0; i < rects.Count; i++) live.Add((i + 1, new LightsDownRect(rects[i].X, rects[i].Y, rects[i].Width, rects[i].Height), true));
        for (int i = 0; i < 30; i++) h.Update(Dt, live, 0);
        return h;
    }

    [Fact]
    public void Full_screen_at_rest_dims_only_the_edges()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var plain = Shoot("plain-picture", LightsDownLook.None, Array.Empty<LightsDownHole>(), false);
            var look = LightsDownFullScreen.Look(Rested().Level, LightsDownTuning.Stock, LightsDownMotion.Full, H);
            var rtb = Shoot("fullscreen-rest", look, Array.Empty<LightsDownHole>(), false);
            Assert.Equal(Lum(plain, W / 2, H / 2), Lum(rtb, W / 2, H / 2));
            Assert.Equal(Lum(plain, W / 2, H / 3), Lum(rtb, W / 2, H / 3));
            Assert.True(Lum(rtb, 3, H / 2) < Lum(plain, 3, H / 2) * 0.5, "the edge goes down");
            Assert.True(Lum(rtb, 4, 4) < 60, "the corners are darkest");
            Assert.True(Lum(rtb, 80, H / 2) < Lum(plain, 80, H / 2), "a soft band, not a line");
        });
    }

    [Fact]
    public void Full_screen_with_the_mouse_moving_has_pulled_back_and_gentle_holds_it_longer()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var t = LightsDownTuning.Stock;
            var rest = Shoot("fullscreen-rest-ref", LightsDownFullScreen.Look(Rested().Level, t, LightsDownMotion.Full, H), Array.Empty<LightsDownHole>(), false);
            var moving = Shoot("fullscreen-mouse-moving", LightsDownFullScreen.Look(Moving(0.2, 0), t, LightsDownMotion.Full, H), Array.Empty<LightsDownHole>(), false);
            var gentle = Shoot("fullscreen-mouse-moving-gentle", LightsDownFullScreen.Look(Moving(0.2, 0.75), t, LightsDownMotion.Full, H), Array.Empty<LightsDownHole>(), false);
            Assert.True(Lum(moving, 60, H / 2) > Lum(rest, 60, H / 2), "the band pulled back toward the edge");
            Assert.True(Lum(gentle, 60, H / 2) < Lum(moving, 60, H / 2), "gentle pulls back more slowly");
        });
    }

    [Fact]
    public void One_floating_video_keeps_a_clear_hole_with_a_halo_in_a_dimmed_room()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var win = new Rect(620, 330, 640, 360);
            var plain = Shoot("floating-plain-desktop", LightsDownLook.None, Array.Empty<LightsDownHole>(), true, new[] { win });
            var early = LightsDownFloating.Look(0.35, 1, LightsDownTuning.Stock, LightsDownMotion.Full, H, Pink);
            Shoot("floating-one-early", early, Holes(new[] { win }).Current, true, new[] { win });
            var look = LightsDownFloating.Look(1, 1, LightsDownTuning.Stock, LightsDownMotion.Full, H, Pink);
            var rtb = Shoot("floating-one", look, Holes(new[] { win }).Current, true, new[] { win });

            Assert.Equal(At(plain, 940, 510), At(rtb, 940, 510));
            Assert.True(Lum(rtb, 100, 100) < Lum(plain, 100, 100) * 0.5, "the room is down");
            var (r, g, b) = At(rtb, (int)win.Right + 12, 510);
            Assert.True(r > g + 25 && r > 70, $"a pink halo by the window ({r},{g},{b})");
            Assert.True(Lum(rtb, (int)win.Right + 12, 510) < Lum(plain, (int)win.Right + 12, 510), "the halo sits in the dim, it is not a light");
        });
    }

    [Fact]
    public void Three_floating_videos_two_overlapping_blend_into_one_dim()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var wins = new[] { new Rect(160, 140, 560, 315), new Rect(600, 360, 560, 315), new Rect(1260, 620, 480, 270) };
            var look = LightsDownFloating.Look(0.8, 1, LightsDownTuning.Stock, LightsDownMotion.Full, H, 0x9B59FF);
            Shoot("floating-three", look, Holes(wins).Current, true, wins);
            var rtb = Shoot("floating-three-flat", look, Holes(wins).Current, true, wins, flat: true);
            // Around and between the overlapping pair: no hard line anywhere in the dim, halos or holes
            // (rows and columns walked pixel by pixel, the windows themselves skipped).
            bool OnWindow(int x, int y)
            {
                foreach (var w in wins) if (x >= w.X - 1 && x <= w.Right + 1 && y >= w.Y - 1 && y <= w.Bottom + 1) return true;
                return false;
            }
            foreach (int y in new[] { 120, 340, 470, 600, 700, 900 })
                for (int x = 1; x < W; x++)
                {
                    if (OnWindow(x, y) || OnWindow(x - 1, y)) continue;
                    var (r0, g0, b0) = At(rtb, x - 1, y);
                    var (r1, g1, b1) = At(rtb, x, y);
                    int jump = Math.Max(Math.Abs(r1 - r0), Math.Max(Math.Abs(g1 - g0), Math.Abs(b1 - b0)));
                    Assert.True(jump <= 12, $"a hard step of {jump} at ({x},{y})");
                }
            Assert.True(Lum(rtb, 1800, 120) < 90, "a far corner is down");
        });
    }
}
