using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Web videos on every chosen screen again (ccp-bugs #1639): live copies of the one
/// fullscreen host on the other "Show content on" screens, never the display clone (#1565).</summary>
public class BrowserFullscreenMirrorTests
{
    [Fact]
    public void Every_other_content_screen_gets_a_copy()
        => Assert.Equal(new[] { @"\\.\DISPLAY2", @"\\.\DISPLAY3" },
            BrowserFullscreenMirrorRule.ExtraScreens(@"\\.\DISPLAY1", new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2", @"\\.\DISPLAY3" }));

    [Fact]
    public void One_content_screen_means_no_copies()
        => Assert.Empty(BrowserFullscreenMirrorRule.ExtraScreens(@"\\.\DISPLAY1", new[] { @"\\.\DISPLAY1" }));

    [Fact]
    public void A_host_outside_the_content_screens_copies_to_all_of_them()
        => Assert.Equal(new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" },
            BrowserFullscreenMirrorRule.ExtraScreens(@"\\.\DISPLAY3", new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" }));

    [Fact]
    public void Content_pinned_to_one_screen_never_spreads()
        // A popout fullscreened on a screen that shows no content: still no copy (the 7.2.3 rule).
        => Assert.Empty(BrowserFullscreenMirrorRule.ExtraScreens(@"\\.\DISPLAY2", new[] { @"\\.\DISPLAY1" }));

    [Fact]
    public void Names_ignore_case_and_blanks_and_repeats()
        => Assert.Equal(new[] { @"\\.\DISPLAY2" },
            BrowserFullscreenMirrorRule.ExtraScreens(@"\\.\display1",
                new[] { @"\\.\DISPLAY1", "", null, @"\\.\DISPLAY2", @"\\.\display2" }));

    [Theory]
    [InlineData(1920, 1080, 1920, 1080, 0, 0, 1920, 1080)]     // same screen size: full
    [InlineData(1920, 1080, 1920, 1200, 0, 60, 1920, 1140)]    // 16:9 on 16:10: bars top and bottom
    [InlineData(1920, 1080, 3440, 1440, 440, 0, 3000, 1440)]   // 16:9 on ultrawide: bars left and right
    [InlineData(3840, 2160, 1280, 1024, 0, 152, 1280, 872)]    // 4K host on a small 5:4 screen
    [InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1080)]           // no source size yet: whole screen
    public void The_copy_fits_the_whole_picture_never_crops(int sw, int sh, int dw, int dh, int l, int t, int r, int b)
        => Assert.Equal((l, t, r, b), BrowserFullscreenMirrorRule.FitRect(sw, sh, dw, dh));

    private static string Source(params string[] parts)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "ConditioningControlPanel", "Resources"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(new[] { root!.FullName, "ConditioningControlPanel" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Fullscreen_never_clones_the_displays_again()
    {
        var browser = Source("MainWindow", "MainWindow.Browser.cs");
        // The host file still explains the old clone in a comment; no call may come back.
        Assert.DoesNotContain("EnableMirror(", browser);
        Assert.DoesNotContain("SetDisplayConfig(", browser);
        var mirrors = Source("Services", "Browser", "BrowserFullscreenMirrors.cs");
        Assert.DoesNotContain("SetDisplayConfig", mirrors);
        // No second player: the copies carry no sound and decode nothing.
        Assert.DoesNotContain("LibVLC", mirrors);
    }

    [Fact]
    public void Copies_open_after_the_maximize_and_close_with_every_exit()
    {
        var browser = Source("MainWindow", "MainWindow.Browser.cs");
        // Queued, never run inside the switch (#1565 #1596 nested pump).
        int later = browser.IndexOf("private void ShowBrowserFullscreenMirrorsLater(", StringComparison.Ordinal);
        Assert.True(later > 0);
        Assert.StartsWith("private void ShowBrowserFullscreenMirrorsLater(Window host, int transition)",
            browser[later..]);
        Assert.Contains("Dispatcher.BeginInvoke(DispatcherPriority.Background", browser.Substring(later, 300));
        int enter = browser.IndexOf("public void EnterBrowserFullscreen()", StringComparison.Ordinal);
        int exit = browser.IndexOf("private void ExitBrowserFullscreen()", StringComparison.Ordinal);
        Assert.True(enter > 0 && exit > enter);
        Assert.Contains("ShowBrowserFullscreenMirrorsLater(_browserPopoutWindow, transition);", browser[enter..exit]);
        // Closed before the early return, so an exit after the host closed itself still clears them.
        var exitBody = browser[exit..];
        int close = exitBody.IndexOf("CloseBrowserFullscreenMirrors();", StringComparison.Ordinal);
        int early = exitBody.IndexOf("if (!_isBrowserFullscreen) return;", StringComparison.Ordinal);
        Assert.True(close > 0 && close < early);
    }

    [Fact]
    public void Copies_are_owned_by_the_host_and_never_take_focus()
    {
        var mirrors = Source("Services", "Browser", "BrowserFullscreenMirrors.cs");
        Assert.Contains("cp.Parent = _ownerHwnd;", mirrors);
        Assert.Contains("WS_EX_NOACTIVATE", mirrors);
        Assert.Contains("ShowWithoutActivation => true", mirrors);
        // A GDI window: no WPF render target the size of a monitor (#1504 #1510 #1513).
        Assert.Contains("WF.Form", mirrors);
    }
}
