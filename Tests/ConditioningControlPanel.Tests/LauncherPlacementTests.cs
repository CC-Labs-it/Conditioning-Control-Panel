using ConditioningControlPanel.Services.Launcher;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1548: the launcher's grow in physical pixels on a mixed three-monitor desktop, and
/// the paint probe that tells a flat, never-presented window from the real one.
/// </summary>
public class LauncherPlacementTests
{
    // The reporter's desktop: 2560x1440 primary, two 1920x1080 to the right (taskbar 48 px).
    private static readonly PxRect Big = new(0, 0, 2560, 1392);
    private static readonly PxRect Side = new(2560, 0, 1920, 1032);

    [Fact]
    public void Grow_keeps_the_centre_on_a_tall_monitor()
    {
        var win = new PxRect(640, 296, 1280, 800);
        var r = LauncherPlacement.GrowWithin(win, Big, 1000);
        Assert.Equal(new PxRect(640, 196, 1280, 1000), r);
    }

    [Fact]
    public void Grow_is_capped_at_the_work_area_of_a_1080_monitor_at_125_percent()
    {
        // 800 DIP at 1.25 = 1000 px; the grow asks 1250 px, the work area holds 1032.
        var win = new PxRect(2880, 16, 1600, 1000);
        var r = LauncherPlacement.GrowWithin(win, Side, LauncherPlacement.ToPx(1000, 1.25));
        Assert.Equal(1032, r.Height);
        Assert.Equal(0, r.Top);
        Assert.True(r.Bottom <= Side.Bottom);
    }

    [Fact]
    public void Grow_pulls_a_window_that_straddles_a_seam_onto_its_monitor()
    {
        // Centred by WPF across the seam between the big monitor and the first side one.
        var win = new PxRect(1900, 300, 1280, 800);
        var r = LauncherPlacement.GrowWithin(win, Big, 1000);
        Assert.Equal(Big.Right - 1280, r.Left);
        Assert.True(r.Left >= Big.Left && r.Right <= Big.Right);
        Assert.True(r.Top >= Big.Top && r.Bottom <= Big.Bottom);
    }

    [Fact]
    public void Grow_never_shrinks_and_never_leaves_the_top()
    {
        var win = new PxRect(100, 0, 1280, 900);
        var r = LauncherPlacement.GrowWithin(win, Big, 700);
        Assert.Equal(900, r.Height);
        Assert.Equal(0, r.Top);
    }

    [Fact]
    public void Grow_narrows_a_window_wider_than_the_work_area()
    {
        var tiny = new PxRect(0, 0, 1000, 700);
        var r = LauncherPlacement.GrowWithin(new PxRect(0, 0, 1280, 680), tiny, 900);
        Assert.Equal(1000, r.Width);
        Assert.Equal(700, r.Height);
    }

    [Theory]
    [InlineData(1000, 1.0, 1000)]
    [InlineData(1000, 1.25, 1250)]
    [InlineData(1000, 1.5, 1500)]
    [InlineData(825.4, 1.25, 1032)]
    [InlineData(0, 1.5, 0)]
    public void Dips_to_pixels(double dip, double scale, int px)
    {
        Assert.Equal(px, LauncherPlacement.ToPx(dip, scale));
    }

    [Fact]
    public void Probe_points_stay_inside_the_window()
    {
        var win = new PxRect(13, 12, 1280, 1000);
        var pts = LauncherPlacement.ProbePoints(win);
        Assert.Equal(9, pts.Count);
        foreach (var (x, y) in pts)
        {
            Assert.InRange(x, win.Left + 20, win.Right - 20);
            Assert.InRange(y, win.Top + 20, win.Bottom - 20);
        }
    }

    [Fact]
    public void The_reported_screenshot_reads_as_blank()
    {
        // #0B0710 at every sample, as in the #1548 screenshot.
        var flat = new[] { 0x0B0710, 0x0B0710, 0x0B0710, 0x0B0710, 0x0B0710, 0x0B0710, 0x0B0710, 0x0B0710, 0x0B0710 };
        Assert.True(LauncherPlacement.LooksBlank(flat));
    }

    [Fact]
    public void The_real_launcher_backdrop_does_not_read_as_blank()
    {
        // Radial gradient stops (#2C1450 at the top left, #06030E in the far corner) plus a card.
        var real = new[] { 0x2C1450, 0x241040, 0x1C1233, 0x221438, 0x1A1030, 0x100A1E, 0x140C24, 0x100A1E, 0x06030E };
        Assert.False(LauncherPlacement.LooksBlank(real));
    }

    [Fact]
    public void Too_few_samples_are_never_blank()
    {
        Assert.False(LauncherPlacement.LooksBlank(new[] { 0, 0, 0 }));
    }
}
