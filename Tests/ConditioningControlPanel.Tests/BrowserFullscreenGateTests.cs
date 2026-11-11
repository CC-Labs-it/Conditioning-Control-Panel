using System;
using System.IO;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1565, #1570, #1585, #1589, #1596: maximizing a web video froze or crashed the app.
/// The switch ran inside the WebView2 callback, pumped the dispatcher at Render priority in the
/// middle, and flipped the Windows display topology to clone on every multi-monitor fullscreen.
/// </summary>
public sealed class BrowserFullscreenGateTests
{
    [Fact]
    public void FirstRequest_PostsOneDrain_LaterOnesRideOnIt()
    {
        var gate = new BrowserFullscreenGate();
        Assert.True(gate.Request(true));
        Assert.False(gate.Request(false));
        Assert.False(gate.Request(true));
    }

    [Fact]
    public void LatestWishWins_AndTheSlotFreesAfterTheDrain()
    {
        var gate = new BrowserFullscreenGate();
        gate.Request(true);
        gate.Request(false);
        Assert.False(gate.Take());
        Assert.Null(gate.Take());
        Assert.True(gate.Request(true));
    }

    [Fact]
    public void AnExit_DropsAQueuedEnter()
    {
        var gate = new BrowserFullscreenGate();
        gate.Request(true);
        gate.Clear();
        var wish = gate.Take();
        Assert.Null(wish);
        Assert.False(BrowserFullscreenGate.ShouldApply(wish, isFullscreen: false));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void OnlyAChangeIsApplied(bool wish, bool isFullscreen, bool expected)
        => Assert.Equal(expected, BrowserFullscreenGate.ShouldApply(wish, isFullscreen));

    [Fact]
    public void ANewTransition_MakesOlderDeferredStepsStale()
    {
        var gate = new BrowserFullscreenGate();
        var enter = gate.BeginTransition();
        Assert.True(gate.IsCurrent(enter));
        gate.BeginTransition();
        Assert.False(gate.IsCurrent(enter));
    }

    [Fact]
    public void TheSwitch_NeverPumpsNorClonesTheDisplays()
    {
        var browser = File.ReadAllText(Path.Combine(SourceRoot(), "MainWindow", "MainWindow.Browser.cs"));
        Assert.DoesNotContain("Dispatcher.Invoke(() => { }, DispatcherPriority.Render)", browser);
        Assert.DoesNotContain("ScreenMirror.EnableMirror", browser);
        Assert.DoesNotContain("Dispatcher.Invoke(() => HandleBrowserFullscreenChanged", browser);
        Assert.Contains("_browser.FullscreenChanged += (s, isFullscreen) => RequestBrowserFullscreen(isFullscreen);", browser);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
