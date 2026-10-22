using System;
using System.IO;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The 7.1.6 overlay fixes: a red bubble's press no longer reaches the app behind it
/// (ccp-bugs #1406), a bubble over a flash takes the click (#1416), the user's corner GIFs run
/// only with the engine (#1399), and the Pink Rush wash is no longer a per-pixel window the size
/// of the desktop (#1415).
/// </summary>
public class OverlayFixes716Tests
{
    // ---- #1406: the red bubble's let-through press is caught ----

    [Theory]
    [InlineData(true, false, true)]    // red bubble on the ambient field: caught
    [InlineData(true, true, false)]    // live defuse bubble in a chaos run: unchanged
    [InlineData(false, false, false)]  // plain bubble: swallowed by the hook, nothing to catch
    [InlineData(false, true, false)]
    public void Only_an_ambient_let_through_press_is_caught(bool passesThrough, bool chaos, bool expected)
        => Assert.Equal(expected, BubbleService.CatchesPassedPress(passesThrough, chaos));

    [Fact]
    public void The_catcher_stays_while_the_button_is_down_and_goes_on_release()
    {
        Assert.False(BubblePressCatcher.ShouldRelease(gotDown: true, buttonHeld: true, shownForMs: 900));
        Assert.True(BubblePressCatcher.ShouldRelease(gotDown: true, buttonHeld: false, shownForMs: 900));
    }

    [Fact]
    public void A_catcher_that_never_got_its_press_does_not_linger()
    {
        Assert.False(BubblePressCatcher.ShouldRelease(false, true, BubblePressCatcher.NoPressTimeoutMs - 1));
        Assert.True(BubblePressCatcher.ShouldRelease(false, true, BubblePressCatcher.NoPressTimeoutMs));
        // ...and nothing keeps it past the hard limit, not even a button that reads as held.
        Assert.True(BubblePressCatcher.ShouldRelease(true, true, BubblePressCatcher.HardTimeoutMs));
    }

    /// <summary>The whole point: once placed, the catcher is the window a press at that point
    /// goes to (a click-through or fully transparent window would be skipped).</summary>
    [Fact]
    public void The_catcher_is_what_a_press_at_its_point_lands_on()
    {
        if (!Environment.UserInteractive) return;   // no desktop to hit-test against
        using var win = new NativeLayeredWindow(0, 0, 0, clickThrough: false);
        if (win.Handle == IntPtr.Zero) return;      // window station refuses windows: nothing to pin
        win.SetAlpha(1);
        win.ShowAt(40, 40, 60, 60);
        try
        {
            Assert.Equal(win.Handle, WindowFromPoint(new POINT { X = 70, Y = 70 }));
        }
        finally { win.Hide(); }
    }

    [Fact]
    public void A_click_through_wash_is_never_what_a_press_lands_on()
    {
        if (!Environment.UserInteractive) return;
        using var win = new NativeLayeredWindow(0xFF, 0x14, 0x93, clickThrough: true);
        if (win.Handle == IntPtr.Zero) return;
        win.SetAlpha(1);
        win.ShowAt(40, 40, 60, 60);
        try
        {
            Assert.NotEqual(win.Handle, WindowFromPoint(new POINT { X = 70, Y = 70 }));
        }
        finally { win.Hide(); }
    }

    // ---- #1416: a bubble over a flash takes the click ----

    [Fact]
    public void A_point_on_a_host_bubble_is_reported_and_a_miss_is_not()
    {
        var saved = BubbleService.ChaosClickDiscsSnapshot;
        try
        {
            BubbleService.ChaosClickDiscsSnapshot = new[] { (500.0, 400.0, 50.0, false) };
            Assert.True(BubbleService.HostBubbleAt(500, 400));
            Assert.True(BubbleService.HostBubbleAt(540, 400));
            Assert.False(BubbleService.HostBubbleAt(560, 400));

            BubbleService.ChaosClickDiscsSnapshot = Array.Empty<(double, double, double, bool)>();
            Assert.False(BubbleService.HostBubbleAt(500, 400));
        }
        finally { BubbleService.ChaosClickDiscsSnapshot = saved; }
    }

    [Fact]
    public void The_flash_hook_asks_for_a_bubble_before_it_claims_a_click()
    {
        var body = MemberBody(Source("Services", "Flash", "FlashService.cs"), "private bool OnLayerFlashDown(");
        var ask = body.IndexOf("BubbleService.HostBubbleAt(", StringComparison.Ordinal);
        var claim = body.IndexOf("_layerHits", StringComparison.Ordinal);
        Assert.True(ask >= 0, "the flash hook no longer yields to a bubble drawn above it");
        Assert.True(ask < claim, "the bubble check must come before the flash hit-test");
    }

    // ---- #1399: corner GIFs follow the engine ----

    [Fact]
    public void The_engine_raises_the_corner_gifs_on_start_and_closes_them_on_stop()
    {
        var src = Source("MainWindow", "MainWindow.StartStop.cs");
        Assert.Contains("App.CornerGif?.RefreshOverlays()", MemberBody(src, "public void StartEngine("));
        Assert.Contains("App.CornerGif?.StopAll()", MemberBody(src, "private void StopEngineCore("));
    }

    // ---- #1415: the Pink Rush wash ----

    [Fact]
    public void The_wash_keeps_its_old_strength_and_fades_straight_to_nothing()
    {
        // The old window: a 100/255 fill at 0.6 opacity.
        Assert.Equal(60, ScreenWash.AlphaAt(0, ScreenWash.PinkRushFadeMs, ScreenWash.PinkRushPeak));
        Assert.Equal(30, ScreenWash.AlphaAt(250, ScreenWash.PinkRushFadeMs, ScreenWash.PinkRushPeak));
        Assert.Equal(0, ScreenWash.AlphaAt(500, ScreenWash.PinkRushFadeMs, ScreenWash.PinkRushPeak));
        Assert.Equal(0, ScreenWash.AlphaAt(9000, ScreenWash.PinkRushFadeMs, ScreenWash.PinkRushPeak));
        Assert.Equal(0, ScreenWash.AlphaAt(double.NaN, ScreenWash.PinkRushFadeMs, ScreenWash.PinkRushPeak));
    }

    [Fact]
    public void Pink_rush_never_opens_a_per_pixel_window_across_the_desktop()
    {
        var body = MemberBody(Source("MainWindow", "MainWindow.Enhancements.cs"), "private void OnPinkRushStarted(");
        Assert.Contains("ScreenWash.Flash(", body);
        Assert.DoesNotContain("AllowsTransparency", body.Replace("never a WPF AllowsTransparency", ""));
        Assert.DoesNotContain("VirtualScreenWidth", body);
    }

    // ---- helpers ----

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        var path = Path.Combine(dir!.FullName, "ConditioningControlPanel");
        foreach (var p in parts) path = Path.Combine(path, p);
        return File.ReadAllText(path);
    }

    /// <summary>From a member's signature to the next member's doc comment.</summary>
    private static string MemberBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " was renamed - update this test with it");
        var end = source.IndexOf("        /// <summary>", start, StringComparison.Ordinal);
        return end > start ? source[start..end] : source[start..];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
}
