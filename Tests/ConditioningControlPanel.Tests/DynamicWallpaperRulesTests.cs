using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The moving wallpaper's rules that need no GPU: panic never waits on it, what it keeps in
/// memory, how Gentle slows it, and when a window pauses it.</summary>
public class DynamicWallpaperRulesTests
{
    [Fact]
    public void Dynamic_stop_never_waits_on_the_ui_thread()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video", "DynamicWallpaperService.cs"));
        int start = src.IndexOf("public void Stop()", StringComparison.Ordinal);
        int task = src.IndexOf("_stopTask = Task.Run(", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && task > start);
        var ui = src.Substring(start, task - start);
        Assert.DoesNotContain(".Join(", ui);
        Assert.DoesNotContain(".Wait(", ui);
    }

    // ---- the moving wallpaper's budgets

    [Theory]
    [InlineData(2, 1)]
    [InlineData(120, 1)]
    [InlineData(121, 2)]
    [InlineData(240, 2)]
    [InlineData(600, 5)]
    public void A_long_gif_keeps_every_nth_frame(int frames, int stride)
    {
        Assert.Equal(stride, DynamicFrameBudget.Stride(frames));
        Assert.True(frames / stride <= DynamicFrameBudget.MaxFramesKept);
    }

    [Fact]
    public void Frames_that_do_not_fit_the_budget_stay_still()
    {
        long cost = DynamicFrameBudget.Cost(320, 320, 100);
        Assert.Equal(320L * 320 * 4 * 100, cost);
        Assert.True(DynamicFrameBudget.MayMove(100, cost, cost));
        Assert.False(DynamicFrameBudget.MayMove(100, cost, cost - 1));
        Assert.False(DynamicFrameBudget.MayMove(1, 0, cost));
        Assert.False(DynamicFrameBudget.MayMove(DynamicFrameBudget.MaxSourceFrames + 1, 0, cost));
    }

    [Fact]
    public void Gentle_speed_stretches_the_frame_delays()
    {
        var delays = new[] { 100, 100 };
        int cur = 0;
        double clock = 0;
        Assert.False(DynamicFrameBudget.Advance(delays, ref cur, ref clock, 100, 0.5));   // 200 ms at half speed
        Assert.True(DynamicFrameBudget.Advance(delays, ref cur, ref clock, 100, 0.5));
        Assert.Equal(1, cur);
    }

    [Fact]
    public void A_still_tile_never_reports_a_change()
    {
        int cur = 0;
        double clock = 0;
        Assert.False(DynamicFrameBudget.Advance(new[] { 0 }, ref cur, ref clock, 1000, 1));
    }

    [Theory]
    //          left, top, right, bottom, zoomed, covered
    [InlineData(0, 0, 1920, 1080, false, true)]      // true fullscreen
    [InlineData(-8, -8, 1928, 1048, true, true)]     // maximised: stops at the taskbar
    [InlineData(-8, -8, 1928, 1048, false, false)]   // the same size but not zoomed leaves the taskbar edge open
    [InlineData(100, 100, 900, 700, false, false)]   // an ordinary window
    public void Only_a_window_hiding_the_whole_monitor_pauses_the_draw(int l, int t, int r, int b, bool zoomed, bool covered)
        => Assert.Equal(covered, DynamicFrameBudget.Covers(l, t, r, b, zoomed, 0, 0, 1920, 1080));

    // ---- #1544: a portrait monitor crops the shared collage, never squashes it

    [Theory]
    [InlineData(1920, 1080)]   // same as the collage
    [InlineData(1080, 1920)]   // portrait
    [InlineData(3440, 1440)]   // ultrawide
    [InlineData(1280, 1024)]   // 5:4
    public void Every_monitor_gets_one_uniform_scale_that_covers_it(int monW, int monH)
    {
        var (k, ox, oy) = DynamicFrameBudget.CoverFit(monW, monH, 1920, 1080);
        float drawnW = 1920 * k, drawnH = 1080 * k;
        Assert.True(drawnW >= monW - 0.01f && drawnH >= monH - 0.01f);          // no black edge
        Assert.True(Math.Abs(drawnW - monW) < 0.01f || Math.Abs(drawnH - monH) < 0.01f);   // crops one axis only
        Assert.Equal((monW - drawnW) / 2f, ox, 2);                               // centred
        Assert.Equal((monH - drawnH) / 2f, oy, 2);
    }

    [Fact]
    public void The_render_no_longer_scales_each_axis_on_its_own()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video", "DynamicWallpaperService.cs"));
        Assert.DoesNotContain("s.Width / c.RefW, s.Height / c.RefH", src);
        Assert.Contains("DynamicFrameBudget.CoverFit(", src);
    }

    // ---- one collage per monitor (McKenzi, 7.2.1: the second monitor did not scale)

    [Fact]
    public void Each_monitor_finds_the_collage_planned_for_it()
    {
        var planned = new[] { (0, 0, 2560, 1440), (2560, 0, 1920, 1080), (-1080, -200, 1080, 1920) };
        Assert.Equal(0, DynamicFrameBudget.MatchMonitor(planned, 0, 0, 2560, 1440));
        Assert.Equal(1, DynamicFrameBudget.MatchMonitor(planned, 2560, 0, 1920, 1080));
        Assert.Equal(2, DynamicFrameBudget.MatchMonitor(planned, -1080, -200, 1080, 1920));
    }

    [Fact]
    public void A_monitor_that_moved_since_the_build_takes_the_one_it_overlaps_most()
    {
        var planned = new[] { (0, 0, 2560, 1440), (2560, 0, 1920, 1080) };
        Assert.Equal(1, DynamicFrameBudget.MatchMonitor(planned, 2560, 0, 2560, 1440));   // resized in place
        Assert.Equal(0, DynamicFrameBudget.MatchMonitor(planned, 9000, 0, 1920, 1080));   // nowhere near: the first
        Assert.Equal(-1, DynamicFrameBudget.MatchMonitor(Array.Empty<(int, int, int, int)>(), 0, 0, 1920, 1080));
    }

    [Fact]
    public void A_collage_planned_for_its_own_monitor_is_drawn_one_to_one()
    {
        foreach (var (w, h) in new[] { (2560, 1440), (1920, 1080), (1080, 1920), (3440, 1440) })
            Assert.Equal((1f, 0f, 0f), DynamicFrameBudget.CoverFit(w, h, w, h));
    }

    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1920, 1080)]
    [InlineData(1080, 1920)]   // portrait
    [InlineData(3440, 1440)]   // ultrawide
    public void Every_layout_fits_the_monitor_it_is_planned_for(int w, int h)
    {
        foreach (CollageLayout layout in Enum.GetValues(typeof(CollageLayout)))
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var slots = CollageComposer.PlanLayout(layout, w, h, new Random(seed));
                Assert.NotEmpty(slots);
                foreach (var s in slots)
                {
                    Assert.InRange(s.Cx, 0f, w);   // every tile centred on this monitor
                    Assert.InRange(s.Cy, 0f, h);
                    if (s.Kind == CollageSlotKind.Hero)
                    {
                        Assert.True(s.HeroW <= w * 0.5f && s.HeroH <= h);
                        Assert.Equal(s.HeroW, s.TileSize(1, 1).W);   // the hero box keeps its own shape
                    }
                    else
                    {
                        var (tw, th) = s.TileSize(1200, 900);       // a tile keeps its picture's shape
                        Assert.Equal(1200f / 900f, tw / (float)th, 1);
                    }
                }
                var fill = slots.Where(s => s.Kind == CollageSlotKind.Fill).ToList();
                Assert.True(fill.Min(s => s.Cy) < h * 0.25f && fill.Max(s => s.Cy) > h * 0.75f);   // top to bottom
            }
        }
    }

    [Fact]
    public void Dynamic_plans_and_draws_a_collage_per_monitor()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video");
        var build = File.ReadAllText(Path.Combine(dir, "DynamicWallpaperService.Build.cs"));
        var render = File.ReadAllText(Path.Combine(dir, "DynamicWallpaperService.cs"));
        Assert.Contains("Screen.AllScreens", build);
        Assert.DoesNotContain("var (w, h) = CollageComposer.CanvasSize();\r\n            var rng", build);
        Assert.Contains("GroupBy(sl => sl.Src!.Path", build);   // one decode per file, however many monitors
        Assert.Contains("c.For(s)", render);
    }

    [Theory]
    [InlineData("10", "0", "Fill")]
    [InlineData("2", "0", "Stretch")]
    [InlineData("22", "0", "Span")]
    [InlineData("6", "0", "Fit")]
    [InlineData("0", "1", "Tile")]
    [InlineData("0", "0", "Center")]
    [InlineData("", "", "unknown")]
    public void The_log_names_the_users_own_desktop_fit(string style, string tile, string name)
        => Assert.Equal(name, WallpaperService.WallpaperStyleName(style, tile));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "App.xaml.cs")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
