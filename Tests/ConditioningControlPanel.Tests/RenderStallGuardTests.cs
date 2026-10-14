using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The 6.11.4 render-stall family: ccp-bugs #1312 (lucky toast hang in the Back Room), #1295 /
/// #1310 (UI thread stuck in XamlAnimatedGif's WriteableBitmap.Lock). The Brain Parasite save
/// throttle (#1311) ships separately in PR #1817.
/// </summary>
public class RenderStallGuardTests
{
    // ---- #1312 lucky toast ----

    // The pure Decide facts moved with LuckyToastRule to Tests/CCP.Core.Tests/LuckyToastRuleTests.cs.

    [Fact]
    public void LuckyToast_HandlerNeverBuildsAWindowPerProc()
    {
        var src = ReadSource("MainWindow", "MainWindow.Enhancements.cs");
        int start = src.IndexOf("private void OnLuckyProc(", StringComparison.Ordinal);
        int end = src.IndexOf("private Window EnsureLuckyToast()", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "OnLuckyProc / EnsureLuckyToast not found");
        var handler = src.Substring(start, end - start);
        Assert.DoesNotContain("new Window", handler);
        Assert.Contains("LuckyToastRule.Decide", handler);
    }

    // ---- #1295 / #1310 overlay GIFs off XamlAnimatedGif ----

    [Theory]
    [InlineData("Chaos", "ChaosGifCascadeOverlay.cs")]
    [InlineData("Services", "BlinkTrainerService.cs")]
    public void OverlayGifs_NoLongerStartAXamlAnimatedGifAnimator(string folder, string file)
    {
        var src = ReadSource(folder, file);
        // Teardown may still clear a source with SetSourceUri(x, null); nothing may START one.
        Assert.DoesNotContain("AnimationBehavior.SetSourceUri(img, new Uri", src);
        Assert.DoesNotContain("AnimationBehavior.SetAutoStart(", src);
        Assert.DoesNotContain("AnimationBehavior.SetRepeatBehavior(", src);
    }

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        var path = Path.Combine(dir!.FullName, "ConditioningControlPanel");
        foreach (var p in parts) path = Path.Combine(path, p);
        return File.ReadAllText(path);
    }
}
