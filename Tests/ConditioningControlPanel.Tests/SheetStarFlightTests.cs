using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>THE LAST LEG (CONTRACT.md "Sheet additions", wallet-stars): the host reads the page's star paths and draws
/// the same motion above its bar, counting the chip as its copies land or leave.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public sealed class SheetStarFlightTests
{
    /// <summary>A page frame the way sheet/fx.js builds it (fly / flyOut), seeded.</summary>
    internal static JObject PageFrame(bool @out, int n, double sx, double sy, double ex, double ey, int seed)
    {
        var rng = new Random(seed);
        var stars = new JArray();
        for (var i = 0; i < n; i++)
        {
            var size = i % 4 == 0 ? 0.8 : 0.34 + rng.NextDouble() * 0.3;
            var spin = (rng.NextDouble() - 0.5) * 500;
            JArray k;
            double d, ms;
            if (@out)
            {
                double tx = ex + (rng.NextDouble() - 0.5) * 140, ty = ey + (rng.NextDouble() - 0.5) * 60, side = (rng.NextDouble() - 0.5) * 160;
                double ax = sx + (tx - sx) * 0.3 + side, ay = Math.Min(sy, ty) - 8 - rng.NextDouble() * 14, bx = sx + (tx - sx) * 0.7 + side * 0.4, by = ay + (ty - ay) * 0.35;
                k = new JArray(
                    new JArray(0, sx, sy, size * 0.3, 0, 0), new JArray(0.12, sx + (ax - sx) * 0.25, sy + (ay - sy) * 0.25, size * 1.05, spin * 0.1, 1),
                    new JArray(0.42, ax, ay, size * 1.1, spin * 0.4, 1), new JArray(0.7, bx, by, size, spin * 0.7, 1),
                    new JArray(0.9, tx, ty, size * 0.8, spin, 1), new JArray(1, tx, ty - 4, size * 1.4, spin, 0));
                d = i * Math.Min(52, 1100.0 / n) + rng.NextDouble() * 30; ms = 1250 + rng.NextDouble() * 450;
            }
            else
            {
                var a = -Math.PI / 2 + (rng.NextDouble() - 0.5) * 3.6; var burst = 34 + rng.NextDouble() * 70;
                double bx = sx + Math.Cos(a) * burst, by = sy + Math.Sin(a) * burst * 0.8, sway = (rng.NextDouble() - 0.5) * 150;
                double mx = bx + (ex - bx) * 0.55 + sway * 0.35, my = by + (ey - by) * 0.55 - Math.Abs(sway) * 0.3;
                k = new JArray(new JArray(0, sx, sy, size * 0.3, 0, 0), new JArray(0.2, bx, by, size * 1.15, spin * 0.2, 1),
                    new JArray(0.62, mx, my, size, spin * 0.6, 1), new JArray(1, ex, ey, size * 0.55, spin, 1));
                d = i * 44 + rng.NextDouble() * 40; ms = 1150 + rng.NextDouble() * 420;
            }
            stars.Add(new JObject { ["d"] = Math.Round(d), ["ms"] = Math.Round(ms), ["k"] = k });
        }
        return new JObject
        {
            ["type"] = "wallet-stars", ["dir"] = @out ? "out" : "in", ["n"] = @out ? 400 : 20,
            ["e"] = @out ? new JArray(0.45, 0, 0.3, 1) : new JArray(0.42, 0, 0.3, 1), ["stars"] = stars,
        };
    }

    [Fact]
    public void A_frame_reads_into_host_pixels_and_poses_like_the_page()
    {
        var f = SheetStarPlan.Read(PageFrame(false, 12, 400, 300, 820, -34, 3), 16, 70)!;
        Assert.False(f.Out);
        Assert.Equal(20, f.Amount);
        Assert.Equal(12, f.Stars.Count);
        foreach (var s in f.Stars)
        {
            Assert.Null(SheetStarPlan.Pose(f, s, s.DelayMs - 1));
            var a = SheetStarPlan.Pose(f, s, s.DelayMs)!.Value;
            Assert.Equal(416, a.X, 6); Assert.Equal(370, a.Y, 6); Assert.Equal(0, a.Alpha, 6);
            var b = SheetStarPlan.Pose(f, s, s.EndMs)!.Value;
            Assert.Equal(836, b.X, 6); Assert.Equal(36, b.Y, 6);    // the chip: page (820, -34) + origin (16, 70)
            Assert.Null(SheetStarPlan.Pose(f, s, s.EndMs + 1));
        }
        // Mid-way it sits on the frame the page's easing names: offset 0.2 is frame two.
        var one = f.Stars[0];
        var keys = one.Keys;
        double t = Enumerable.Range(0, 2000).Select(i => i / 2000.0).First(x => SparkleFlightPlan.Ease(x, 0.42, 0, 0.3, 1) >= 0.2);
        var mid = SheetStarPlan.Pose(f, one, one.DelayMs + t * one.DurMs)!.Value;
        Assert.Equal(keys[1].X, mid.X, 0); Assert.Equal(keys[1].Y, mid.Y, 0);
    }

    [Fact]
    public void Junk_is_dropped_star_by_star_and_the_flight_is_bounded()
    {
        var m = PageFrame(true, 4, 820, -34, 400, 300, 5);
        var stars = (JArray)m["stars"]!;
        stars.Add(new JObject { ["d"] = 0, ["ms"] = 100, ["k"] = new JArray(new JArray(0.5, 1, 1, 1, 0, 1), new JArray(1, 2, 2, 1, 0, 1)) });   // no start frame
        stars.Add(new JObject { ["d"] = 3900, ["ms"] = 500, ["k"] = new JArray(new JArray(0, 1, 1, 1, 0, 1), new JArray(1, 2, 2, 1, 0, 1)) });  // past the cap
        stars.Add(new JObject { ["d"] = 0, ["ms"] = 500, ["k"] = new JArray(new JArray(0, "x", 1, 1, 0, 1), new JArray(1, 2, 2, 1, 0, 1)) });  // not a number
        stars.Add(new JObject { ["d"] = 0, ["ms"] = 500, ["k"] = new JArray(new JArray(0, 1e9, 1, 1, 0, 1), new JArray(1, 2, 2, 1, 0, 1)) });  // off the map
        stars.Add("nope");
        var f = SheetStarPlan.Read(m, 0, 0)!;
        Assert.True(f.Out);
        Assert.Equal(4, f.Stars.Count);
        Assert.Equal(0.45, f.X1); Assert.Equal(1, f.Y2);
        Assert.Null(SheetStarPlan.Read(new JObject { ["stars"] = new JArray() }, 0, 0));
        Assert.Null(SheetStarPlan.Read(null, 0, 0));
        Assert.Null(SheetStarPlan.Read(m, double.NaN, 0));
        var many = PageFrame(false, 80, 0, 0, 10, 10, 1);
        Assert.Equal(SheetStarPlan.MaxStars, SheetStarPlan.Read(many, 0, 0)!.Stars.Count);
        // A bad easing falls back to the page's gain curve.
        m["e"] = new JArray(2, 0, 0.3, 1);
        Assert.Equal(0.42, SheetStarPlan.Read(m, 0, 0)!.X1);
    }

    [Theory]
    [InlineData(false, 20, 120, true, 100, 120)]    // preview gain: the pretend wallet already moved
    [InlineData(true, 400, 600, true, 1000, 600)]   // preview spend
    [InlineData(false, 20, 100, false, 100, 120)]   // real gain, the ledger change still on its way
    [InlineData(true, 400, 1000, false, 1000, 600)] // real spend
    [InlineData(true, 400, 100, false, 100, 0)]     // never below zero
    public void The_chip_counts_between_honest_numbers(bool @out, int amount, int truth, bool after, int from, int to)
        => Assert.Equal((from, to), SheetStarPlan.Counts(@out, amount, truth, after));

    [Fact]
    public void The_layer_leaves_then_lands_each_star_once()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var layer = new SparkleFlightLayer();
            var f = SheetStarPlan.Read(PageFrame(true, 10, 820, -34, 400, 300, 9), 0, 0)!;
            var left = new List<int>(); var landed = new List<int>();
            layer.Fly(f, left.Add, landed.Add);
            Assert.InRange(left.Count, 0, 3);   // the first frame: only the first stars have left
            Assert.Empty(landed);
            layer.FrameAt(f.Stars.Max(s => s.DelayMs) + 1);
            Assert.Equal(10, left.Count);
            Assert.Empty(landed);
            layer.FrameAt(f.Stars.Max(s => s.EndMs) + 1);
            Assert.Equal(Enumerable.Range(0, 10), landed.OrderBy(i => i));
            Assert.Equal(0, layer.StarsInAir);
            layer.Clear();
        });
    }

    /// <summary>Stills of the last leg (CCP_FLY_DUMP=&lt;dir&gt;): the bar with the chip, the sheet below it drawn by
    /// "the page" (its own copy, clipped to the sheet) and the host's copy on top everywhere but the sheet.</summary>
    [Fact]
    public void Stills_of_the_last_leg_render()
    {
        var dump = Environment.GetEnvironmentVariable("CCP_FLY_DUMP");
        WpfRenderHarness.OnStaThread(() =>
        {
            const int W = 900, H = 560;
            var origin = new Point(16, 70);   // the page's (0, 0) in the window
            var chip = new Point(836, 30);
            foreach (var (name, @out) in new[] { ("gain", false), ("spend", true) })
            {
                var frame = @out ? PageFrame(true, 24, chip.X - origin.X, chip.Y - origin.Y, 420, 260, 4) : PageFrame(false, 24, 420, 300, chip.X - origin.X, chip.Y - origin.Y, 4);
                var host = SheetStarPlan.Read(frame, origin.X, origin.Y)!;
                var page = SheetStarPlan.Read(frame, 0, 0)!;
                var end = host.Stars.Max(s => s.EndMs);
                foreach (var at in new[] { 0.35, 0.55, 0.8 })
                {
                    var ms = end * at;
                    var bmp = Compose(W, H, origin, chip, host, page, ms);
                    Assert.True(bmp.PixelWidth == W);
                    if (string.IsNullOrEmpty(dump)) continue;
                    Directory.CreateDirectory(dump);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bmp));
                    using var file = File.Create(Path.Combine(dump, $"leg-{name}-{(int)(at * 100):00}.png"));
                    png.Save(file);
                }
            }
        });
    }

    private static RenderTargetBitmap Compose(int w, int h, Point origin, Point chip, SheetStarFlight host, SheetStarFlight page, double ms)
    {
        var star = SparkleStar.Shared;
        var sheet = new Rect(origin.X, origin.Y, w - 2 * origin.X, h - origin.Y - 16);
        var v = new DrawingVisual();
        using (var dc = v.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x0A, 0x1E)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x17, 0x40)), null, new Rect(0, 0, w, 52));   // the bar
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x25, 0x52)), null, new Rect(chip.X - 14, chip.Y - 14, 76, 28), 14, 14);
            if (star != null) dc.DrawImage(star, new Rect(chip.X - 11, chip.Y - 11, 22, 22));
            // The host's copy: the whole path (what is over the sheet gets covered next, as the HWND covers it).
            DrawStars(dc, host, ms, star);
            // The page's HWND: opaque, with its own copy of the same stars, clipped to itself.
            dc.PushClip(new RectangleGeometry(sheet, 12, 12));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x17, 0x40)), new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD2, 0x4A)), 4), sheet);
            dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
            DrawStars(dc, page, ms, star);
            dc.Pop();
            dc.Pop();
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(v);
        return bmp;
    }

    private static void DrawStars(DrawingContext dc, SheetStarFlight f, double ms, ImageSource? star)
    {
        foreach (var s in f.Stars)
        {
            if (SheetStarPlan.Pose(f, s, ms) is not { } p || p.Alpha <= 0.01) continue;
            var half = SparkleFlightPlan.StarPx * p.Scale / 2;
            var m = Matrix.Identity; m.Rotate(p.Angle); m.Translate(p.X, p.Y);
            dc.PushTransform(new MatrixTransform(m)); dc.PushOpacity(p.Alpha);
            if (star != null) dc.DrawImage(star, new Rect(-half, -half, half * 2, half * 2));
            else dc.DrawEllipse(Brushes.Gold, null, new Point(0, 0), half, half);
            dc.Pop(); dc.Pop();
        }
    }
}
