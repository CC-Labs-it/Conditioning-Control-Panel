using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class SparkleFlightTests
{
    [Theory]
    [InlineData(1, MotionLevel.Full, 10)]
    [InlineData(10, MotionLevel.Full, 17)]
    [InlineData(500, MotionLevel.Full, 30)]
    [InlineData(500, MotionLevel.Reduced, 15)]
    [InlineData(-100, MotionLevel.Full, 10)]
    [InlineData(-2000, MotionLevel.Full, 22)]
    [InlineData(25, MotionLevel.Off, 0)]
    [InlineData(0, MotionLevel.Full, 0)]
    public void StarCountsFollowTheMockup(int amount, MotionLevel level, int stars)
        => Assert.Equal(stars, SparkleFlightPlan.StarCount(amount, level));

    [Fact]
    public void TheCountClimbsAsTheyLandAndTheLastStandsOnTheTruth()
    {
        var shown = Enumerable.Range(0, 18).Select(k => SparkleFlightPlan.CountAt(100, 117, k, 17)).ToArray();
        Assert.Equal(100, shown[0]);
        Assert.Equal(117, shown[17]);
        Assert.True(shown.Zip(shown.Skip(1)).All(p => p.Second >= p.First));
        Assert.Equal(new[] { 0, 0, 3, 7, 7 }, new[] { 0.0, 0.1, 0.4, 0.99, 3.0 }.Select(SparkleFlightPlan.BellFor));
    }

    [Fact]
    public void PathsAreSeededStartAtTheSpotAndEndOnTheWallet()
    {
        var a = SparkleFlightPlan.Gain(400, 600, 50, 20, 12, new Random(7));
        var b = SparkleFlightPlan.Gain(400, 600, 50, 20, 12, new Random(7));
        Assert.Equal(a, b);
        foreach (var s in a)
        {
            Assert.Null(SparkleFlightPlan.Pose(s, s.DelayMs - 1));
            var end = SparkleFlightPlan.Pose(s, s.DelayMs + s.DurMs)!.Value;
            Assert.Equal(50, end.X, 6); Assert.Equal(20, end.Y, 6);
            Assert.InRange(s.DelayMs + s.DurMs, 1150, 2700);    // the mockup's 1.2 to 2.7 s
        }
        var leave = SparkleFlightPlan.Spend(50, 20, 400, 600, 10, new Random(3));
        Assert.All(leave, s => Assert.Equal(0, SparkleFlightPlan.Pose(s, s.DelayMs + s.DurMs)!.Value.Alpha, 6));
        Assert.Equal(0, SparkleFlightPlan.Ease(0, .42, 0, .3, 1)); Assert.Equal(1, SparkleFlightPlan.Ease(1, .42, 0, .3, 1));
    }

    [Theory]
    [InlineData(5, 100, 105, SparkleSource.BackRoom, true)]
    [InlineData(-40, 100, 60, SparkleSource.CounterBuy, true)]
    [InlineData(900, 0, 900, SparkleSource.Synced, false)]      // first fill after a sign-in
    [InlineData(-60, 60, 0, SparkleSource.Reset, false)]
    public void WhatFlies(int amount, int before, int after, SparkleSource source, bool flies)
        => Assert.Equal(flies, SparkleFlightDirector.ShouldFly(new SparkleChange(amount, before, after, source)));

    /// <summary>The rendered clips ship, are short, never clip, and the tick bells sit under the chord.</summary>
    [Fact]
    public void TheBellsAreRenderedQuietClips()
    {
        var dir = Path.Combine(AppDir(), "Resources", "sounds", "sparkles");
        double Peak(string name)
        {
            var bytes = File.ReadAllBytes(Path.Combine(dir, name + ".wav"));
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.InRange(bytes.Length, 1000, 200_000);
            var peak = 0;
            for (var i = 44; i + 1 < bytes.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(bytes, i)));
            return peak / 32767.0;
        }
        var chord = Peak("chord");
        Assert.InRange(chord, 0.5, 0.91);
        for (var k = 0; k < SparkleFlightPlan.Bells; k++) Assert.InRange(Peak($"bell_{k:00}"), 0.05, chord);
        Assert.True(Peak("air_up") < chord && Peak("air_down") < chord);
        var sfx = File.ReadAllText(Path.Combine(AppDir(), "Services", "Prizes", "SparkleSfx.cs"));
        Assert.Contains("LauncherSoundEnabled", sfx);     // the launcher's speaker button mutes them
        Assert.Contains("PlayOneShot", sfx);              // sample gain, never the device volume
        Assert.DoesNotContain("WaveOutEvent", sfx);
    }

    /// <summary>A still of a flight mid-air: CCP_FLY_DUMP=&lt;dir&gt; writes it as a PNG.</summary>
    [Fact]
    public void A_still_of_the_flight_renders()
    {
        var dump = Environment.GetEnvironmentVariable("CCP_FLY_DUMP");
        var starFile = Path.Combine(AppDir(), "Resources", "features", "sparkle_star.png");
        WpfRenderHarness.OnStaThread(() =>
        {
            var star = new BitmapImage(new Uri(starFile));
            var paths = SparkleFlightPlan.Gain(320, 330, 560, 30, 30, new Random(11));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x27, 0x1A, 0x32)), null, new Rect(0, 0, 640, 380));
                dc.DrawImage(star, new Rect(548, 18, 24, 24));
                foreach (var s in paths)
                {
                    if (SparkleFlightPlan.Pose(s, 900) is not { } p || p.Alpha <= 0.01) continue;
                    var half = SparkleFlightPlan.StarPx * p.Scale / 2;
                    var m = Matrix.Identity; m.Rotate(p.Angle); m.Translate(p.X, p.Y);
                    dc.PushTransform(new MatrixTransform(m)); dc.PushOpacity(p.Alpha);
                    dc.DrawImage(star, new Rect(-half, -half, half * 2, half * 2));
                    dc.Pop(); dc.Pop();
                }
            }
            var bmp = new RenderTargetBitmap(640, 380, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            Assert.True(paths.Count(s => SparkleFlightPlan.Pose(s, 900) is { Alpha: > 0.5 }) >= 10);
            if (string.IsNullOrEmpty(dump)) return;
            Directory.CreateDirectory(dump);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bmp));
            using var f = File.Create(Path.Combine(dump, "flight-900ms.png"));
            png.Save(f);
        });
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
